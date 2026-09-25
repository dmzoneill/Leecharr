// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public class ArrWebhookRegistration : IArrWebhookRegistration
{
    private record ExistingWebhookInfo(int Id, string Url, string ApiKey);

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly HttpClient client;
    private readonly IConfigFileProvider configFileProvider;
    private readonly Logger logger;

    public ArrWebhookRegistration(IConfigFileProvider configFileProvider, HttpClient client = null)
    {
        this.configFileProvider = configFileProvider;
        this.client = client ?? SharedClient;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public bool Register(ArrConnectionDefinition connection)
    {
        var whSuccess = this.RegisterWebhook(connection);
        var dcSuccess = this.RegisterDownloadClient(connection);
        return whSuccess && dcSuccess;
    }

    public bool Unregister(ArrConnectionDefinition connection)
    {
        var whSuccess = this.UnregisterWebhook(connection);
        var dcSuccess = this.UnregisterDownloadClient(connection);
        return whSuccess && dcSuccess;
    }

    public bool RegisterWebhook(ArrConnectionDefinition connection)
    {
        if (connection == null || !connection.Enable || !connection.WebhookEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(connection.Url) || string.Equals(connection.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
            var leecharrUrl = this.GetLeecharrBaseUrl(connection);
            var webhookUrl = $"{leecharrUrl}/api/v1/webhook/arr";
            var currentApiKey = this.configFileProvider?.ApiKey ?? string.Empty;

            var existing = this.FindExistingWebhook(connection, apiVersion);
            var existingKey = existing?.ApiKey ?? string.Empty;
            if (existing != null &&
                string.Equals(existing.Url?.TrimEnd('/'), webhookUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(currentApiKey) || string.Equals(existingKey, currentApiKey, StringComparison.Ordinal)))
            {
                this.logger.Debug("Leecharr webhook already registered in {0} (notification id {1})", connection.ArrType, existing.Id);
                return true;
            }

            var fields = new List<object>
            {
                new { name = "url", value = (object)webhookUrl },
                new { name = "method", value = (object)1 },
                new
                {
                    name = "headers",
                    value = (object)new[]
                    {
                        new { key = "X-Api-Key", value = currentApiKey },
                    },
                },
            };

            var isUpdate = existing != null && existing.Id > 0;
            var notificationBody = new
            {
                id = isUpdate ? existing.Id : 0,
                name = "Leecharr",
                implementation = "Webhook",
                configContract = "WebhookSettings",
                onGrab = true,
                onDownload = true,
                onUpgrade = false,
                onRename = true,
                onHealthIssue = false,
                includeHealthWarnings = false,
                fields,
            };

            var json = JsonSerializer.Serialize(notificationBody);
            var url = isUpdate
                ? $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/notification/{existing.Id}"
                : $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/notification";
            var method = isUpdate ? HttpMethod.Put : HttpMethod.Post;

            using var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                request.Headers.Add("X-Api-Key", connection.ApiKey);
            }

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = this.client.Send(request);
            if (response.IsSuccessStatusCode)
            {
                this.logger.Info(
                    "{0} Leecharr webhook in {1} at {2} (target: {3})",
                    isUpdate ? "Updated" : "Registered",
                    connection.ArrType,
                    connection.Url,
                    webhookUrl);
                return true;
            }

            this.logger.Warn(
                "Failed to {0} webhook in {1}: {2}",
                isUpdate ? "update" : "register",
                connection.ArrType,
                response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to register webhook in {0}", connection.ArrType);
            return false;
        }
    }

    public bool UnregisterWebhook(ArrConnectionDefinition connection)
    {
        if (connection == null || string.IsNullOrWhiteSpace(connection.Url) || string.Equals(connection.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
            var existing = this.FindExistingWebhook(connection, apiVersion);
            if (existing == null)
            {
                return true;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/notification/{existing.Id}");
            if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                request.Headers.Add("X-Api-Key", connection.ApiKey);
            }

            using var response = this.client.Send(request);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                this.logger.Info("Unregistered Leecharr webhook from {0}", connection.ArrType);
                return true;
            }

            this.logger.Warn("Failed to unregister webhook from {0}: {1}", connection.ArrType, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to unregister webhook from {0}", connection.ArrType);
            return false;
        }
    }

    public bool RegisterDownloadClient(ArrConnectionDefinition connection)
    {
        if (connection == null || !connection.Enable || !connection.EnableAutomaticAdd)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(connection.Url) || string.Equals(connection.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";

            var existingId = this.FindExistingDownloadClientId(connection, apiVersion);
            if (existingId.HasValue)
            {
                this.logger.Debug("Leecharr download client already registered in {0} (client id {1})", connection.ArrType, existingId.Value);
                return true;
            }

            var host = this.ResolveDownloadClientHost(connection);
            var port = this.configFileProvider?.Port > 0 ? this.configFileProvider.Port : 7889;

            var (catField, catVal) = GetCategoryFieldAndValue(connection);

            var fields = new List<object>
            {
                new { name = "host", value = (object)host },
                new { name = "port", value = (object)port },
                new { name = "useSsl", value = (object)false },
                new { name = "urlBase", value = (object)string.Empty },
                new { name = "password", value = (object)string.Empty },
            };

            if (!string.IsNullOrWhiteSpace(catField))
            {
                fields.Add(new { name = catField, value = (object)catVal });
            }

            var body = new
            {
                name = "Leecharr",
                implementation = "Deluge",
                configContract = "DelugeSettings",
                enable = true,
                priority = 1,
                fields,
            };

            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/downloadclient");
            if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                request.Headers.Add("X-Api-Key", connection.ApiKey);
            }

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = this.client.Send(request);
            if (response.IsSuccessStatusCode)
            {
                this.logger.Info("Registered Leecharr download client in {0} at {1}", connection.ArrType, connection.Url);
                return true;
            }

            this.logger.Warn("Failed to register download client in {0}: {1}", connection.ArrType, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to register download client in {0}", connection.ArrType);
            return false;
        }
    }

    public bool UnregisterDownloadClient(ArrConnectionDefinition connection)
    {
        if (connection == null || string.IsNullOrWhiteSpace(connection.Url) || string.Equals(connection.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
            var existingId = this.FindExistingDownloadClientId(connection, apiVersion);
            if (!existingId.HasValue)
            {
                return true;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/downloadclient/{existingId.Value}");
            if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                request.Headers.Add("X-Api-Key", connection.ApiKey);
            }

            using var response = this.client.Send(request);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                this.logger.Info("Unregistered Leecharr download client from {0}", connection.ArrType);
                return true;
            }

            this.logger.Warn("Failed to unregister download client from {0}: {1}", connection.ArrType, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to unregister download client from {0}", connection.ArrType);
            return false;
        }
    }

    private ExistingWebhookInfo FindExistingWebhook(ArrConnectionDefinition connection, string apiVersion)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/notification");
            if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                request.Headers.Add("X-Api-Key", connection.ApiKey);
            }

            using var response = this.client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);

            foreach (var notification in doc.RootElement.EnumerateArray())
            {
                var name = notification.TryGetProperty("name", out var n) ? n.GetString() : null;

                string url = null;
                string apiKey = null;

                if (notification.TryGetProperty("fields", out var fields))
                {
                    foreach (var field in fields.EnumerateArray())
                    {
                        var fieldName = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                        if (fieldName == "url")
                        {
                            url = field.TryGetProperty("value", out var fv) ? fv.GetString() : null;
                        }
                        else if (fieldName == "headers" && field.TryGetProperty("value", out var headers))
                        {
                            foreach (var h in headers.EnumerateArray())
                            {
                                var hKey = h.TryGetProperty("key", out var hk) ? hk.GetString() : null;
                                if (string.Equals(hKey, "X-Api-Key", StringComparison.OrdinalIgnoreCase))
                                {
                                    apiKey = h.TryGetProperty("value", out var hv) ? hv.GetString() : null;
                                }
                            }
                        }
                    }
                }

                var isLeecharr = string.Equals(name, "Leecharr", StringComparison.OrdinalIgnoreCase) ||
                                (url != null && url.Contains("leecharr", StringComparison.OrdinalIgnoreCase) &&
                                (url.Contains("/api/v1/webhook/arr", StringComparison.OrdinalIgnoreCase) ||
                                url.Contains("/api/v1/webhooks/arr", StringComparison.OrdinalIgnoreCase)));

                if (isLeecharr && notification.TryGetProperty("id", out var idProp))
                {
                    return new ExistingWebhookInfo(idProp.GetInt32(), url, apiKey);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to check existing webhooks in {0}", connection.ArrType);
            return null;
        }
    }

    private int? FindExistingDownloadClientId(ArrConnectionDefinition connection, string apiVersion)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{connection.Url.TrimEnd('/')}/api/{apiVersion}/downloadclient");
            if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                request.Headers.Add("X-Api-Key", connection.ApiKey);
            }

            using var response = this.client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);

            foreach (var dc in doc.RootElement.EnumerateArray())
            {
                var name = dc.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.Equals(name, "Leecharr", StringComparison.OrdinalIgnoreCase))
                {
                    if (dc.TryGetProperty("id", out var idProp))
                    {
                        return idProp.GetInt32();
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to check existing download clients in {0}", connection.ArrType);
            return null;
        }
    }

    private static (string Field, string Value) GetCategoryFieldAndValue(ArrConnectionDefinition connection)
    {
        var arrType = connection.ArrType ?? string.Empty;
        if (string.Equals(arrType, "Sonarr", StringComparison.OrdinalIgnoreCase))
        {
            return ("tvCategory", !string.IsNullOrWhiteSpace(connection.Category) ? connection.Category : "tv-sonarr");
        }

        if (string.Equals(arrType, "Radarr", StringComparison.OrdinalIgnoreCase))
        {
            return ("movieCategory", !string.IsNullOrWhiteSpace(connection.Category) ? connection.Category : "radarr");
        }

        if (string.Equals(arrType, "Lidarr", StringComparison.OrdinalIgnoreCase))
        {
            return ("musicCategory", !string.IsNullOrWhiteSpace(connection.Category) ? connection.Category : "music");
        }

        if (string.Equals(arrType, "Readarr", StringComparison.OrdinalIgnoreCase))
        {
            return ("bookCategory", !string.IsNullOrWhiteSpace(connection.Category) ? connection.Category : "books");
        }

        return (null, null);
    }

    private string ResolveDownloadClientHost(ArrConnectionDefinition connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.WebhookHost))
        {
            var trimmed = connection.WebhookHost.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                {
                    return uri.Host;
                }
            }

            return trimmed;
        }

        var envHost = Environment.GetEnvironmentVariable("LEECHARR_HOST") ??
                        Environment.GetEnvironmentVariable("SEEDARR_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            return envHost.Trim();
        }

        if (IsLoopbackOrLocalhost(connection.Url))
        {
            return "127.0.0.1";
        }

        var bindAddress = this.configFileProvider?.BindAddress;
        if (!string.IsNullOrWhiteSpace(bindAddress) &&
            bindAddress != "*" &&
            bindAddress != "0.0.0.0" &&
            bindAddress != "::" &&
            !IsHexContainerId(bindAddress))
        {
            return bindAddress;
        }

        var hostname = Dns.GetHostName();
        return IsHexContainerId(hostname) ? "leecharr.local" : hostname;
    }

    private string GetLeecharrBaseUrl(ArrConnectionDefinition connection)
    {
        var urlBase = NormalizeUrlBase(this.configFileProvider?.UrlBase);

        var envUrl = Environment.GetEnvironmentVariable("LEECHARR_URL") ??
                    Environment.GetEnvironmentVariable("SEEDARR_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return AppendUrlBase(envUrl, urlBase);
        }

        var enableSsl = this.configFileProvider?.EnableSsl == true;
        var scheme = enableSsl ? "https" : "http";
        var port = enableSsl ? (this.configFileProvider?.SslPort ?? 7890) : (this.configFileProvider?.Port ?? 7889);

        var hostCandidate = connection?.WebhookHost;
        if (!string.IsNullOrWhiteSpace(hostCandidate))
        {
            if (hostCandidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                hostCandidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return AppendUrlBase(hostCandidate, urlBase);
            }

            return AppendUrlBase($"{scheme}://{FormatHostWithPort(hostCandidate, port)}", urlBase);
        }

        var envHost = Environment.GetEnvironmentVariable("LEECHARR_HOST") ??
                        Environment.GetEnvironmentVariable("SEEDARR_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            if (envHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                envHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return AppendUrlBase(envHost, urlBase);
            }

            return AppendUrlBase($"{scheme}://{FormatHostWithPort(envHost, port)}", urlBase);
        }

        var bindAddress = this.configFileProvider?.BindAddress;

        if (string.IsNullOrWhiteSpace(bindAddress) ||
            bindAddress == "*" ||
            bindAddress == "0.0.0.0" ||
            bindAddress == "::" ||
            IsHexContainerId(bindAddress))
        {
            if (IsLoopbackOrLocalhost(connection?.Url))
            {
                bindAddress = "127.0.0.1";
            }
            else
            {
                var hostname = Dns.GetHostName();
                bindAddress = IsHexContainerId(hostname) ? "leecharr.local" : hostname;
            }
        }

        return AppendUrlBase($"{scheme}://{FormatHostWithPort(bindAddress, port)}", urlBase);
    }

    private static string AppendUrlBase(string baseUrl, string urlBase)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmedBase = baseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(urlBase))
        {
            return trimmedBase;
        }

        if (trimmedBase.EndsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedBase;
        }

        return $"{trimmedBase}{urlBase}";
    }

    private static string FormatHostWithPort(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var trimmedHost = host.Trim().TrimEnd('/');

        bool hasPort;
        if (trimmedHost.StartsWith('[') && trimmedHost.Contains(']'))
        {
            hasPort = trimmedHost.Substring(trimmedHost.IndexOf(']') + 1).Contains(':');
        }
        else
        {
            hasPort = trimmedHost.Contains(':');
        }

        if (hasPort)
        {
            return trimmedHost;
        }

        if (port == 80 || port == 443)
        {
            return trimmedHost;
        }

        return $"{trimmedHost}:{port}";
    }

    private static string NormalizeUrlBase(string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return string.Empty;
        }

        var trimmed = urlBase.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : "/" + trimmed;
    }

    private static bool IsLoopbackOrLocalhost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return url.Contains("://localhost", StringComparison.OrdinalIgnoreCase)
            || url.Contains("://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || url.Contains("://[::1]", StringComparison.OrdinalIgnoreCase)
            || url.Contains("://0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHexContainerId(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        if (s.Length != 12 && s.Length != 64)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }
}
