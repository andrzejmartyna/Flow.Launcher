using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public static class ChromiumTabsFetcher
{
    public static async Task<string> GetOpenTabsJsonAsync(int port)
    {
        using var http = new HttpClient();
        return await http.GetStringAsync($"http://localhost:{port}/json");
    }

    public static async Task<List<string>> GetOpenTabsAsync()
    {
        bool any = false;
        var urls = new List<string>();
        foreach (int port in FindRemoteDebuggingPorts())
        {
            any = true;

            var json = await GetOpenTabsJsonAsync(port);
            using var doc = JsonDocument.Parse(json);

            foreach (var tab in doc.RootElement.EnumerateArray())
            {
                if (tab.TryGetProperty("url", out var urlProp))
                {
                    var url = urlProp.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                        urls.Add(url);
                }
            }
        }

        if (!any)
        {
            Console.WriteLine("No Chromium instance with --remote-debugging-port found.");
        }

        return urls;
    }

    private static IEnumerable<int> FindRemoteDebuggingPorts()
    {
        // Works if Chrome/Edge was launched with --remote-debugging-port
        foreach (var procName in new[] { "chrome", "msedge", "brave" })
        {
            foreach (var p in Process.GetProcessesByName(procName))
            {
                bool found = false;
                int port = 0;
                try
                {
                    string cmdLine = GetCommandLine(p);
                    var m = Regex.Match(cmdLine, "--remote-debugging-port=(\\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out port))
                    {
                        found = true;
                    }
                }
                catch { /* ignore */ }
                if (found)
                {
                    yield return port;
                }
            }
        }
    }

    // uses WMI to read full command line (requires admin)
    private static string GetCommandLine(Process process)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId={process.Id}");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString() ?? "";
            }
        }
        catch { }
        return "";
    }
}
