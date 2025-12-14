using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text.Json;
using System.Windows.Automation;
using BrowserTabs;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public static class AutomationTabMatcher
{
    /// <summary>
    /// Matches AutomationElement TabItems with URLs from a CDP /json list.
    /// </summary>
    /// <param name="tabElements">UIA tab items (AutomationElement)</param>
    /// <param name="cdpJson">JSON string from http://localhost:PORT/json</param>
    /// <returns>Dictionary mapping AutomationElement → URL (may contain nulls)</returns>
    public static Dictionary<string, BrowserTab> MatchTabs(
        IEnumerable<BrowserTab> tabElements,
        string cdpJson)
    {
        var result = new Dictionary<string, BrowserTab>();
        if (tabElements == null || string.IsNullOrWhiteSpace(cdpJson))
            return result;

        // parse CDP JSON
        using var doc = JsonDocument.Parse(cdpJson);
        var tabs = doc.RootElement
            .EnumerateArray()
            .Select(e => new
            {
                title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                url = e.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""
            })
            .ToList();

        // match by fuzzy title
        foreach (var el in tabElements)
        {
            string name = el?.AutomationElement?.Current.Name ?? "";

            var match = tabs.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.title) &&
                (string.Equals(name, t.title, StringComparison.OrdinalIgnoreCase) ||
                 name.Contains(t.title, StringComparison.OrdinalIgnoreCase) ||
                 t.title.Contains(name, StringComparison.OrdinalIgnoreCase)));

            if (match?.url != null && el != null)
                result[match?.url] = el;
        }

        return result;
    }
}
