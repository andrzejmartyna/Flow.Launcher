using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using static Flow.Launcher.Plugin.BrowserBookmark.Main;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

internal static class BrowserTabPlugin
{
    private static readonly string ClassName = nameof(BrowserTabPlugin);

    public static void OpenBookmarkAndTrack(BrowserTabTracker tabTracker, string url)
    {
        tabTracker.ExpectUrl(url);
        Context.API.OpenUrl(url);
    }

    public static List<Result> InjectExistingTabs(BrowserTabTracker tabTracker, IPublicAPI api, List<Result> results)
    {
        api.LogDebug(ClassName, "InjectExistingTabs");

        //var allTabsElements = BrowserTabManager.GetAllTabs();

        //var matches = AutomationTabMatcher.MatchTabs(allTabsElements, allTabsUrls);
        //api.LogInfo(ClassName, "========================= Matches =========================");
        //foreach (var m in matches)
        //{
        //    api.LogInfo(ClassName, $"Map {m.Key.Current.Name}\r\n\ton to {m.Value}");
        //}

        foreach (var r in results)
        {
            var bookmarkUrl = ((BookmarkAttributes)r.ContextData).Url;
            if (tabTracker.UrlToBrowserTab.TryGetValue(bookmarkUrl, out var tab))
            {
                api.LogDebug(ClassName, $"Mapped {bookmarkUrl}");
                //r.Title = tab.Title;
                
                ////r.IcoPath = GetBrowserIcoPath(existingTab.BrowserName);
                
                //r.Score = titleMatch.Score + browserNameMatch.Score;
                //r.TitleHighlightData = titleMatch.MatchData;
                //r.SubTitle = tab.BrowserName;
                
                ////r.ContextData = existingTab;
                ///
                r.Action = c =>
                {
                    if (!tab.ActivateTab())
                    {
                        tabTracker.Remove(bookmarkUrl);
                        OpenBookmarkAndTrack(tabTracker, bookmarkUrl);
                    }
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
    public static void DumpElements(AutomationElement parent, string classNameOnly = null, string controlTypeOnly = null, int indent = 0)
    {
        var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement child in children)
        {
            var type = child.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "");
            var name = child.Current.Name;
            var dump = true;
            if (!string.IsNullOrEmpty(classNameOnly) && child.Current.ClassName != classNameOnly)
            {
                dump = false;
            }
            if (!string.IsNullOrEmpty(controlTypeOnly) && type != controlTypeOnly)
            {
                dump = false;
            }
            if (dump)
            {
                Context.API.LogDebug(ClassName, $"{new string(' ', indent)}{type}: {name}");
            }
            DumpElements(child, classNameOnly, controlTypeOnly, indent + 2); // recurse
        }
    }
}
