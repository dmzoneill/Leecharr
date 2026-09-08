// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Http.Transport;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Notifications;

public interface IWebhookDispatcher
{
    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);
}

public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly HttpClient httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> retryPolicy;
    private readonly Logger logger;
    private readonly TimeSpan timeout;
    private readonly bool allowLoopback;

    public TimeSpan Timeout => this.timeout;

    public WebhookDispatcher(IHttpTransportEngine transportEngine = null, HttpClient httpClient = null, TimeSpan? timeout = null, bool allowLoopback = false)
        : this(
            httpClient ?? (transportEngine != null ? new HttpClient(new DynamicHttpTransportHandler(transportEngine), disposeHandler: true) { Timeout = timeout ?? TimeSpan.FromSeconds(10) } : new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) }),
            null,
            timeout,
            allowLoopback)
    {
    }

    public WebhookDispatcher(HttpClient httpClient, TimeSpan? timeout = null, bool allowLoopback = false)
        : this(null, httpClient, timeout, allowLoopback)
    {
    }

    internal WebhookDispatcher(HttpClient httpClient, AsyncRetryPolicy<HttpResponseMessage> retryPolicy, TimeSpan? timeout = null, bool allowLoopback = false)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(10);
        this.allowLoopback = allowLoopback;
        this.httpClient = httpClient ?? new HttpClient { Timeout = this.timeout };
        this.logger = LogManager.GetCurrentClassLogger();
        this.retryPolicy = retryPolicy ?? CreateRetryPolicy();
    }

    public static bool IsValidTargetUrl(string targetUrl, bool allowLoopback = false)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (allowLoopback)
        {
            return true;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
            {
                return false;
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 127 ||
                    bytes[0] == 0 ||
                    (bytes[0] == 169 && bytes[1] == 254) ||
                    (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) ||
                    bytes[0] >= 224)
                {
                    return false;
                }
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                {
                    return false;
                }

                if (IPAddress.IPv6Any.Equals(ip) || IPAddress.IPv6None.Equals(ip) || IPAddress.IPv6Loopback.Equals(ip))
                {
                    return false;
                }

                if (ip.IsIPv4MappedToIPv6)
                {
                    var ipv4 = ip.MapToIPv4();
                    var bytes = ipv4.GetAddressBytes();
                    if (bytes[0] == 127 ||
                        bytes[0] == 0 ||
                        (bytes[0] == 169 && bytes[1] == 254) ||
                        bytes[0] >= 224)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    internal static AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy(
        int retryCount = 3,
        Func<int, TimeSpan> sleepDurationProvider = null,
        Action<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context> onRetry = null)
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount,
                sleepDurationProvider ?? (retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))), // 2s, 4s, 8s
                (outcome, timespan, retryAttempt, context) =>
                {
                    outcome.Result?.Dispose();

                    if (onRetry != null)
                    {
                        onRetry(outcome, timespan, retryAttempt, context);
                    }
                    else
                    {
                        LogManager.GetCurrentClassLogger().Warn("Webhook dispatch failed. Retrying in {0}s (Attempt {1}/{2})...", timespan.TotalSeconds, retryAttempt, retryCount);
                    }
                });
    }

    public async Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        if (!IsValidTargetUrl(targetUrl, this.allowLoopback))
        {
            this.logger.Warn("Webhook dispatch blocked: Invalid or prohibited target URL (SSRF protection): {0}", targetUrl);
            return false;
        }

        try
        {
            using var response = await this.retryPolicy.ExecuteAsync(
                async (ct) =>
                {
                    var request = this.BuildHttpRequest(targetUrl, payload, customHeadersJson);
                    return await this.httpClient.SendAsync(request, ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                this.logger.Info("Webhook successfully dispatched to {0} (Status: {1})", targetUrl, response.StatusCode);
                return true;
            }

            this.logger.Warn("Webhook dispatch to {0} returned non-success status code: {1}", targetUrl, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to dispatch webhook to {0}", targetUrl);
            return false;
        }
    }

    private HttpRequestMessage BuildHttpRequest(string targetUrl, object payload, string customHeadersJson)
    {
        HttpContent content;

        if (targetUrl.Contains("pushover.net", StringComparison.OrdinalIgnoreCase) &&
            payload is IDictionary<string, object> dict)
        {
            var formPairs = dict.Select(kvp =>
                new KeyValuePair<string, string>(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
            content = new FormUrlEncodedContent(formPairs);
        }
        else if (targetUrl.Contains("pushover.net", StringComparison.OrdinalIgnoreCase) &&
                 payload is IDictionary<string, string> stringDict)
        {
            content = new FormUrlEncodedContent(stringDict);
        }
        else
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = content,
        };

        this.AttachCustomHeaders(request, customHeadersJson);

        return request;
    }

    private void AttachCustomHeaders(HttpRequestMessage request, string customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
        {
            return;
        }

        var trimmed = customHeadersJson.Trim();

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var key = prop.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        var val = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()?.Trim()
                            : prop.Value.GetRawText().Trim();

                        this.AddHeader(request, key, val);
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to parse custom headers JSON. Attempting fallback key-value parsing.");
            }
        }

        // Fallback or line-based key-value parsing (e.g. "Header: Value" or "Header=Value")
        try
        {
            var lines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var addedAny = false;
            foreach (var line in lines)
            {
                var cleanLine = line.Trim();
                if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.StartsWith("#") || cleanLine.StartsWith("//"))
                {
                    continue;
                }

                var separatorIndex = cleanLine.IndexOfAny(new[] { ':', '=' });
                if (separatorIndex > 0)
                {
                    var key = cleanLine.Substring(0, separatorIndex).Trim();
                    var val = cleanLine.Substring(separatorIndex + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        this.AddHeader(request, key, val);
                        addedAny = true;
                    }
                }
            }

            if (!addedAny && !trimmed.StartsWith("{"))
            {
                this.logger.Warn("Could not parse custom headers from input (header keys: {0})", RedactHeadersForLogging(customHeadersJson));
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to parse custom headers (header keys: {0})", RedactHeadersForLogging(customHeadersJson));
        }
    }

    internal static string RedactHeadersForLogging(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        try
        {
            var lines = input.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var keys = new List<string>();
            foreach (var line in lines)
            {
                var clean = line.Trim().Trim('{', '}', '"');
                var sep = clean.IndexOfAny(new[] { ':', '=' });
                if (sep > 0)
                {
                    keys.Add(clean.Substring(0, sep).Trim().Trim('"'));
                }
            }

            return keys.Count > 0 ? string.Join(", ", keys) : "[unparseable headers]";
        }
        catch
        {
            return "[redacted]";
        }
    }

    private void AddHeader(HttpRequestMessage request, string key, string value)
    {
        var added = request.Headers.TryAddWithoutValidation(key, value ?? string.Empty);
        if (!added && request.Content != null)
        {
            added = request.Content.Headers.TryAddWithoutValidation(key, value ?? string.Empty);
        }

        if (added)
        {
            this.logger.Debug("Attached custom header '{0}' to webhook request", key);
        }
        else
        {
            this.logger.Warn("Failed to add custom header '{0}' to outgoing request", key);
        }
    }
}
