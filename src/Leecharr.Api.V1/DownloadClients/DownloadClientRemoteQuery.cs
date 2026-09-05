// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.DownloadClients;

namespace Leecharr.Api.V1.DownloadClients;

public static class DownloadClientRemoteQuery
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static async Task<List<DownloadClientRemoteItem>> QueryRemoteClientItemsAsync(DownloadClientDefinition client, HttpClient httpClient = null)
    {
        var items = new List<DownloadClientRemoteItem>();
        if (client == null)
        {
            return items;
        }

        var port = client.Port > 0 ? client.Port : 8080;
        var scheme = client.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{client.Host}:{port}";

        HttpClient localHttp = null;
        if (httpClient == null)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };
            localHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        }

        var http = httpClient ?? localHttp;

        try
        {
            if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(client.Password))
                {
                    var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "username", client.Username ?? string.Empty },
                        { "password", client.Password ?? string.Empty },
                    });

                    var loginResp = await http.PostAsync($"{baseUrl}/api/v2/auth/login", loginContent);
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        Logger.Warn("qBittorrent login failed with status {0} for {1}", loginResp.StatusCode, baseUrl);
                        return items;
                    }

                    var loginResult = await loginResp.Content.ReadAsStringAsync();
                    if (string.Equals(loginResult.Trim(), "Fails.", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warn("qBittorrent authentication failed (Fails.) for {0}", baseUrl);
                        return items;
                    }
                }

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
                else
                {
                    Logger.Warn("qBittorrent query returned status code {0} for {1}", resp.StatusCode, baseUrl);
                }
            }
            else if (string.Equals(client.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
                {
                    Content = new StringContent(
                        "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\"]}}",
                        Encoding.UTF8,
                        "application/json"),
                };

                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(client.Password))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{client.Password}"));
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                }

                var resp = await http.SendAsync(req);
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

                    if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(client.Password))
                    {
                        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{client.Password}"));
                        req2.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                    }

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
                else
                {
                    Logger.Warn("Transmission query returned status code {0} for {1}", resp.StatusCode, baseUrl);
                }
            }
            else if (string.Equals(client.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(client.Password))
                {
                    var loginContent = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            method = "auth.login",
                            @params = new object[] { client.Password },
                            id = 1,
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var loginResp = await http.PostAsync($"{baseUrl}/json", loginContent);
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        Logger.Warn("Deluge login failed with status code {0} for {1}", loginResp.StatusCode, baseUrl);
                        return items;
                    }

                    var loginJson = await loginResp.Content.ReadAsStringAsync();
                    using var loginDoc = JsonDocument.Parse(loginJson);
                    if (loginDoc.RootElement.TryGetProperty("result", out var resElem) &&
                        resElem.ValueKind == JsonValueKind.False)
                    {
                        Logger.Warn("Deluge authentication failed for {0}", baseUrl);
                        return items;
                    }
                }

                var body = new StringContent(
                    "{\"method\":\"core.get_torrents_status\",\"params\":[{},[\"name\",\"total_size\",\"progress\",\"state\",\"save_path\",\"label\"]],\"id\":1}",
                    Encoding.UTF8,
                    "application/json");

                var resp = await http.PostAsync($"{baseUrl}/json", body);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("error", out var errElem) && errElem.ValueKind != JsonValueKind.Null)
                    {
                        Logger.Warn("Deluge returned error: {0} for {1}", errElem.ToString(), baseUrl);
                        return items;
                    }

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
                else
                {
                    Logger.Warn("Deluge query returned status code {0} for {1}", resp.StatusCode, baseUrl);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to query remote download client {0} ({1}:{2})", client.Name, client.Host, port);
        }
        finally
        {
            localHttp?.Dispose();
        }

        return items;
    }
}
