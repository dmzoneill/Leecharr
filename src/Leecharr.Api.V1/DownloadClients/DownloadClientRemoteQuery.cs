// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NzbDrone.Core.DownloadClients;

namespace Leecharr.Api.V1.DownloadClients;

public static class DownloadClientRemoteQuery
{
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<List<DownloadClientRemoteItem>> QueryRemoteClientItemsAsync(DownloadClientDefinition client, HttpClient httpClient = null)
    {
        var items = new List<DownloadClientRemoteItem>();
        var port = client.Port > 0 ? client.Port : 8080;
        var scheme = client.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{client.Host}:{port}";
        var http = httpClient ?? DefaultHttpClient;

        try
        {
            if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await http.GetAsync($"{baseUrl}/api/v2/torrents/info");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var idx = 1;
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var hash = el.TryGetProperty("hash", out var h) ? h.GetString() : string.Empty;
                            var name = el.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                            var size = el.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                            var prog = el.TryGetProperty("progress", out var p) && p.TryGetDouble(out var pr) ? pr : 0.0;
                            var state = el.TryGetProperty("state", out var st) ? st.GetString() : "unknown";
                            var save = el.TryGetProperty("save_path", out var sp) ? sp.GetString() : string.Empty;
                            var cat = el.TryGetProperty("category", out var c) ? c.GetString() : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = state,
                                SavePath = save,
                                Category = cat,
                            });
                        }
                    }
                }
            }
            else if (string.Equals(client.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                var body = new StringContent(
                    "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\"]}}",
                    Encoding.UTF8,
                    "application/json");

                var resp = await http.PostAsync($"{baseUrl}/transmission/rpc", body);
                if (resp.StatusCode == HttpStatusCode.Conflict && resp.Headers.TryGetValues("X-Transmission-Session-Id", out var sessValues))
                {
                    var sessionId = sessValues.FirstOrDefault();
                    using var req2 = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
                    {
                        Content = new StringContent(
                            "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\"]}}",
                            Encoding.UTF8,
                            "application/json"),
                    };
                    req2.Headers.Add("X-Transmission-Session-Id", sessionId);
                    resp = await http.SendAsync(req2);
                }

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("arguments", out var args) && args.TryGetProperty("torrents", out var torrents) && torrents.ValueKind == JsonValueKind.Array)
                    {
                        var idx = 1;
                        foreach (var el in torrents.EnumerateArray())
                        {
                            var hash = el.TryGetProperty("hashString", out var h) ? h.GetString() : string.Empty;
                            var name = el.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                            var size = el.TryGetProperty("totalSize", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                            var prog = el.TryGetProperty("percentDone", out var p) && p.TryGetDouble(out var pr) ? pr : 0.0;
                            var save = el.TryGetProperty("downloadDir", out var sp) ? sp.GetString() : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = "active",
                                SavePath = save,
                                Category = string.Empty,
                            });
                        }
                    }
                }
            }
            else if (string.Equals(client.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                var body = new StringContent(
                    "{\"method\":\"core.get_torrents_status\",\"params\":[{},[\"name\",\"total_size\",\"progress\",\"state\",\"save_path\",\"label\"]],\"id\":1}",
                    Encoding.UTF8,
                    "application/json");

                var resp = await http.PostAsync($"{baseUrl}/json", body);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object)
                    {
                        var idx = 1;
                        foreach (var prop in res.EnumerateObject())
                        {
                            var hash = prop.Name;
                            var el = prop.Value;
                            var name = el.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                            var size = el.TryGetProperty("total_size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                            var prog = el.TryGetProperty("progress", out var p) && p.TryGetDouble(out var pr) ? pr / 100.0 : 0.0;
                            var state = el.TryGetProperty("state", out var st) ? st.GetString() : "unknown";
                            var save = el.TryGetProperty("save_path", out var sp) ? sp.GetString() : string.Empty;
                            var cat = el.TryGetProperty("label", out var c) ? c.GetString() : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = state,
                                SavePath = save,
                                Category = cat,
                            });
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return items;
    }
}
