using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using BrowserTabs;
using static Flow.Launcher.Plugin.BrowserBookmark.Main;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

internal class TabsWalker
{
    private static readonly string ClassName = nameof(TabsTracker);

    private readonly TimeSpan _tabRetryTimeout = TimeSpan.FromSeconds(4);
    private readonly TimeSpan _tabRetryInterval = TimeSpan.FromMilliseconds(250);

    private readonly TabsCache _cache = new();

    private static AutomationElement TryGetFocusedTabFromWindow(AutomationElement mainWindow)
    {
        AutomationElement focused = null;
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

    public BrowserTab GetCurrentTabFromWindow(AutomationElement mainWindow, Process process, CancellationToken cancellationToken)
    {
        try
        {
            Condition tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

            var sw = Stopwatch.StartNew();
            BrowserTab result = null;
            var count = 1;

            while (sw.Elapsed < _tabRetryTimeout && !cancellationToken.IsCancellationRequested)
            {
                Context.API.LogDebug(ClassName, $"Start searching for a new tab... Try no {count++}");

                var tabs = mainWindow.FindAll(TreeScope.Descendants, tabCondition);
                if (_cache.Empty())
                {
                    foreach (AutomationElement tab in tabs)
                    {
                        _cache.Add(tab);
                    }
                    Context.API.LogDebug(ClassName, "Waiting after filling known tabs list");
                    Thread.Sleep(_tabRetryInterval);
                    continue;
                }

                var focusedTabElement = TryGetFocusedTabFromWindow(mainWindow);
                if (focusedTabElement != null && !string.IsNullOrWhiteSpace(focusedTabElement.Current.Name))
                {
                    Context.API.LogDebug(ClassName, $"Focused tab via keyboard focus: {focusedTabElement.Current.Name}");
                    if (_cache.Contains(focusedTabElement))
                    {
                        Context.API.LogDebug(ClassName, "... but the tab is an existing one, skipping");
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
                    Context.API.LogDebug(ClassName, "No tab found");
                }
                else
                {
                    Context.API.LogDebug(ClassName, $"Found tabs: {tabs.Count}");
                    //TabsDebug.DumpElements(mainWindow, null, "Tab");

                    AutomationElement newTabElement = null;

                    // searching from the end in search for a tab not in the cache
                    for (var i = tabs.Count - 1; i >= 0; i--)
                    {
                        var tab = tabs[i];
                        var name = tab.Current.Name;
                        var className = tab.Current.ClassName;

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        // on Chrome, while using Flow Launcher on the browser in the foreground, there may be an invisible tab that should be skipped
                        if (className.Contains("bolt-tab", StringComparison.OrdinalIgnoreCase))
                        {
                            Context.API.LogDebug(ClassName, $"Skipping name='{name}', className='{className}'");
                            continue;
                        }

                        if (_cache.Contains(tab))
                            continue;

                        Context.API.LogDebug(ClassName, $"FOUND NEW TAB: name={name}, className={className}");

                        newTabElement = tab;
                        break;
                    }

                    if (newTabElement != null)
                    {
                        _cache.Add(newTabElement);

                        result = new BrowserTab
                        {
                            Title = newTabElement.Current.Name,
                            BrowserName = process.ProcessName,
                            Hwnd = process.MainWindowHandle,
                            AutomationElement = newTabElement
                        };

                        break;
                    }

                    Context.API.LogDebug(ClassName, "No NEW tab found");
                }

                Thread.Sleep(_tabRetryInterval);
            }

            if (result == null)
            {
                Context.API.LogDebug(ClassName, "Timeout waiting for new tab");
            }

            return result;
        }
        catch (ElementNotAvailableException ex)
        {
            Context.API.LogException(ClassName, "Element not available", ex);
            return null;
        }
        catch (Exception ex)
        {
            Context.API.LogException(ClassName, "Error getting current tab from window", ex);
            return null;
        }
    }
}
