using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public static class FirefoxTabsFetcher
{
    private const string Endpoint = "ws://localhost:6000";

    public static async Task<List<string>> GetOpenTabsAsync()
    {
        var urls = new List<string>();
        try
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(Endpoint), CancellationToken.None);

            // Request list of tabs
            var message = "{\"to\":\"root\",\"type\":\"listTabs\"}";
            var buffer = Encoding.UTF8.GetBytes(message);
            await ws.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);

            // Read response
            var recvBuffer = new byte[16384];
            var result = await ws.ReceiveAsync(recvBuffer, CancellationToken.None);
            var json = Encoding.UTF8.GetString(recvBuffer, 0, result.Count);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tabs", out var tabs))
            {
                foreach (var tab in tabs.EnumerateArray())
                {
                    var url = tab.GetProperty("url").GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                        urls.Add(url);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FirefoxTabsFetcher] {ex.Message}");
        }

        return urls;
    }
}
