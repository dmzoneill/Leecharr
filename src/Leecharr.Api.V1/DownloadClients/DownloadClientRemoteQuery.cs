// Copyright (c) FeedItOut. All rights reserved.

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
using NzbDrone.Core.Http;

namespace Leecharr.Api.V1.DownloadClients;

public static class DownloadClientRemoteQuery
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static async Task<List<DownloadClientRemoteItem>> QueryRemoteClientItemsAsync(
        DownloadClientDefinition client,
        HttpClient httpClient = null,
        ISafeHttpClientService safeHttpClientService = null,
        bool filterByCategory = true)
    {
        var items = new List<DownloadClientRemoteItem>();
        if (client == null)
        {
            return items;
        }

        var port = client.Port > 0 ? client.Port : 8080;
        var scheme = client.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{client.Host}:{port}";

        if (safeHttpClientService != null)
        {
            try
            {
                safeHttpClientService.ValidateUrl(baseUrl);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SSRF blocked query for {0}", baseUrl);
                return items;
            }
        }
        else if (client.Host != null && client.Host.Trim().StartsWith("169.254.", StringComparison.Ordinal))
        {
            Logger.Warn("SSRF blocked query for {0}", baseUrl);
            return items;
        }

        var password = DownloadClientPasswordHelper.Unprotect(client.Password);

        HttpClient localHttp = null;
        if (httpClient == null)
        {
            var handler = new SocketsHttpHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            localHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        }

        var http = httpClient ?? localHttp;

        try
        {
            if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
                {
                    var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "username", client.Username ?? string.Empty },
                        { "password", password ?? string.Empty },
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
                            var tags = el.TryGetProperty("tags", out var tg) ? tg.GetString() : string.Empty;
                            if (string.IsNullOrWhiteSpace(cat) && !string.IsNullOrWhiteSpace(tags))
                            {
                                cat = tags;
                            }

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
                                Tags = tags,
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
                        "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\",\"labels\"]}}",
                        Encoding.UTF8,
                        "application/json"),
                };

                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{password}"));
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                }

                var resp = await http.SendAsync(req);
                if (resp.StatusCode == HttpStatusCode.Conflict && resp.Headers.TryGetValues("X-Transmission-Session-Id", out var sessValues))
                {
                    var sessionId = sessValues.FirstOrDefault();
                    using var req2 = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
                    {
                        Content = new StringContent(
                            "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\",\"labels\"]}}",
                            Encoding.UTF8,
                            "application/json"),
                    };

                    if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
                    {
                        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{password}"));
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
                            var labelList = new List<string>();
                            if (el.TryGetProperty("labels", out var lbls) && lbls.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var l in lbls.EnumerateArray())
                                {
                                    var str = l.GetString();
                                    if (!string.IsNullOrWhiteSpace(str))
                                    {
                                        labelList.Add(str.Trim());
                                    }
                                }
                            }

                            var labelsStr = string.Join(", ", labelList);
                            var cat = labelList.Count > 0 ? labelList[0] : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = "active",
                                SavePath = save,
                                Category = cat,
                                Tags = labelsStr,
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
                if (!string.IsNullOrWhiteSpace(password))
                {
                    var loginContent = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            method = "auth.login",
                            @params = new object[] { password },
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
                                Tags = cat,
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

        if (filterByCategory && !string.IsNullOrWhiteSpace(client.Category))
        {
            items = items.Where(i => MatchesCategory(i, client.Category)).ToList();
        }

        return items;
    }

    public static bool MatchesCategory(DownloadClientRemoteItem item, string targetCategory)
    {
        if (string.IsNullOrWhiteSpace(targetCategory))
        {
            return true;
        }

        if (item == null)
        {
            return false;
        }

        var targets = targetCategory.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .Where(t => !string.IsNullOrEmpty(t))
                                    .ToList();
        if (targets.Count == 0)
        {
            return true;
        }

        var candidateTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            candidateTokens.Add(item.Category.Trim());
            foreach (var token in item.Category.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                candidateTokens.Add(token.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Tags))
        {
            candidateTokens.Add(item.Tags.Trim());
            foreach (var token in item.Tags.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                candidateTokens.Add(token.Trim());
            }
        }

        return targets.Any(target => candidateTokens.Contains(target));
    }

    public static Task<bool> PauseTorrentAsync(DownloadClientDefinition client, string infoHash, HttpClient httpClient = null, ISafeHttpClientService safeHttpClientService = null)
    {
        return ExecuteTorrentActionAsync(client, infoHash, "pause", false, httpClient, safeHttpClientService);
    }

    public static Task<bool> ResumeTorrentAsync(DownloadClientDefinition client, string infoHash, HttpClient httpClient = null, ISafeHttpClientService safeHttpClientService = null)
    {
        return ExecuteTorrentActionAsync(client, infoHash, "resume", false, httpClient, safeHttpClientService);
    }

    public static Task<bool> DeleteTorrentAsync(DownloadClientDefinition client, string infoHash, bool deleteData = false, HttpClient httpClient = null, ISafeHttpClientService safeHttpClientService = null)
    {
        return ExecuteTorrentActionAsync(client, infoHash, "delete", deleteData, httpClient, safeHttpClientService);
    }

    private static async Task<bool> ExecuteTorrentActionAsync(
        DownloadClientDefinition client,
        string infoHash,
        string action,
        bool deleteData = false,
        HttpClient httpClient = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        if (client == null || string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        var port = client.Port > 0 ? client.Port : 8080;
        var scheme = client.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{client.Host}:{port}";

        if (safeHttpClientService != null)
        {
            try
            {
                safeHttpClientService.ValidateUrl(baseUrl);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SSRF blocked action for {0}", baseUrl);
                return false;
            }
        }
        else if (client.Host != null && client.Host.Trim().StartsWith("169.254.", StringComparison.Ordinal))
        {
            Logger.Warn("SSRF blocked action for {0}", baseUrl);
            return false;
        }

        var password = DownloadClientPasswordHelper.Unprotect(client.Password);

        HttpClient localHttp = null;
        if (httpClient == null)
        {
            var handler = new SocketsHttpHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            localHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        }

        var http = httpClient ?? localHttp;

        try
        {
            if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
                {
                    var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "username", client.Username ?? string.Empty },
                        { "password", password ?? string.Empty },
                    });

                    var loginResp = await http.PostAsync($"{baseUrl}/api/v2/auth/login", loginContent);
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        Logger.Warn("qBittorrent login failed with status {0} for {1}", loginResp.StatusCode, baseUrl);
                        return false;
                    }

                    var loginResult = await loginResp.Content.ReadAsStringAsync();
                    if (string.Equals(loginResult.Trim(), "Fails.", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warn("qBittorrent authentication failed (Fails.) for {0}", baseUrl);
                        return false;
                    }
                }

                if (action == "pause")
                {
                    var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "hashes", infoHash } });
                    var resp = await http.PostAsync($"{baseUrl}/api/v2/torrents/pause", content);
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        resp = await http.PostAsync($"{baseUrl}/api/v2/torrents/stop", content);
                    }

                    return resp.IsSuccessStatusCode;
                }
                else if (action == "resume")
                {
                    var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "hashes", infoHash } });
                    var resp = await http.PostAsync($"{baseUrl}/api/v2/torrents/resume", content);
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        resp = await http.PostAsync($"{baseUrl}/api/v2/torrents/start", content);
                    }

                    return resp.IsSuccessStatusCode;
                }
                else if (action == "delete")
                {
                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "hashes", infoHash },
                        { "deleteFiles", deleteData ? "true" : "false" },
                    });
                    var resp = await http.PostAsync($"{baseUrl}/api/v2/torrents/delete", content);
                    return resp.IsSuccessStatusCode;
                }

                return false;
            }
            else if (string.Equals(client.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                string rpcMethod;
                var argumentsDict = new Dictionary<string, object>
                {
                    ["ids"] = new[] { infoHash },
                };

                if (action == "pause")
                {
                    rpcMethod = "torrent-stop";
                }
                else if (action == "resume")
                {
                    rpcMethod = "torrent-start";
                }
                else if (action == "delete")
                {
                    rpcMethod = "torrent-remove";
                    argumentsDict["delete-local-data"] = deleteData;
                }
                else
                {
                    return false;
                }

                var rpcPayload = new Dictionary<string, object>
                {
                    ["method"] = rpcMethod,
                    ["arguments"] = argumentsDict,
                };

                var rpcContent = JsonSerializer.Serialize(rpcPayload);

                var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
                {
                    Content = new StringContent(rpcContent, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{password}"));
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                }

                var resp = await http.SendAsync(req);
                if (resp.StatusCode == HttpStatusCode.Conflict && resp.Headers.TryGetValues("X-Transmission-Session-Id", out var sessValues))
                {
                    var sessionId = sessValues.FirstOrDefault();
                    using var req2 = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
                    {
                        Content = new StringContent(rpcContent, Encoding.UTF8, "application/json"),
                    };

                    if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
                    {
                        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{password}"));
                        req2.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                    }

                    req2.Headers.Add("X-Transmission-Session-Id", sessionId);
                    resp = await http.SendAsync(req2);
                }

                return resp.IsSuccessStatusCode;
            }
            else if (string.Equals(client.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(password))
                {
                    var loginContent = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            method = "auth.login",
                            @params = new object[] { password },
                            id = 1,
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var loginResp = await http.PostAsync($"{baseUrl}/json", loginContent);
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        Logger.Warn("Deluge login failed with status code {0} for {1}", loginResp.StatusCode, baseUrl);
                        return false;
                    }

                    var loginJson = await loginResp.Content.ReadAsStringAsync();
                    using var loginDoc = JsonDocument.Parse(loginJson);
                    if (loginDoc.RootElement.TryGetProperty("result", out var resElem) &&
                        resElem.ValueKind == JsonValueKind.False)
                    {
                        Logger.Warn("Deluge authentication failed for {0}", baseUrl);
                        return false;
                    }
                }

                string delugeMethod;
                object[] delugeParams;
                if (action == "pause")
                {
                    delugeMethod = "core.pause_torrent";
                    delugeParams = new object[] { infoHash };
                }
                else if (action == "resume")
                {
                    delugeMethod = "core.resume_torrent";
                    delugeParams = new object[] { infoHash };
                }
                else if (action == "delete")
                {
                    delugeMethod = "core.remove_torrent";
                    delugeParams = new object[] { infoHash, deleteData };
                }
                else
                {
                    return false;
                }

                var body = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        method = delugeMethod,
                        @params = delugeParams,
                        id = 1,
                    }),
                    Encoding.UTF8,
                    "application/json");

                var resp = await http.PostAsync($"{baseUrl}/json", body);
                return resp.IsSuccessStatusCode;
            }
            else
            {
                Logger.Warn("Unsupported client type {0} for torrent action {1}", client.ClientType, action);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to execute torrent action {0} for client {1} ({2}:{3})", action, client.Name, client.Host, port);
            return false;
        }
        finally
        {
            localHttp?.Dispose();
        }
    }
}
