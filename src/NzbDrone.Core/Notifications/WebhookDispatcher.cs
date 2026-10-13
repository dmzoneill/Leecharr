// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Trackers;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Notifications;

public class WebhookDispatchResult
{
    public bool Success { get; set; }

    public HttpStatusCode? StatusCode { get; set; }

    public string Message { get; set; }

    public string ResponseBodySnippet { get; set; }
}

public interface IWebhookDispatcher
{
    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);

    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default);

    Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);

    Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default);
}

public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly HttpClient httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> retryPolicy;
    private readonly Logger logger;
    private static readonly Logger StaticLogger = LogManager.GetCurrentClassLogger();
    private static readonly char[] LineSeparators = ['\r', '\n'];
    private static readonly char[] HeaderLineSeparators = ['\r', '\n', ','];
    private static readonly SearchValues<char> KeyValueSeparators = SearchValues.Create([':', '=']);
    private static readonly string[] SensitiveRedirectHeaderNames =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
    ];
    private readonly TimeSpan timeout;
    private readonly bool allowLoopback;
    private readonly bool allowPrivateNetworkRequests;

    public TimeSpan Timeout => this.timeout;

    public WebhookDispatcher(IHttpTransportEngine transportEngine = null, HttpClient httpClient = null, TimeSpan? timeout = null, bool allowLoopback = false, IConfigService configService = null)
        : this(
            httpClient,
            null,
            timeout,
            allowLoopback,
            configService?.AllowPrivateNetworkRequests ?? false,
            transportEngine)
    {
    }

    public WebhookDispatcher(HttpClient httpClient, TimeSpan? timeout = null, bool allowLoopback = false)
        : this(httpClient, null, timeout, allowLoopback, false)
    {
    }

    internal WebhookDispatcher(HttpClient httpClient, AsyncRetryPolicy<HttpResponseMessage> retryPolicy, TimeSpan? timeout = null, bool allowLoopback = false, bool allowPrivateNetworkRequests = false, IHttpTransportEngine transportEngine = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(10);
        this.allowLoopback = allowLoopback;
        this.allowPrivateNetworkRequests = allowPrivateNetworkRequests;
        this.httpClient = httpClient ?? CreateDefaultHttpClient(transportEngine, this.timeout, allowLoopback, allowPrivateNetworkRequests);
        this.logger = LogManager.GetCurrentClassLogger();
        this.retryPolicy = retryPolicy ?? CreateRetryPolicy();
    }

    private static HttpClient CreateDefaultHttpClient(IHttpTransportEngine transportEngine, TimeSpan timeout, bool allowLoopback, bool allowPrivateNetworkRequests)
    {
        var handler = transportEngine != null
            ? (HttpMessageHandler)new DynamicHttpTransportHandler(transportEngine)
            : CreatePinnedSocketsHttpHandler(allowLoopback, allowPrivateNetworkRequests);

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout,
        };
    }

    internal static SocketsHttpHandler CreatePinnedSocketsHttpHandler(
        bool allowLoopback,
        bool allowPrivateNetworkRequests,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync = null)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = (context, cancellationToken) =>
                ConnectPinnedAsync(context, allowLoopback, allowPrivateNetworkRequests, resolveHostAsync, cancellationToken),
        };
    }

    internal static async ValueTask<Stream> ConnectPinnedAsync(
        SocketsHttpConnectionContext context,
        bool allowLoopback,
        bool allowPrivateNetworkRequests,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = await ResolveForConnectAsync(host, resolveHostAsync, cancellationToken).ConfigureAwait(false);
        EnsureConnectable(host, addresses, allowLoopback, allowPrivateNetworkRequests);

        Exception lastError = null;
        foreach (var address in addresses)
        {
            var targetIp = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(targetIp, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                lastError = ex;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        if (lastError != null)
        {
            throw lastError;
        }

        throw new SocketException((int)SocketError.HostUnreachable);
    }

    internal static void EnsureConnectable(string host, IPAddress[] addresses, bool allowLoopback, bool allowPrivateNetworkRequests)
    {
        if (addresses == null || addresses.Length == 0)
        {
            throw new SecurityException($"Unable to resolve IP address for host '{host}'.");
        }

        foreach (var address in addresses)
        {
            if (IsBlockedIp(address, allowLoopback, allowPrivateNetworkRequests))
            {
                throw new SecurityException($"SSRF blocked: IP address '{address}' for host '{host}' is prohibited.");
            }
        }
    }

    private static async Task<IPAddress[]> ResolveForConnectAsync(
        string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsedIp))
        {
            return [parsedIp];
        }

        try
        {
            if (resolveHostAsync != null)
            {
                return await resolveHostAsync(host, cancellationToken).ConfigureAwait(false);
            }

            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecurityException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StaticLogger.Trace(ex, "DNS lookup failed during connect for host '{0}'", host);
            throw new SecurityException($"Unable to resolve IP address for host '{host}'.", ex);
        }
    }

    public static bool IsValidTargetUrl(string targetUrl, bool allowLoopback = false, bool allowPrivateNetworkRequests = false)
    {
        return IsValidTargetUrl(targetUrl, allowLoopback, allowPrivateNetworkRequests, resolveHost: null);
    }

    internal static bool IsValidTargetUrl(string targetUrl, bool allowLoopback, bool allowPrivateNetworkRequests, Func<string, IPAddress[]> resolveHost)
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

        if (string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return allowPrivateNetworkRequests;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsBlockedIp(ip, allowLoopback, allowPrivateNetworkRequests))
            {
                return false;
            }
        }
        else
        {
            IPAddress[] addresses;
            try
            {
                addresses = resolveHost != null ? resolveHost(host) : Dns.GetHostAddresses(host);
            }
            catch (Exception ex)
            {
                StaticLogger.Trace(ex, "DNS lookup failed during validation for host '{0}'", host);
                return false;
            }

            if (addresses == null || addresses.Length == 0 || addresses.Any(a => IsBlockedIp(a, allowLoopback, allowPrivateNetworkRequests)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsBlockedIp(IPAddress ip, bool allowLoopback = false, bool allowPrivateNetworkRequests = false)
    {
        if (ip == null)
        {
            return true;
        }

        if (allowLoopback)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (allowPrivateNetworkRequests && IsAllowedPrivateNetworkIp(ip))
        {
            return false;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
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
                return true;
            }

            // Private CIDRs (RFC 1918) and Carrier-Grade NAT (RFC 6598)
            if (bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 100 && (bytes[1] & 0xC0) == 64))
            {
                return true;
            }
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return true;
            }

            if (IPAddress.IPv6Any.Equals(ip) || IPAddress.IPv6None.Equals(ip) || IPAddress.IPv6Loopback.Equals(ip))
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();

            // IPv6 Unique Local Addresses (fc00::/7)
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedPrivateNetworkIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return true;
            }

            if (bytes[0] == 127)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();

            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }
        }

        return false;
    }

    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(1);

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
                sleepDurationProvider: (retryAttempt, outcome, context) =>
                {
                    if (outcome.Result != null)
                    {
                        var retryAfter = ExtractRetryAfter(outcome.Result);
                        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                        {
                            return retryAfter.Value > MaxRetryAfter ? MaxRetryAfter : retryAfter.Value;
                        }
                    }

                    return sleepDurationProvider != null
                        ? sleepDurationProvider(retryAttempt)
                        : TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                },
                onRetryAsync: (outcome, timespan, retryAttempt, context) =>
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

                    return Task.CompletedTask;
                });
    }

    internal static TimeSpan? ExtractRetryAfter(HttpResponseMessage response)
    {
        if (response == null)
        {
            return null;
        }

        try
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    return CapRetryAfter(response.Headers.RetryAfter.Delta.Value);
                }

                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    var delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero)
                    {
                        return CapRetryAfter(delta);
                    }
                }
            }

            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                    {
                        return CapRetryAfter(TimeSpan.FromSeconds(seconds));
                    }

                    if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        var delta = date - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero)
                        {
                            return CapRetryAfter(delta);
                        }
                    }
                }
            }

            if (response.Headers.TryGetValues("X-Retry-After", out var xRetryValues))
            {
                var val = xRetryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(val) &&
                    double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                {
                    return CapRetryAfter(TimeSpan.FromSeconds(seconds));
                }
            }

            if (response.Content != null)
            {
                Stream stream = null;
                try
                {
                    var s = response.Content.ReadAsStream();
                    if (s is MemoryStream)
                    {
                        stream = s;
                    }
                }
                catch (Exception ex)
                {
                    StaticLogger.Trace(ex, "Response stream unreadable or not backed by memory stream");
                }

                if (stream is MemoryStream memStream && memStream.Length > 0 && memStream.Length <= 16384)
                {
                    var currentPos = memStream.Position;
                    try
                    {
                        memStream.Position = 0;
                        using var reader = new StreamReader(memStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                        var rawBody = reader.ReadToEnd();
                        memStream.Position = currentPos;

                        if (!string.IsNullOrWhiteSpace(rawBody) && rawBody.TrimStart().StartsWith('{'))
                        {
                            using var doc = JsonDocument.Parse(rawBody);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("parameters", out var paramsElem) &&
                                paramsElem.ValueKind == JsonValueKind.Object &&
                                paramsElem.TryGetProperty("retry_after", out var tgRetry))
                            {
                                if (tgRetry.TryGetDouble(out var s))
                                {
                                    return CapRetryAfter(TimeSpan.FromSeconds(s));
                                }
                            }

                            var retryProps = new[] { "retry_after", "retryAfter", "retry_after_seconds", "retryAfterSeconds" };
                            foreach (var prop in retryProps)
                            {
                                if (root.TryGetProperty(prop, out var elem))
                                {
                                    if (elem.ValueKind == JsonValueKind.Number && elem.TryGetDouble(out var s))
                                    {
                                        return CapRetryAfter(TimeSpan.FromSeconds(s));
                                    }

                                    if (elem.ValueKind == JsonValueKind.String && double.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sParsed))
                                    {
                                        return CapRetryAfter(TimeSpan.FromSeconds(sParsed));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        memStream.Position = currentPos;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Response header or content inspection failed; fallback to default retry policy
        }

        return null;
    }

    private static TimeSpan? CapRetryAfter(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaxRetryAfter ? MaxRetryAfter : delay;
    }

    internal static bool ShouldUseGetWithoutBodyOnRedirect(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.MovedPermanently
            || statusCode == HttpStatusCode.Redirect
            || statusCode == HttpStatusCode.SeeOther;
    }

    internal static bool IsSameHostRedirect(Uri initialUri, Uri redirectUri)
    {
        if (initialUri == null || redirectUri == null)
        {
            return false;
        }

        return string.Equals(initialUri.Host, redirectUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    internal static string SanitizeCustomHeadersForRedirect(string customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
        {
            return customHeadersJson;
        }

        var trimmed = customHeadersJson.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return customHeadersJson;
                }

                var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var key = prop.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(key) || IsSensitiveRedirectHeader(key))
                    {
                        continue;
                    }

                    var val = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.GetRawText();
                    sanitized[key] = val ?? string.Empty;
                }

                return sanitized.Count == 0 ? null : JsonSerializer.Serialize(sanitized);
            }
            catch (Exception ex)
            {
                StaticLogger.Trace(ex, "Failed to sanitize custom headers JSON for redirect");
            }
        }

        try
        {
            var lines = trimmed.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            foreach (var line in lines)
            {
                var cleanLine = line.Trim();
                if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.StartsWith('#') || cleanLine.StartsWith("//"))
                {
                    continue;
                }

                var separatorIndex = cleanLine.AsSpan().IndexOfAny(KeyValueSeparators);
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = cleanLine.Substring(0, separatorIndex).Trim();
                if (string.IsNullOrWhiteSpace(key) || IsSensitiveRedirectHeader(key))
                {
                    continue;
                }

                kept.Add(cleanLine);
            }

            return kept.Count == 0 ? null : string.Join("\r\n", kept);
        }
        catch (Exception ex)
        {
            StaticLogger.Trace(ex, "Failed to sanitize line-based custom headers for redirect");
            return customHeadersJson;
        }
    }

    private static bool IsSensitiveRedirectHeader(string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return false;
        }

        foreach (var sensitive in SensitiveRedirectHeaderNames)
        {
            if (string.Equals(headerName, sensitive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default)
    {
        return await this.DispatchAsync(targetUrl, payload, customHeadersJson, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default)
    {
        var result = await this.DispatchDetailedAsync(targetUrl, payload, customHeadersJson, httpMethod ?? HttpMethod.Post, cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default)
    {
        return await this.DispatchDetailedAsync(targetUrl, payload, customHeadersJson, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return new WebhookDispatchResult
            {
                Success = false,
                Message = "Target webhook URL is required.",
            };
        }

        if (!IsValidTargetUrl(targetUrl, this.allowLoopback, this.allowPrivateNetworkRequests))
        {
            this.logger.Warn("Webhook dispatch blocked: Invalid or prohibited target URL (SSRF protection): {0}", RedactTargetUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                Message = $"Target URL '{RedactTargetUrlForLogging(targetUrl)}' is prohibited (SSRF protection: loopback, link-local, and cloud metadata addresses are not permitted).",
            };
        }

        try
        {
            var currentUrl = targetUrl;
            var initialUri = new Uri(targetUrl);
            var redirectCount = 0;
            const int maxRedirects = 5;
            var currentMethod = httpMethod ?? HttpMethod.Post;
            var currentPayload = payload;
            var currentCustomHeaders = customHeadersJson;

            while (redirectCount <= maxRedirects)
            {
                using var response = await this.retryPolicy.ExecuteAsync(
                    async (ct) =>
                    {
                        var request = this.BuildHttpRequest(currentUrl, currentPayload, currentCustomHeaders, currentMethod);
                        return await this.httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);

                if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 399 && response.Headers.Location != null)
                {
                    redirectCount++;
                    if (redirectCount > maxRedirects)
                    {
                        this.logger.Warn("Webhook dispatch to {0} exceeded maximum redirect limit ({1})", RedactTargetUrlForLogging(targetUrl), maxRedirects);
                        return new WebhookDispatchResult
                        {
                            Success = false,
                            StatusCode = response.StatusCode,
                            Message = $"Webhook dispatch to {RedactTargetUrlForLogging(targetUrl)} exceeded maximum redirect limit ({maxRedirects}).",
                        };
                    }

                    var baseUri = new Uri(currentUrl);
                    if (!Uri.TryCreate(baseUri, response.Headers.Location, out var redirectUri))
                    {
                        this.logger.Warn("Webhook dispatch to {0} returned invalid redirect location: {1}", RedactTargetUrlForLogging(currentUrl), response.Headers.Location);
                        return new WebhookDispatchResult
                        {
                            Success = false,
                            StatusCode = response.StatusCode,
                            Message = $"Webhook dispatch returned invalid redirect location: {response.Headers.Location}.",
                        };
                    }

                    var nextUrl = redirectUri.AbsoluteUri;
                    if (!IsValidTargetUrl(nextUrl, this.allowLoopback, this.allowPrivateNetworkRequests))
                    {
                        this.logger.Warn("Webhook redirect blocked: Target prohibited by SSRF protection: {0}", RedactTargetUrlForLogging(nextUrl));
                        return new WebhookDispatchResult
                        {
                            Success = false,
                            StatusCode = response.StatusCode,
                            Message = $"Webhook redirect blocked: Target prohibited by SSRF protection: {RedactTargetUrlForLogging(nextUrl)}",
                        };
                    }

                    if (!IsSameHostRedirect(initialUri, redirectUri))
                    {
                        this.logger.Warn("Webhook redirect blocked: Cross-host redirect is not permitted ({0} -> {1})", RedactTargetUrlForLogging(currentUrl), RedactTargetUrlForLogging(nextUrl));
                        return new WebhookDispatchResult
                        {
                            Success = false,
                            StatusCode = response.StatusCode,
                            Message = $"Webhook redirect blocked: Cross-host redirect is not permitted ({RedactTargetUrlForLogging(currentUrl)} -> {RedactTargetUrlForLogging(nextUrl)}).",
                        };
                    }

                    this.logger.Debug("Webhook redirected from {0} to {1}", RedactTargetUrlForLogging(currentUrl), RedactTargetUrlForLogging(nextUrl));
                    currentCustomHeaders = SanitizeCustomHeadersForRedirect(currentCustomHeaders);
                    if (ShouldUseGetWithoutBodyOnRedirect(response.StatusCode))
                    {
                        currentMethod = HttpMethod.Get;
                        currentPayload = null;
                    }

                    currentUrl = nextUrl;
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    this.logger.Info("Webhook successfully dispatched to {0} (Status: {1})", RedactTargetUrlForLogging(currentUrl), response.StatusCode);
                    return new WebhookDispatchResult
                    {
                        Success = true,
                        StatusCode = response.StatusCode,
                        Message = $"Webhook dispatched successfully (HTTP {(int)response.StatusCode} {response.StatusCode}).",
                    };
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var responseBody = response.Content != null
                        ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                        : string.Empty;

                    if (responseBody.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase))
                    {
                        this.logger.Warn("Telegram entity parse failure for {0}: {1}. Retrying once with plain text without parse_mode.", RedactTargetUrlForLogging(currentUrl), responseBody);

                        var fallbackPayload = RemoveParseMode(payload);
                        using var fallbackResponse = await this.retryPolicy.ExecuteAsync(
                            async (ct) =>
                            {
                                var fallbackRequest = this.BuildHttpRequest(currentUrl, fallbackPayload, customHeadersJson, httpMethod);
                                return await this.httpClient.SendAsync(fallbackRequest, ct).ConfigureAwait(false);
                            },
                            cancellationToken).ConfigureAwait(false);

                        if (fallbackResponse.IsSuccessStatusCode)
                        {
                            this.logger.Info("Webhook successfully dispatched to {0} on plain text retry (Status: {1})", RedactTargetUrlForLogging(currentUrl), fallbackResponse.StatusCode);
                            return new WebhookDispatchResult
                            {
                                Success = true,
                                StatusCode = fallbackResponse.StatusCode,
                                Message = $"Webhook dispatched successfully (HTTP {(int)fallbackResponse.StatusCode} {fallbackResponse.StatusCode}).",
                            };
                        }

                        this.logger.Warn("Webhook plain text retry to {0} returned non-success status code: {1}", RedactTargetUrlForLogging(currentUrl), fallbackResponse.StatusCode);
                        var fallbackSnippet = await ExtractBodySnippetAsync(fallbackResponse, cancellationToken).ConfigureAwait(false);
                        var fallbackReason = fallbackResponse.ReasonPhrase ?? fallbackResponse.StatusCode.ToString();
                        return new WebhookDispatchResult
                        {
                            Success = false,
                            StatusCode = fallbackResponse.StatusCode,
                            ResponseBodySnippet = fallbackSnippet,
                            Message = string.IsNullOrEmpty(fallbackSnippet)
                                ? $"Webhook endpoint returned HTTP {(int)fallbackResponse.StatusCode} ({fallbackReason})."
                                : $"Webhook endpoint returned HTTP {(int)fallbackResponse.StatusCode} ({fallbackReason}): {fallbackSnippet}",
                        };
                    }
                }

                var bodySnippet = await ExtractBodySnippetAsync(response, cancellationToken).ConfigureAwait(false);
                var reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                this.logger.Warn("Webhook dispatch to {0} returned non-success status code: {1}", RedactTargetUrlForLogging(currentUrl), response.StatusCode);
                return new WebhookDispatchResult
                {
                    Success = false,
                    StatusCode = response.StatusCode,
                    ResponseBodySnippet = bodySnippet,
                    Message = string.IsNullOrEmpty(bodySnippet)
                        ? $"Webhook endpoint returned HTTP {(int)response.StatusCode} ({reason})."
                        : $"Webhook endpoint returned HTTP {(int)response.StatusCode} ({reason}): {bodySnippet}",
                };
            }

            return new WebhookDispatchResult
            {
                Success = false,
                Message = "Webhook dispatch failed.",
            };
        }
        catch (HttpRequestException ex)
        {
            this.logger.Error(ex, "HTTP error while dispatching webhook to {0}", RedactTargetUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                StatusCode = ex.StatusCode,
                Message = $"HTTP request failed: {ex.Message}",
            };
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            this.logger.Error(ex, "Webhook dispatch to {0} timed out", RedactTargetUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                Message = "Webhook request timed out.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to dispatch webhook to {0}", RedactTargetUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                Message = $"Webhook dispatch failed: {ex.Message}",
            };
        }
    }

    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    internal static object RemoveParseMode(object payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is IDictionary<string, object> dict)
        {
            var newDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dict)
            {
                AddPlainTextField(newDict, kvp.Key, kvp.Value);
            }

            return newDict;
        }

        if (payload is IDictionary<string, string> stringDict)
        {
            var newDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in stringDict)
            {
                if (IsParseModeKey(kvp.Key))
                {
                    continue;
                }

                newDict[kvp.Key] = IsTelegramTextKey(kvp.Key)
                    ? UnescapeTelegramMarkdown(kvp.Value)
                    : kvp.Value;
            }

            return newDict;
        }

        if (payload is string jsonStr && jsonStr.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return CopyObjectWithoutParseMode(doc.RootElement);
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Trace(ex, "Failed to parse JSON string in SanitizeTelegramPayload");
            }
        }

        try
        {
            var json = JsonSerializer.Serialize(payload, DefaultJsonOptions);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return CopyObjectWithoutParseMode(doc.RootElement);
            }
        }
        catch (Exception ex)
        {
            StaticLogger.Trace(ex, "Failed to serialize/deserialize payload in SanitizeTelegramPayload");
        }

        return payload;
    }

    internal static string UnescapeTelegramMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                sb.Append(text[i + 1]);
                i++;
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static bool IsParseModeKey(string key)
    {
        return string.Equals(key, "parse_mode", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "parseMode", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTelegramTextKey(string key)
    {
        return string.Equals(key, "text", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddPlainTextField(IDictionary<string, object> target, string key, object value)
    {
        if (IsParseModeKey(key))
        {
            return;
        }

        target[key] = IsTelegramTextKey(key) ? UnescapeTelegramText(value) : value;
    }

    private static Dictionary<string, object> CopyObjectWithoutParseMode(JsonElement root)
    {
        var dictFromJson = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            if (IsParseModeKey(prop.Name))
            {
                continue;
            }

            dictFromJson[prop.Name] = IsTelegramTextKey(prop.Name)
                ? UnescapeTelegramText(prop.Value.Clone())
                : prop.Value.Clone();
        }

        return dictFromJson;
    }

    private static object UnescapeTelegramText(object value)
    {
        switch (value)
        {
            case string text:
                return UnescapeTelegramMarkdown(text);
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return UnescapeTelegramMarkdown(element.GetString() ?? string.Empty);
            default:
                return value;
        }
    }

    private static async Task<string> ExtractBodySnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response?.Content == null)
        {
            return string.Empty;
        }

        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var bodySnippet = raw.Trim().Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                if (bodySnippet.Length > 256)
                {
                    bodySnippet = string.Concat(bodySnippet.AsSpan(0, 256), "...");
                }

                return bodySnippet;
            }
        }
        catch (Exception ex)
        {
            StaticLogger.Trace(ex, "Content read error or cancellation while extracting body snippet");
        }

        return string.Empty;
    }

    private HttpRequestMessage BuildHttpRequest(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod = null)
    {
        HttpContent content = null;
        var method = httpMethod ?? HttpMethod.Post;

        if (payload != null && method != HttpMethod.Get && method != HttpMethod.Head)
        {
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
            else if (payload is string strPayload)
            {
                var trimmed = strPayload.TrimStart();
                var mediaType = (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                    ? "application/json"
                    : "text/plain";
                content = new StringContent(strPayload, Encoding.UTF8, mediaType);
            }
            else
            {
                var json = JsonSerializer.Serialize(payload, DefaultJsonOptions);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }
        }

        var request = new HttpRequestMessage(method, targetUrl)
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

        if (trimmed.StartsWith('{'))
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
            var lines = trimmed.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            var addedAny = false;
            foreach (var line in lines)
            {
                var cleanLine = line.Trim();
                if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.StartsWith('#') || cleanLine.StartsWith("//"))
                {
                    continue;
                }

                var separatorIndex = cleanLine.AsSpan().IndexOfAny(KeyValueSeparators);
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

            if (!addedAny && !trimmed.StartsWith('{'))
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
            var lines = input.Split(HeaderLineSeparators, StringSplitOptions.RemoveEmptyEntries);
            var keys = new List<string>();
            foreach (var line in lines)
            {
                var clean = line.Trim().Trim('{', '}', '"');
                var sep = clean.AsSpan().IndexOfAny(KeyValueSeparators);
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

    internal static string RedactTargetUrlForLogging(string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return string.Empty;
        }

        return TrackerUrlSanitizer.Sanitize(targetUrl.Trim());
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
