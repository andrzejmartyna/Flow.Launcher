using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;
using BrowserTabs;
using static Flow.Launcher.Plugin.BrowserBookmark.Main;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

internal static class BrowserTabsPlugin
{
    private static readonly string ClassName = nameof(BrowserTabsPlugin);

    public static List<Result> InjectExistingTabs(IPublicAPI api, List<Result> results)
    {
        api.LogInfo(ClassName, "InjectExistingTabs");

        var allTabsElements = BrowserTabManager.GetAllTabs();
        var allTabsUrls = SafeAsyncHelper.RunSync(() => ChromiumTabsFetcher.GetOpenTabsJsonAsync(9222));

        var matches = AutomationTabMatcher.MatchTabs(allTabsElements, allTabsUrls);
        //api.LogInfo(ClassName, "========================= Matches =========================");
        //foreach (var m in matches)
        //{
        //    api.LogInfo(ClassName, $"Map {m.Key.Current.Name}\r\n\ton to {m.Value}");
        //}

        foreach (var r in results)
        {
            var bookmarkUrl = ((BookmarkAttributes)r.ContextData).Url;
            if (matches.TryGetValue(bookmarkUrl, out BrowserTab existingTab))
            {
                api.LogInfo(ClassName, $"Mapped {bookmarkUrl}\r\n\tto {existingTab.AutomationElement.Current.Name}");
                //r.Title = tab.Title;
                r.IcoPath = GetBrowserIcoPath(existingTab.BrowserName);
                //r.Score = titleMatch.Score + browserNameMatch.Score;
                //r.TitleHighlightData = titleMatch.MatchData;
                //r.SubTitle = tab.BrowserName;
                r.ContextData = existingTab;
                r.Action = c =>
                {
                    existingTab.ActivateTab();
                    return true;
                };
                r.ShowBadge = true;
                r.BadgeIcoPath = "Images/BrowserTabsPlugin.png";
            }
        }
        return results;
    }

    private static string GetBrowserIcoPath(string browserName)
    {
        // Defined at
        // https://github.com/jjw24/BrowserTabs/blob/6d95bc467c58eb89c8e2f707d72d4cf180c48976/BrowserTabs/BrowserTabManager.cs#L18
        return browserName.ToLower() switch
        {
            "chrome" => "Images/chrome.png",
            "msedge" => "Images/msedge.png",
            "firefox" => "Images/firefox.png",
            _ => "Images/chromium.png",
        };
    }

    private static void DumpElements(AutomationElement parent, int indent = 0)
    {
        var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement child in children)
        {
            var type = child.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "");
            var name = child.Current.Name;
            Console.WriteLine($"{new string(' ', indent)}{type}: {name}");
            DumpElements(child, indent + 2); // recurse
        }
    }

    private static string? TryGetUrlFromAutomation(IPublicAPI api, AutomationElement root)
    {
        api.LogInfo(ClassName, $"Trying to get URL from {root.Current.Name}");

        //var chromiumUrls = SafeAsyncHelper.RunSync(ChromiumTabsFetcher.GetOpenTabsAsync);
        //var firefoxUrls = SafeAsyncHelper.RunSync(FirefoxTabsFetcher.GetOpenTabsAsync);

        //api.LogInfo(ClassName, "=== Chromium Tabs ===");
        //foreach (var url in chromiumUrls)
        //    api.LogInfo(ClassName, url);

        //api.LogInfo(ClassName, "=== Firefox Tabs ===");
        //foreach (var url in firefoxUrls)
        //    api.LogInfo(ClassName, url);

        if (root == null)
            return null;

        // Common UIA names for address bars in different browsers
        string[] possibleNames =
        {
            "Address and search bar",          // Chrome / Edge
            "Search or enter address",         // Firefox
            "Address bar"                      // generic fallback
        };

        DumpElements(root);

        //var element = AutomationElement.FromHandle(root.Current.NativeWindowHandle);
        //if (element != null)
        //{
        //    AutomationElementCollection elements =
        //        element.FindAll(TreeScope.Subtree, Condition.TrueCondition);

        //    foreach (AutomationElement e in elements)
        //    {
        //        string type = e.Current.ControlType?.ProgrammaticName ?? "(no type)";
        //        string name = e.Current.Name ?? "(no name)";
        //        api.LogInfo(ClassName, $"{type}: {name}");
        //    }
        //}

        //var first = root.FindFirst(TreeScope.Subtree, Condition.TrueCondition);
        //string type2 = first?.Current.ControlType?.ProgrammaticName ?? "(no type)";
        //string name2 = first?.Current.Name ?? "(no name)";
        //Console.WriteLine($"{type2}: {name2}");

        //var addressBar = root.FindFirst(TreeScope.Subtree, Condition.TrueCondition);
        //api.LogInfo(ClassName, $"Found {addressBar.Current.Name}");

        foreach (var name in possibleNames)
        {
            //var addressBar = root.FindFirst(
            //    TreeScope.Subtree,
            //    new AndCondition(
            //        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            //        new PropertyCondition(AutomationElement.NameProperty, name)));

            //if (addressBar == null)
            //    continue;

            //if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            //{
            //    return ((ValuePattern)pattern).Current.Value;
            //}
        }
        return null;
    }


    const int DefaultPort = 9222;

    public static async Task<Dictionary<string, string>> GetTabsAsync(int port = DefaultPort)
    {
        using var http = new HttpClient();
        var url = $"http://localhost:{port}/json";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var map = new Dictionary<string, string>();
        foreach (var tab in doc.RootElement.EnumerateArray())
        {
            if (tab.GetProperty("type").GetString() != "page")
                continue;

            var pageUrl = tab.GetProperty("url").GetString();
            var title = tab.GetProperty("title").GetString();
            if (!string.IsNullOrEmpty(pageUrl))
                map[pageUrl!] = title ?? "";
        }
        return map;
    }
}
