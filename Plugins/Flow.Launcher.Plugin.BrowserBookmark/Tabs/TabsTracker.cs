using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using BrowserTabs;
using static Flow.Launcher.Plugin.BrowserBookmark.Main;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public class TabsTracker : IDisposable
{
    private static readonly string ClassName = nameof(TabsTracker);
    private static readonly HashSet<string> chromiumProcessNames = new HashSet<string>(["msedge", "chrome", "brave", "vivaldi", "opera", "chromium"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> firefoxProcessNames = new HashSet<string>(["firefox"], StringComparer.OrdinalIgnoreCase);
    private readonly TabsWalker _walker = new();

    private string? _expectedUrl;
    private readonly object _sync = new();

    public Dictionary<string, BrowserTab> UrlToBrowserTab { get; } = [];

    private AutomationFocusChangedEventHandler? _focusHandler;
    private bool _initialized;

    public void OpenBookmarkAndTrack(string url)
    {
        ExpectUrl(url);
        Context.API.LogDebug(ClassName, $"Opening... {url}");
        Context.API.OpenUrl(url);
    }

    public List<Result> InjectExistingTabs(List<Result> results)
    {
        foreach (var r in results)
        {
            var bookmarkUrl = ((BookmarkAttributes)r.ContextData).Url;
            if (UrlToBrowserTab.TryGetValue(bookmarkUrl, out var existingTab))
            {
                Context.API.LogDebug(ClassName, $"Mapped {bookmarkUrl}");

                r.ContextData = existingTab;
                r.Action = c =>
                {
                    if (!existingTab.ActivateTab())
                    {
                        Context.API.LogError(ClassName, "Failed to activate a tab");
                        Remove(bookmarkUrl);
                        OpenBookmarkAndTrack(bookmarkUrl);
                    }
                    return true;
                };

                r.ShowBadge = true;
                r.BadgeIcoPath = "Images/BrowserTabsPlugin.png";
            }
        }
        return results;
    }

    public void Init()
    {
        if (_initialized)
            return;

        _focusHandler = OnFocusChanged;
        Automation.AddAutomationFocusChangedEventHandler(_focusHandler);
        _initialized = true;
    }

    public void Dispose()
    {
        if (_focusHandler != null)
            Automation.RemoveAutomationFocusChangedEventHandler(_focusHandler);
    }

    public void ExpectUrl(string url)
    {
        lock (_sync)
        {
            if (_expectedUrl != null)
            {
                Context.API.LogError(ClassName, $"Opening {url} while older is still not resolved ({_expectedUrl}). Forgetting the older.");
            }
            _expectedUrl = url;
        }
    }

    private void Remove(string url)
    {
        lock (_sync)
        {
            UrlToBrowserTab.Remove(url);
        }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        string? urlToBind;
        lock (_sync)
        {
            urlToBind = _expectedUrl;
        }
        if (urlToBind is null)
            return;

        try
        {
            Context.API.LogDebug(ClassName, $"Searching for... {urlToBind}");

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

            Context.API.LogDebug(ClassName, $"The active browser is {process.ProcessName}");

            var rootElement = AutomationElement.FromHandle(process.MainWindowHandle);
            if (rootElement == null)
                return;

            Context.API.LogDebug(ClassName, $"The root element is {rootElement.Current.Name}");

            var currentTab = _walker.GetCurrentTabFromWindow(rootElement, process, CancellationToken.None);
            if (currentTab != null)
            {
                lock (_sync)
                {
                    Context.API.LogDebug(ClassName, $"Registering {urlToBind} as tab: {currentTab.Title}");
                    UrlToBrowserTab[urlToBind] = currentTab;
                    _expectedUrl = null;
                    Context.API.ReQuery();
                }
            }
        }
        catch (Exception ex)
        {
            Context.API.LogException(ClassName, "Exception", ex);
        }
    }
}
