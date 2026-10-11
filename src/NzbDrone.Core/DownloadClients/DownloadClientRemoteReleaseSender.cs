// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.DownloadClients;

public static class DownloadClientRemoteReleaseSender
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static async Task<DownloadClientReleaseAddResult> AddReleaseAsync(
        DownloadClientDefinition client,
        DownloadClientReleaseAddRequest request,
        HttpClient httpClient = null,
        ISafeHttpClientService safeHttpClientService = null,
        CancellationToken cancellationToken = default)
    {
        if (client == null || request == null)
        {
            return DownloadClientReleaseAddResult.Failed("Download client or request was null.");
        }

        var effectiveCategory = !string.IsNullOrWhiteSpace(request.Category)
            ? request.Category
            : client.Category;

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
                Logger.Warn(ex, "SSRF blocked release add for {0}", baseUrl);
                return DownloadClientReleaseAddResult.Failed($"SSRF validation blocked release add for {baseUrl}.");
            }
        }
        else if (client.Host != null && client.Host.Trim().StartsWith("169.254.", StringComparison.Ordinal))
        {
            return DownloadClientReleaseAddResult.Failed($"SSRF validation blocked release add for {baseUrl}.");
        }

        var password = DownloadClientPasswordCodec.Unprotect(client.Password);
        HttpClient localHttp = null;
        if (httpClient == null)
        {
            localHttp = CreateLocalHttpClient(safeHttpClientService, TimeSpan.FromSeconds(30));
        }

        var http = httpClient ?? localHttp;
        try
        {
            if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                return await AddToQbittorrentAsync(http, baseUrl, client, password, request, effectiveCategory, cancellationToken);
            }

            if (string.Equals(client.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                return await AddToTransmissionAsync(http, baseUrl, client, password, request, effectiveCategory, cancellationToken);
            }

            if (string.Equals(client.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                return await AddToDelugeAsync(http, baseUrl, password, request, effectiveCategory, cancellationToken);
            }

            return DownloadClientReleaseAddResult.Failed($"Unsupported download client type '{client.ClientType}'.");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to send release to download client {0} ({1}:{2})", client.Name, client.Host, port);
            return DownloadClientReleaseAddResult.Failed(ex.Message);
        }
        finally
        {
            localHttp?.Dispose();
        }
    }

    private static async Task<DownloadClientReleaseAddResult> AddToQbittorrentAsync(
        HttpClient http,
        string baseUrl,
        DownloadClientDefinition client,
        string password,
        DownloadClientReleaseAddRequest request,
        string category,
        CancellationToken cancellationToken)
    {
        if (!await EnsureQbittorrentLoggedInAsync(http, baseUrl, client, password, cancellationToken))
        {
            return DownloadClientReleaseAddResult.Failed($"Authentication failed for qBittorrent at {baseUrl}.");
        }

        var infoHash = ResolveInfoHash(request);
        if (request.TorrentBytes != null && request.TorrentBytes.Length > 0)
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(request.TorrentBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(fileContent, "torrents", "release.torrent");
            AddQbittorrentFormFields(form, category, request.SavePath, request.StartPaused);

            using var fileAddResp = await http.PostAsync($"{baseUrl}/api/v2/torrents/add", form, cancellationToken);
            if (!fileAddResp.IsSuccessStatusCode)
            {
                return DownloadClientReleaseAddResult.Failed($"qBittorrent add returned HTTP {(int)fileAddResp.StatusCode}.");
            }

            return DownloadClientReleaseAddResult.Succeeded(infoHash);
        }

        var url = !string.IsNullOrWhiteSpace(request.MagnetUrl)
            ? request.MagnetUrl
            : request.TorrentUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return DownloadClientReleaseAddResult.Failed("No magnet, torrent URL, or torrent bytes were provided.");
        }

        var fields = new Dictionary<string, string> { { "urls", url } };
        AddQbittorrentFormDictionary(fields, category, request.SavePath, request.StartPaused);
        using var content = new FormUrlEncodedContent(fields);
        using var resp = await http.PostAsync($"{baseUrl}/api/v2/torrents/add", content, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return DownloadClientReleaseAddResult.Failed($"qBittorrent add returned HTTP {(int)resp.StatusCode}.");
        }

        return DownloadClientReleaseAddResult.Succeeded(infoHash ?? MagnetLinkParser.NormalizeInfoHash(MagnetLinkParser.Parse(url)?.InfoHash));
    }

    private static void AddQbittorrentFormFields(MultipartFormDataContent form, string category, string savePath, bool startPaused)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            form.Add(new StringContent(category), "category");
        }

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            form.Add(new StringContent(savePath), "savepath");
        }

        if (startPaused)
        {
            form.Add(new StringContent("true"), "paused");
        }
    }

    private static void AddQbittorrentFormDictionary(Dictionary<string, string> fields, string category, string savePath, bool startPaused)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            fields["category"] = category;
        }

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            fields["savepath"] = savePath;
        }

        if (startPaused)
        {
            fields["paused"] = "true";
        }
    }

    private static async Task<DownloadClientReleaseAddResult> AddToTransmissionAsync(
        HttpClient http,
        string baseUrl,
        DownloadClientDefinition client,
        string password,
        DownloadClientReleaseAddRequest request,
        string category,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object>();
        if (request.TorrentBytes != null && request.TorrentBytes.Length > 0)
        {
            arguments["metainfo"] = Convert.ToBase64String(request.TorrentBytes);
        }
        else if (!string.IsNullOrWhiteSpace(request.MagnetUrl))
        {
            arguments["filename"] = request.MagnetUrl;
        }
        else if (!string.IsNullOrWhiteSpace(request.TorrentUrl))
        {
            arguments["filename"] = request.TorrentUrl;
        }
        else
        {
            return DownloadClientReleaseAddResult.Failed("No magnet, torrent URL, or torrent bytes were provided.");
        }

        if (request.StartPaused)
        {
            arguments["paused"] = true;
        }

        if (!string.IsNullOrWhiteSpace(request.SavePath))
        {
            arguments["download-dir"] = request.SavePath;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            arguments["labels"] = new[] { category };
        }

        var payload = JsonSerializer.Serialize(new
        {
            method = "torrent-add",
            arguments,
        });

        using var resp = await SendTransmissionRpcAsync(http, baseUrl, payload, client, password, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return DownloadClientReleaseAddResult.Failed($"Transmission add returned HTTP {(int)resp.StatusCode}.");
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!TryGetTransmissionRpcSuccess(doc.RootElement, "torrent-add", baseUrl))
        {
            return DownloadClientReleaseAddResult.Failed($"Transmission torrent-add failed for {baseUrl}.");
        }

        var infoHash = ResolveInfoHash(request);
        if (string.IsNullOrWhiteSpace(infoHash) &&
            doc.RootElement.TryGetProperty("arguments", out var args) &&
            args.TryGetProperty("torrent-added", out var added) &&
            added.TryGetProperty("hashString", out var hashElem))
        {
            infoHash = hashElem.GetString();
        }

        return DownloadClientReleaseAddResult.Succeeded(infoHash);
    }

    private static async Task<DownloadClientReleaseAddResult> AddToDelugeAsync(
        HttpClient http,
        string baseUrl,
        string password,
        DownloadClientReleaseAddRequest request,
        string category,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(password))
        {
            if (!await EnsureDelugeLoggedInAsync(http, baseUrl, password, cancellationToken))
            {
                return DownloadClientReleaseAddResult.Failed($"Authentication failed for Deluge at {baseUrl}.");
            }
        }

        var options = BuildDelugeAddOptions(request, category);
        string method;
        object[] parameters;
        if (request.TorrentBytes != null && request.TorrentBytes.Length > 0)
        {
            method = "core.add_torrent_file";
            parameters = new object[] { Convert.ToBase64String(request.TorrentBytes), options };
        }
        else if (!string.IsNullOrWhiteSpace(request.MagnetUrl))
        {
            method = "core.add_torrent_magnet";
            parameters = new object[] { request.MagnetUrl, options };
        }
        else if (!string.IsNullOrWhiteSpace(request.TorrentUrl))
        {
            method = "core.add_torrent_url";
            parameters = new object[] { request.TorrentUrl, options };
        }
        else
        {
            return DownloadClientReleaseAddResult.Failed("No magnet, torrent URL, or torrent bytes were provided.");
        }

        var body = new StringContent(
            JsonSerializer.Serialize(new { method, @params = parameters, id = 1 }),
            Encoding.UTF8,
            "application/json");

        using var resp = await http.PostAsync($"{baseUrl}/json", body, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return DownloadClientReleaseAddResult.Failed($"Deluge add returned HTTP {(int)resp.StatusCode}.");
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out var errElem) && errElem.ValueKind != JsonValueKind.Null)
        {
            return DownloadClientReleaseAddResult.Failed($"Deluge add failed: {errElem}.");
        }

        return DownloadClientReleaseAddResult.Succeeded(ResolveInfoHash(request));
    }

    private static Dictionary<string, object> BuildDelugeAddOptions(DownloadClientReleaseAddRequest request, string category)
    {
        var options = new Dictionary<string, object>
        {
            ["add_paused"] = request.StartPaused,
        };

        if (!string.IsNullOrWhiteSpace(request.SavePath))
        {
            options["download_location"] = request.SavePath;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            options["label"] = category;
        }

        return options;
    }

    private static string ResolveInfoHash(DownloadClientReleaseAddRequest request)
    {
        if (request.TorrentBytes != null && request.TorrentBytes.Length > 0)
        {
            try
            {
                var parsed = new TorrentFileParser().Parse(request.TorrentBytes);
                return MagnetLinkParser.NormalizeInfoHash(parsed?.InfoHash);
            }
            catch
            {
                return null;
            }
        }

        var magnet = !string.IsNullOrWhiteSpace(request.MagnetUrl) ? request.MagnetUrl : request.TorrentUrl;
        if (!string.IsNullOrWhiteSpace(magnet) && magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            return MagnetLinkParser.NormalizeInfoHash(MagnetLinkParser.Parse(magnet)?.InfoHash);
        }

        return null;
    }

    private static async Task<bool> EnsureQbittorrentLoggedInAsync(
        HttpClient http,
        string baseUrl,
        DownloadClientDefinition client,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(client.Username) && string.IsNullOrWhiteSpace(password))
        {
            return true;
        }

        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "username", client.Username ?? string.Empty },
            { "password", password ?? string.Empty },
        });

        using var loginResp = await http.PostAsync($"{baseUrl}/api/v2/auth/login", loginContent, cancellationToken).ConfigureAwait(false);
        if (!loginResp.IsSuccessStatusCode)
        {
            return false;
        }

        var loginResult = await loginResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return !string.Equals(loginResult.Trim(), "Fails.", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> EnsureDelugeLoggedInAsync(HttpClient http, string baseUrl, string password, CancellationToken cancellationToken)
    {
        var loginContent = new StringContent(
            JsonSerializer.Serialize(new { method = "auth.login", @params = new object[] { password }, id = 1 }),
            Encoding.UTF8,
            "application/json");

        using var loginResp = await http.PostAsync($"{baseUrl}/json", loginContent, cancellationToken).ConfigureAwait(false);
        if (!loginResp.IsSuccessStatusCode)
        {
            return false;
        }

        var loginJson = await loginResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var loginDoc = JsonDocument.Parse(loginJson);
        return !(loginDoc.RootElement.TryGetProperty("result", out var resElem) && resElem.ValueKind == JsonValueKind.False);
    }

    private static HttpRequestMessage CreateTransmissionRpcRequest(string baseUrl, string rpcContent, DownloadClientDefinition client, string password)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
        {
            Content = new StringContent(rpcContent, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(password))
        {
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{password}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }

        return req;
    }

    private static bool TryGetTransmissionRpcSuccess(JsonElement root, string operationContext, string baseUrl)
    {
        if (root.TryGetProperty("result", out var resultElem) &&
            resultElem.ValueKind == JsonValueKind.String &&
            string.Equals(resultElem.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var resultText = root.TryGetProperty("result", out var errElem) && errElem.ValueKind == JsonValueKind.String
            ? errElem.GetString()
            : root.GetRawText();
        Logger.Warn("Transmission {0} RPC failed: {1} for {2}", operationContext, resultText, baseUrl);
        return false;
    }

    private static async Task<HttpResponseMessage> SendTransmissionRpcAsync(
        HttpClient http,
        string baseUrl,
        string rpcContent,
        DownloadClientDefinition client,
        string password,
        CancellationToken cancellationToken)
    {
        using var req = CreateTransmissionRpcRequest(baseUrl, rpcContent, client, password);
        var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Conflict &&
            resp.Headers.TryGetValues("X-Transmission-Session-Id", out var sessValues))
        {
            var sessionId = sessValues.FirstOrDefault();
            resp.Dispose();
            using var req2 = CreateTransmissionRpcRequest(baseUrl, rpcContent, client, password);
            req2.Headers.Add("X-Transmission-Session-Id", sessionId);
            return await http.SendAsync(req2, cancellationToken).ConfigureAwait(false);
        }

        return resp;
    }

    private static HttpClient CreateLocalHttpClient(ISafeHttpClientService safeHttpClientService, TimeSpan timeout)
    {
        var safeClient = safeHttpClientService?.CreateHttpClient(timeout, useCookies: true);
        if (safeClient != null)
        {
            return safeClient;
        }

        var handler = new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        return new HttpClient(handler) { Timeout = timeout };
    }
}
