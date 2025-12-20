using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using BrowserTabs;
using SkiaSharp;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public class BrowserTabTracker : IDisposable
{
    private static readonly string ClassName = nameof(BrowserTabTracker);
    private static readonly HashSet<string> chromiumProcessNames = new HashSet<string>(new string[6] { "msedge", "chrome", "brave", "vivaldi", "opera", "chromium" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> firefoxProcessNames = new HashSet<string>(new string[1] { "firefox" }, StringComparer.OrdinalIgnoreCase);

    private string? expectedUrl;
    private readonly object sync = new();

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

    private BrowserTab? GetCurrentTabFromWindow(AutomationElement mainWindow, Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var browserName = process.ProcessName.ToLowerInvariant();
            var firefox = browserName == "firefox";

            Condition tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

            //Condition tabCondition = firefox ?
            //    //new AndCondition(
            //        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)//,
            //        //new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)
            //    //)
            //:
            //    new AndCondition(
            //        new OrCondition(
            //            new PropertyCondition(AutomationElement.ClassNameProperty, "EdgeTab"),
            //            new PropertyCondition(AutomationElement.ClassNameProperty, "Tab")
            //        ),
            //        new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)
            //    );

            api.LogDebug(ClassName, $"mainWindow: Name='{mainWindow.Current.Name}', " +
                        $"ClassName='{mainWindow.Current.ClassName}', " +
                        $"ControlType='{mainWindow.Current.ControlType.ProgrammaticName}', " +
                        $"IsEnabled={mainWindow.Current.IsEnabled}, " +
                        $"IsOffscreen={mainWindow.Current.IsOffscreen}");

            //foreach (AutomationElement e in mainWindow.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            //{
            //    if (e.Current.ControlType == ControlType.TabItem ||
            //        e.Current.ControlType.ProgrammaticName?.Contains("TabItem") == true)
            //    {
            //        api.LogDebug(ClassName,
            //            $"[DUMP] CT='{e.Current.ControlType.ProgrammaticName}', " +
            //            $"Class='{e.Current.ClassName}', Name='{e.Current.Name}'");
            //    }
            //}

            //BrowserTabPlugin.DumpElements(mainWindow, null, "TabItem");
            //BrowserTabPlugin.DumpElements(mainWindow, "Tab");
            //BrowserTabPlugin.DumpElements(mainWindow, "EdgeTab");

            //var tabs = mainWindow.FindAll(
            //    TreeScope.Descendants,
            //    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

            //api.LogDebug(ClassName, $"TabItems count: {tabs.Count}");

            //foreach (AutomationElement tab in tabs)
            //{
            //    api.LogDebug(ClassName,
            //        $"Tab: Name='{tab.Current.Name}', Class='{tab.Current.ClassName}', " +
            //        $"IsKeyboardFocusable={tab.Current.IsKeyboardFocusable}, " +
            //        $"HasKeyboardFocus={tab.Current.HasKeyboardFocus}");
            //}

            api.LogDebug(ClassName, "Start searching...");
            var tabs = mainWindow.FindAll(TreeScope.Descendants, tabCondition);
            if (tabs == null || tabs.Count <= 0)
            {
                api.LogDebug(ClassName, "No tab found");
                return null;
            }

            api.LogDebug(ClassName, $"Found tabs: {tabs.Count}");
            var focusedTab = tabs[tabs.Count - 1];
            if (focusedTab == null)
            {
                return null;
            }

            api.LogDebug(ClassName, $"Found focused tab: {focusedTab.Current.Name}");

            var tabName = focusedTab.Current.Name;
            if (string.IsNullOrWhiteSpace(tabName))
                return null;

            return new BrowserTab
            {
                Title = tabName,
                BrowserName = process.ProcessName,
                Hwnd = process.MainWindowHandle,
                AutomationElement = focusedTab
            };
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
            return; // nic nie oczekujemy

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
                    // register
                    api.LogDebug(ClassName, $"Registering {urlToBind} as tab: {currentTab.Title}");
                    UrlToBrowserTab[urlToBind] = currentTab;
                    expectedUrl = null; // handled
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
