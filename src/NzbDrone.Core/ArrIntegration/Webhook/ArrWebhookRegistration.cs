// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public class ArrWebhookRegistration : IArrWebhookRegistration
{
    private record ExistingWebhookInfo(int Id, string Url, string ApiKey);

    private record ExistingDelugeClient(
        int Id,
        string Host,
        string Port,
        string UseSsl,
        string UrlBase,
        string Password,
        bool HasPassword,
        string CategoryValue,
        bool HasCategory);

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
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
        if (connection != null &&
            !string.IsNullOrWhiteSpace(connection.Url) &&
            !ArrTargetUrlValidator.IsValidTargetUrl(connection.Url, out var validationError))
        {
            this.logger.Warn(
                "Skipping Arr registration for {0} ({1}): {2}",
                connection.Name,
                connection.Url,
                validationError);
            return false;
        }

        var whSuccess = this.RegisterWebhook(connection);
        var dcSuccess = this.RegisterDownloadClient(connection);
        return whSuccess && dcSuccess;
    }

    public bool Unregister(ArrConnectionDefinition connection)
    {
        if (connection != null &&
            !string.IsNullOrWhiteSpace(connection.Url) &&
            !ArrTargetUrlValidator.IsValidTargetUrl(connection.Url, out var validationError))
        {
            this.logger.Warn(
                "Skipping Arr unregistration for {0} ({1}): {2}",
                connection.Name,
                connection.Url,
                validationError);
            return false;
        }

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
            var path = isUpdate
                ? $"api/{apiVersion}/notification/{existing.Id}"
                : $"api/{apiVersion}/notification";

            if (!TryBuildArrUri(connection.Url, path, out var requestUri))
            {
                this.logger.Warn("Invalid Arr connection URL for {0}: {1}", connection.ArrType, connection.Url);
                return false;
            }

            var method = isUpdate ? HttpMethod.Put : HttpMethod.Post;
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = CreateArrRequest(method, requestUri, connection.ApiKey, content);

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

            var path = $"api/{apiVersion}/notification/{existing.Id}";
            if (!TryBuildArrUri(connection.Url, path, out var requestUri))
            {
                return false;
            }

            using var request = CreateArrRequest(HttpMethod.Delete, requestUri, connection.ApiKey);

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

            var host = this.ResolveDownloadClientHost(connection);
            var port = this.ResolveDownloadClientPort(connection);
            var useSsl = this.ResolveDownloadClientUseSsl(connection);
            var urlBase = NormalizeUrlBase(this.configFileProvider?.UrlBase);
            var password = this.configFileProvider?.ApiKey ?? string.Empty;
            var (catField, catVal) = GetCategoryFieldAndValue(connection);

            var existing = this.FindExistingDelugeDownloadClient(connection, apiVersion, catField);
            if (existing != null &&
                DownloadClientMatches(existing, host, port, useSsl, urlBase, password, catField, catVal))
            {
                this.logger.Debug("Leecharr download client already registered in {0} (client id {1})", connection.ArrType, existing.Id);
                return true;
            }

            var fields = new List<object>
            {
                new { name = "host", value = (object)host },
                new { name = "port", value = (object)port },
                new { name = "useSsl", value = (object)useSsl },
                new { name = "urlBase", value = (object)urlBase },
                new { name = "password", value = (object)password },
            };

            if (!string.IsNullOrWhiteSpace(catField))
            {
                fields.Add(new { name = catField, value = (object)catVal });
            }

            var body = new
            {
                id = existing == null ? 0 : existing.Id,
                name = "Leecharr",
                implementation = "Deluge",
                configContract = "DelugeSettings",
                enable = true,
                priority = 1,
                fields,
            };

            var json = JsonSerializer.Serialize(body);
            var isUpdate = existing != null;
            var path = isUpdate
                ? $"api/{apiVersion}/downloadclient/{existing.Id}"
                : $"api/{apiVersion}/downloadclient";
            if (!TryBuildArrUri(connection.Url, path, out var requestUri))
            {
                this.logger.Warn("Invalid Arr connection URL for {0}: {1}", connection.ArrType, connection.Url);
                return false;
            }

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var method = isUpdate ? HttpMethod.Put : HttpMethod.Post;
            using var request = CreateArrRequest(method, requestUri, connection.ApiKey, content);

            using var response = this.client.Send(request);
            if (response.IsSuccessStatusCode)
            {
                this.logger.Info(
                    "{0} Leecharr download client in {1} at {2}",
                    isUpdate ? "Updated" : "Registered",
                    connection.ArrType,
                    connection.Url);
                return true;
            }

            this.logger.Warn(
                "Failed to {0} download client in {1}: {2}",
                isUpdate ? "update" : "register",
                connection.ArrType,
                response.StatusCode);
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
            var existing = this.FindExistingDelugeDownloadClient(connection, apiVersion, null);
            if (existing == null)
            {
                return true;
            }

            var path = $"api/{apiVersion}/downloadclient/{existing.Id}";
            if (!TryBuildArrUri(connection.Url, path, out var requestUri))
            {
                return false;
            }

            using var request = CreateArrRequest(HttpMethod.Delete, requestUri, connection.ApiKey);

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
            var path = $"api/{apiVersion}/notification";
            if (!TryBuildArrUri(connection.Url, path, out var requestUri))
            {
                return null;
            }

            using var request = CreateArrRequest(HttpMethod.Get, requestUri, connection.ApiKey);

            using var response = this.client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);

            foreach (var notification in doc.RootElement.EnumerateArray())
            {
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

                var notificationName = notification.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString()
                    : null;
                if (!string.Equals(notificationName, "Leecharr", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var implementation = notification.TryGetProperty("implementation", out var implProp)
                    ? implProp.GetString()
                    : null;
                var isWebhookImplementation = string.IsNullOrEmpty(implementation) ||
                    string.Equals(implementation, "Webhook", StringComparison.OrdinalIgnoreCase);

                if (IsLeecharrArrWebhookUrl(url) &&
                    isWebhookImplementation &&
                    notification.TryGetProperty("id", out var idProp))
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

    private static bool IsLeecharrArrWebhookUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return url.Contains("/api/v1/webhook/arr", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/api/v1/webhooks/arr", StringComparison.OrdinalIgnoreCase);
    }

    private ExistingDelugeClient FindExistingDelugeDownloadClient(ArrConnectionDefinition connection, string apiVersion, string categoryField)
    {
        try
        {
            var path = $"api/{apiVersion}/downloadclient";
            if (!TryBuildArrUri(connection.Url, path, out var requestUri))
            {
                return null;
            }

            using var request = CreateArrRequest(HttpMethod.Get, requestUri, connection.ApiKey);

            using var response = this.client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);

            foreach (var dc in doc.RootElement.EnumerateArray())
            {
                var name = dc.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (!string.Equals(name, "Leecharr", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var implementation = dc.TryGetProperty("implementation", out var implProp) ? implProp.GetString() : null;
                if (!string.Equals(implementation, "Deluge", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!dc.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var id))
                {
                    continue;
                }

                return ReadExistingDelugeClient(id, dc, categoryField);
            }

            return null;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to check existing download clients in {0}", connection.ArrType);
            return null;
        }
    }

    private static ExistingDelugeClient ReadExistingDelugeClient(int id, JsonElement client, string categoryField)
    {
        string host = null;
        string port = null;
        string useSsl = null;
        string urlBase = null;
        string password = null;
        var hasPassword = false;
        string categoryValue = null;
        var hasCategory = false;

        if (client.TryGetProperty("fields", out var fieldArray) && fieldArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fieldArray.EnumerateArray())
            {
                var fieldName = field.TryGetProperty("name", out var fieldNameProp) ? fieldNameProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                var fieldValue = ReadJsonFieldValue(field);
                if (string.Equals(fieldName, "host", StringComparison.OrdinalIgnoreCase))
                {
                    host = fieldValue;
                }
                else if (string.Equals(fieldName, "port", StringComparison.OrdinalIgnoreCase))
                {
                    port = fieldValue;
                }
                else if (string.Equals(fieldName, "useSsl", StringComparison.OrdinalIgnoreCase))
                {
                    useSsl = fieldValue;
                }
                else if (string.Equals(fieldName, "urlBase", StringComparison.OrdinalIgnoreCase))
                {
                    urlBase = fieldValue;
                }
                else if (string.Equals(fieldName, "password", StringComparison.OrdinalIgnoreCase))
                {
                    hasPassword = true;
                    password = fieldValue;
                }
                else if (!string.IsNullOrWhiteSpace(categoryField) &&
                    string.Equals(fieldName, categoryField, StringComparison.OrdinalIgnoreCase))
                {
                    hasCategory = true;
                    categoryValue = fieldValue;
                }
            }
        }

        return new ExistingDelugeClient(id, host, port, useSsl, urlBase, password, hasPassword, categoryValue, hasCategory);
    }

    private static string ReadJsonFieldValue(JsonElement field)
    {
        if (!field.TryGetProperty("value", out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return value.ToString();
    }

    private static bool DownloadClientMatches(
        ExistingDelugeClient existing,
        string host,
        int port,
        bool useSsl,
        string urlBase,
        string password,
        string categoryField,
        string categoryValue)
    {
        return HostFieldEquals(existing.Host, host) &&
            PortFieldEquals(existing.Port, port) &&
            BoolFieldEquals(existing.UseSsl, useSsl) &&
            UrlBaseFieldEquals(existing.UrlBase, urlBase) &&
            PasswordFieldEquals(existing.Password, existing.HasPassword, password) &&
            CategoryFieldEquals(existing, categoryField, categoryValue);
    }

    private static bool HostFieldEquals(string existing, string desired)
    {
        if (string.IsNullOrWhiteSpace(existing) || string.IsNullOrWhiteSpace(desired))
        {
            return false;
        }

        return string.Equals(
            FormatHostAuthority(existing.Trim()),
            FormatHostAuthority(desired.Trim()),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PortFieldEquals(string existing, int desired)
    {
        return int.TryParse(existing, out var parsed) && parsed == desired;
    }

    private static bool BoolFieldEquals(string existing, bool desired)
    {
        return bool.TryParse(existing, out var parsed) && parsed == desired;
    }

    private static bool UrlBaseFieldEquals(string existing, string desired)
    {
        return string.Equals(NormalizeUrlBase(existing), NormalizeUrlBase(desired), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PasswordFieldEquals(string existing, bool hasPassword, string desired)
    {
        var wanted = desired ?? string.Empty;
        if (!hasPassword)
        {
            return wanted.Length == 0;
        }

        return string.Equals(existing ?? string.Empty, wanted, StringComparison.Ordinal);
    }

    private static bool CategoryFieldEquals(ExistingDelugeClient existing, string fieldName, string desired)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return true;
        }

        if (!existing.HasCategory)
        {
            return false;
        }

        return string.Equals(existing.CategoryValue ?? string.Empty, desired ?? string.Empty, StringComparison.Ordinal);
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

        if (string.Equals(arrType, "Whisparr", StringComparison.OrdinalIgnoreCase))
        {
            return ("movieCategory", !string.IsNullOrWhiteSpace(connection.Category) ? connection.Category : "whisparr");
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
                    return FormatHostAuthority(uri.Host);
                }
            }

            return FormatHostAuthority(trimmed);
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

    private int ResolveDownloadClientPort(ArrConnectionDefinition connection)
    {
        if (TryParseAbsoluteWebhookHost(connection, out var uri) && uri.Port > 0)
        {
            return uri.Port;
        }

        if (this.configFileProvider?.EnableSsl == true)
        {
            var sslPort = this.configFileProvider.SslPort;
            return sslPort > 0 ? sslPort : 7890;
        }

        return this.configFileProvider?.Port > 0 ? this.configFileProvider.Port : 7889;
    }

    private bool ResolveDownloadClientUseSsl(ArrConnectionDefinition connection)
    {
        if (TryParseAbsoluteWebhookHost(connection, out var uri))
        {
            return uri.Scheme == Uri.UriSchemeHttps;
        }

        return this.configFileProvider?.EnableSsl == true;
    }

    private static bool TryParseAbsoluteWebhookHost(ArrConnectionDefinition connection, out Uri uri)
    {
        uri = null;
        if (connection == null || string.IsNullOrWhiteSpace(connection.WebhookHost))
        {
            return false;
        }

        var trimmed = connection.WebhookHost.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
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
        var port = enableSsl ? this.configFileProvider.SslPort : (this.configFileProvider?.Port ?? 7889);

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
        trimmedHost = FormatHostAuthority(trimmedHost);

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

    private static string FormatHostAuthority(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var trimmed = host.Trim();
        if (trimmed.StartsWith('[') && trimmed.Contains(']'))
        {
            return trimmed;
        }

        if (IPAddress.TryParse(trimmed, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{trimmed}]";
        }

        return trimmed;
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

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return address.GetAddressBytes()[0] == 0;
        }

        return false;
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
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildArrUri(string baseUrl, string relativePath, out Uri uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsedBase))
        {
            return false;
        }

        if (parsedBase.Scheme != Uri.UriSchemeHttp && parsedBase.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var cleanBase = ArrConnectionUrlNormalizer.StripApiSuffix(parsedBase.GetLeftPart(UriPartial.Path));
        var cleanRelative = relativePath.TrimStart('/');
        return Uri.TryCreate($"{cleanBase}/{cleanRelative}", UriKind.Absolute, out uri);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("roslyn.sonaranalyzer.security.cs", "S5144", Justification = "Outbound integration webhook/client registration with validated HTTP/HTTPS URI")]
    [SuppressMessage("Security", "S5144:Server-Side Request Forgery (SSRF)", Justification = "Outbound integration webhook/client registration with validated HTTP/HTTPS URI")]
    private static HttpRequestMessage CreateArrRequest(HttpMethod method, Uri uri, string apiKey, HttpContent content = null)
    {
#pragma warning disable S5144
        var request = new HttpRequestMessage(method, uri);
#pragma warning restore S5144
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }
}
