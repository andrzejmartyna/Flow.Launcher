using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using BrowserTabs;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public class BrowserTabTracker : IDisposable
{
    private static readonly string ClassName = nameof(BrowserTabTracker);
    private static readonly HashSet<string> chromiumProcessNames = new HashSet<string>(new string[6] { "msedge", "chrome", "brave", "vivaldi", "opera", "chromium" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> firefoxProcessNames = new HashSet<string>(new string[1] { "firefox" }, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownTabs = new();
    private readonly TimeSpan _tabRetryTimeout = TimeSpan.FromSeconds(4);
    private readonly TimeSpan _tabRetryInterval = TimeSpan.FromMilliseconds(250);

    private string? expectedUrl;
    private readonly object sync = new();

    private static string RuntimeIdToKey(int[] id) => string.Join("-", id);
    //private static string RuntimeIdToKey(AutomationElement elem) => elem != null ? $"{RuntimeIdToKey(elem.GetRuntimeId())}-{elem.Current.Name}" : null;
    private static string RuntimeIdToKey(AutomationElement elem) => elem != null ? RuntimeIdToKey(elem.GetRuntimeId()) : null;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public Dictionary<string, BrowserTab> UrlToBrowserTab { get; } = new();

    private AutomationFocusChangedEventHandler? focusHandler;
    private bool initialized;
    private IPublicAPI api;

    public void Init(IPublicAPI api)
    {
        if (initialized)
            return;

        this.api = api;
        focusHandler = OnFocusChanged;
        Automation.AddAutomationFocusChangedEventHandler(focusHandler);
        initialized = true;
    }

    public void Dispose()
    {
        if (focusHandler != null)
            Automation.RemoveAutomationFocusChangedEventHandler(focusHandler);
    }

    public void ExpectUrl(string url)
    {
        lock (sync)
        {
            if (expectedUrl != null)
            {
                api.LogError(ClassName, $"Opening {url} while older is still not resolved ({expectedUrl}). Forgetting the older.");
            }
            expectedUrl = url;
        }
    }

    public void Remove(string url)
    {
        lock (sync)
        {
            UrlToBrowserTab.Remove(url);
        }
    }

    private AutomationElement? TryGetFocusedTabFromWindow(AutomationElement mainWindow)
    {
        AutomationElement? focused = null;
        try
        {
            focused = AutomationElement.FocusedElement;
        }
        catch
        {
            return null;
        }

        if (focused == null)
            return null;

        var walker = TreeWalker.ControlViewWalker;
        var current = focused;
        while (current != null)
        {
            if (current.Equals(mainWindow))
                break;
            current = walker.GetParent(current);
        }

        if (current == null || !current.Equals(mainWindow))
            return null; // focus on a different window/application

        // return to the focused element and go up to TabItem
        current = focused;
        while (current != null)
        {
            if (current.Current.ControlType == ControlType.TabItem || current.Current.ControlType.ProgrammaticName?.Contains("TabItem") == true)
            {
                return current;
            }
            current = walker.GetParent(current);
        }
        return null;
    }

    private BrowserTab? GetCurrentTabFromWindow(AutomationElement mainWindow, Process process, CancellationToken cancellationToken)
    {
        try
        {
            Condition tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

            var sw = Stopwatch.StartNew();
            BrowserTab? result = null;
            int count = 1;

            while (sw.Elapsed < _tabRetryTimeout && !cancellationToken.IsCancellationRequested)
            {
                api.LogDebug(ClassName, $"Start searching for a new tab... Try no {count++}");

                var tabs = mainWindow.FindAll(TreeScope.Descendants, tabCondition);
                if (_knownTabs.Count <= 0)
                {
                    foreach (var tab in tabs)
                    {
                        _knownTabs.Add(RuntimeIdToKey((AutomationElement)tab));
                    }
                    api.LogDebug(ClassName, "Waiting after filling known tabs list");
                    Thread.Sleep(_tabRetryInterval);
                    continue;
                }

                var focusedTabElement = TryGetFocusedTabFromWindow(mainWindow);
                if (focusedTabElement != null && !string.IsNullOrWhiteSpace(focusedTabElement.Current.Name))
                {
                    api.LogDebug(ClassName, $"Focused tab via keyboard focus: {focusedTabElement.Current.Name}");
                    if (_knownTabs.Contains(RuntimeIdToKey(focusedTabElement)))
                    {
                        api.LogDebug(ClassName, "... but the tab is an existing one, skipping");
                        Thread.Sleep(_tabRetryInterval);
                        continue;
                    }

                    return new BrowserTab
                    {
                        Title = focusedTabElement.Current.Name,
                        BrowserName = process.ProcessName,
                        Hwnd = process.MainWindowHandle,
                        AutomationElement = focusedTabElement
                    };
                }

                if (tabs == null || tabs.Count <= 0)
                {
                    api.LogDebug(ClassName, "No tab found");
                }
                else
                {
                    api.LogDebug(ClassName, $"Found tabs: {tabs.Count}");
                    //BrowserTabPlugin.DumpElements(mainWindow, null, "Tab");

                    AutomationElement? newTabElement = null;
                    string? newTabKey = null;

                    // searching from the end in search for a tab not in the cache
                    for (int i = tabs.Count - 1; i >= 0; i--)
                    {
                        var tab = tabs[i];
                        var name = tab.Current.Name;
                        var className = tab.Current.ClassName;

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        // on Chrome, while using Flow Launcher on the browser in the foreground, there may be an invisible tab that should be skipped
                        if (className.Contains("bolt-tab", StringComparison.OrdinalIgnoreCase))
                        {
                            api.LogDebug(ClassName, $"Skipping name='{name}', className='{className}'");
                            continue;
                        }

                        var key = RuntimeIdToKey(tab);
                        if (_knownTabs.Contains(key))
                            continue;

                        api.LogDebug(ClassName, $"FOUND NEW TAB: name={name}, key={key}, className={className}");

                        newTabElement = tab;
                        newTabKey = key;
                        break;
                    }

                    if (newTabElement != null && newTabKey != null)
                    {
                        _knownTabs.Add(newTabKey);

                        result = new BrowserTab
                        {
                            Title = newTabElement.Current.Name,
                            BrowserName = process.ProcessName,
                            Hwnd = process.MainWindowHandle,
                            AutomationElement = newTabElement
                        };

                        break;
                    }

                    api.LogDebug(ClassName, "No NEW tab found");
                }

                Thread.Sleep(_tabRetryInterval);
            }

            if (result == null)
            {
                api.LogDebug(ClassName, "Timeout waiting for new tab");
            }

            return result;
        }
        catch (ElementNotAvailableException ex)
        {
            api.LogException(ClassName, "Element not available", ex);
            return null;
        }
        catch (Exception ex)
        {
            api.LogException(ClassName, "Error getting current tab from window", ex);
            return null;
        }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        string? urlToBind;
        lock (sync)
        {
            urlToBind = expectedUrl;
        }
        if (urlToBind is null)
            return;

        try
        {
            api.LogDebug(ClassName, $"Searching for... {urlToBind}");

            var element = sender as AutomationElement;
            if (element is null)
                return;

            int pid = element.Current.ProcessId;
            Process? process = null;
            try { process = Process.GetProcessById(pid); }
            catch { /* could disappear */ }

            if (process is null)
                return;

            var chromium = chromiumProcessNames.Contains(process.ProcessName);
            var firefox = firefoxProcessNames.Contains(process.ProcessName);
            if (!chromium && !firefox)
                return; // not a browser

            api.LogDebug(ClassName, $"The active browser is {process.ProcessName}");

            var rootElement = AutomationElement.FromHandle(process.MainWindowHandle);
            if (rootElement == null)
                return;

            api.LogDebug(ClassName, $"The root element is {rootElement.Current.Name}");

            var currentTab = GetCurrentTabFromWindow(rootElement, process, CancellationToken.None);
            if (currentTab != null)
            {
                lock (sync)
                {
                    api.LogDebug(ClassName, $"Registering {urlToBind} as tab: {currentTab.Title}");
                    UrlToBrowserTab[urlToBind] = currentTab;
                    expectedUrl = null;
                    api.ReQuery();
                }
            }
        }
        catch (Exception ex)
        {
            api.LogException(ClassName, "Exception", ex);
        }
    }
}
