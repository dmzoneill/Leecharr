// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Http.Transport;

public class FlareSolverrTransportProvider : IHttpTransportProvider, IHttpOutboundProxyRefreshable, IDisposable
{
    private readonly IConfigService configService;
    private readonly INetworkBindingService networkBindingService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private readonly HttpClient solverHttpClient;
    private readonly HttpClient fallbackHttpClient;
    private readonly SocketsHttpHandler solverHandler;
    private readonly SocketsHttpHandler fallbackHandler;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<string, string> sessionPool = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> sessionCreationLocks = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public string ProviderId => "FlareSolverr";

    public string DisplayName => "FlareSolverr (Cloudflare / DDoS-GUARD Challenge Solver)";

    public string Version => "3.3.0";

    public string Description => "Routes HTTP requests through a FlareSolverr headless browser instance to bypass Cloudflare Turnstile and DDoS-GUARD.";

    public bool IsAvailable => true;

    public HttpTransportCapabilities Capabilities => new()
    {
        SupportsHttp3Quic = false,
        SupportsBrowserFingerprintEmulation = true,
        SupportsFlareSolverr = true,
        SupportsCustomProxy = true,
        SupportsTlsJa3Ja4Fingerprinting = true,
        SupportsCookieExtraction = true,
    };

    public string FlareSolverrUrl { get; set; } = "http://localhost:8191/v1";

    public int ActiveSessionCount => this.sessionPool.Count;

    public IReadOnlyDictionary<string, string> ActiveSessions => this.sessionPool;

    internal SocketsHttpHandler SolverHandler => this.solverHandler;

    internal SocketsHttpHandler FallbackHandler => this.fallbackHandler;

    public FlareSolverrTransportProvider(
        IConfigService configService = null,
        HttpClient httpClient = null,
        INetworkBindingService networkBindingService = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        this.configService = configService;
        this.networkBindingService = networkBindingService;
        this.vpnKillSwitchService = vpnKillSwitchService;

        if (httpClient != null)
        {
            this.solverHttpClient = httpClient;
            this.fallbackHttpClient = httpClient;
        }
        else
        {
            this.solverHandler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                UseProxy = false,
            };

            this.solverHttpClient = new HttpClient(this.solverHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(60),
            };

            this.fallbackHandler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectCallback = SocketsHttpHandlerConnectCallbackFactory.Create(
                    this.configService,
                    this.networkBindingService,
                    this.vpnKillSwitchService,
                    this.logger),
            };

            this.RefreshOutboundProxy();

            this.fallbackHttpClient = new HttpClient(this.fallbackHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(60),
            };
        }
    }

    public void RefreshOutboundProxy()
    {
        if (this.fallbackHandler == null)
        {
            return;
        }

        HttpOutboundProxyConfigurator.Apply(this.fallbackHandler, this.configService, HttpOutboundProxyProfile.FlareSolverrFallback);
    }

    public async Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        var url = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
            ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
            : this.FlareSolverrUrl;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var resp = await this.solverHttpClient.GetAsync(url, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                return new HttpTransportHealthCheckResult
                {
                    IsHealthy = true,
                    StatusMessage = $"FlareSolverr service is responding at {url}.",
                };
            }
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "FlareSolverr endpoint uncontactable at {0}", url);
        }

        return new HttpTransportHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = $"FlareSolverr endpoint at {url} is offline or unreachable.",
            Warnings = { $"FlareSolverr endpoint at {url} is not currently responding." },
        };
    }

    public async Task<string> GetOrCreateSessionAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "default";
        }

        if (this.sessionPool.TryGetValue(domain, out var existingSession))
        {
            return existingSession;
        }

        var creationLock = this.sessionCreationLocks.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
        await creationLock.WaitAsync(cancellationToken);
        try
        {
            if (this.sessionPool.TryGetValue(domain, out existingSession))
            {
                return existingSession;
            }

            var endpoint = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
                ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
                : this.FlareSolverrUrl;

            try
            {
                var sanitizedDomain = Regex.Replace(domain, @"[^a-zA-Z0-9_-]", "_", RegexOptions.None, TimeSpan.FromSeconds(2));
                var sessionId = $"leecharr_{sanitizedDomain}_{Guid.NewGuid():N}";
                if (sessionId.Length > 32)
                {
                    sessionId = sessionId.Substring(0, 32);
                }

                var payload = new Dictionary<string, object>
                {
                    ["cmd"] = "sessions.create",
                    ["session"] = sessionId,
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var response = await this.solverHttpClient.PostAsync(endpoint, jsonContent, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("status", out var st) && st.GetString() == "ok")
                    {
                        var createdSession = root.TryGetProperty("session", out var sessProp)
                            ? sessProp.GetString()
                            : sessionId;

                        this.sessionPool[domain] = createdSession;
                        this.logger.Debug("Created FlareSolverr session '{0}' for domain '{1}'", createdSession, domain);
                        return createdSession;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to create dedicated FlareSolverr session for domain {0}", domain);
            }

            return null;
        }
        finally
        {
            creationLock.Release();
        }
    }

    public async Task<bool> DestroySessionAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "default";
        }

        if (!this.sessionPool.TryRemove(domain, out var sessionId))
        {
            return false;
        }

        var endpoint = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
            ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
            : this.FlareSolverrUrl;

        try
        {
            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "sessions.destroy",
                ["session"] = sessionId,
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await this.solverHttpClient.PostAsync(endpoint, jsonContent, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to destroy FlareSolverr session '{0}' for domain '{1}'", sessionId, domain);
            return false;
        }
    }

    public async Task ClearSessionsAsync(CancellationToken cancellationToken = default)
    {
        var domains = this.sessionPool.Keys.ToList();
        foreach (var domain in domains)
        {
            await this.DestroySessionAsync(domain, cancellationToken);
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetFlareSolverrCommand(request.Method, out _))
        {
            return await this.fallbackHttpClient.SendAsync(request, cancellationToken);
        }

        var endpoint = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
            ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
            : this.FlareSolverrUrl;

        var domain = request.RequestUri?.Host?.ToLowerInvariant() ?? "default";

        try
        {
            var session = await this.GetOrCreateSessionAsync(domain, cancellationToken);
            return await this.ExecuteFlareSolverrRequestAsync(endpoint, request, domain, session, isRetry: false, cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "FlareSolverr execution failed, falling back to direct HTTP request");
            if (CanSafelyFallbackToDirectHttp(request.Method))
            {
                return await this.fallbackHttpClient.SendAsync(request, cancellationToken);
            }

            throw;
        }
    }

    private async Task<HttpResponseMessage> ExecuteFlareSolverrRequestAsync(
        string endpoint,
        HttpRequestMessage request,
        string domain,
        string session,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        if (!TryGetFlareSolverrCommand(request.Method, out var method))
        {
            return await this.fallbackHttpClient.SendAsync(request, cancellationToken);
        }

        var payloadDict = new Dictionary<string, object>
        {
            ["cmd"] = method,
            ["url"] = request.RequestUri?.ToString(),
            ["maxTimeout"] = 60000,
        };

        if (!string.IsNullOrEmpty(session))
        {
            payloadDict["session"] = session;
        }

        if (request.Method == HttpMethod.Post && request.Content != null)
        {
            var postData = await request.Content.ReadAsStringAsync(cancellationToken);
            payloadDict["postData"] = postData;
        }

        var jsonContent = new StringContent(JsonSerializer.Serialize(payloadDict), Encoding.UTF8, "application/json");
        using var response = await this.solverHttpClient.PostAsync(endpoint, jsonContent, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var st) && st.GetString() == "ok")
            {
                var solution = root.GetProperty("solution");
                return BuildHttpResponseFromSolution(solution, request);
            }

            // If session expired / invalid on FlareSolverr side, clear session and retry once
            var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : string.Empty;
            if (!isRetry && !string.IsNullOrEmpty(session) && (message.Contains("session", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            {
                this.sessionPool.TryRemove(domain, out _);
                var newSession = await this.GetOrCreateSessionAsync(domain, cancellationToken);
                return await this.ExecuteFlareSolverrRequestAsync(endpoint, request, domain, newSession, isRetry: true, cancellationToken);
            }
        }

        if (CanSafelyFallbackToDirectHttp(request.Method))
        {
            return await this.fallbackHttpClient.SendAsync(request, cancellationToken);
        }

        return CreateFlareSolverrFailureResponse(request);
    }

    private static bool TryGetFlareSolverrCommand(HttpMethod method, out string command)
    {
        if (method == HttpMethod.Get)
        {
            command = "request.get";
            return true;
        }

        if (method == HttpMethod.Post)
        {
            command = "request.post";
            return true;
        }

        command = null;
        return false;
    }

    private static bool CanSafelyFallbackToDirectHttp(HttpMethod method) => method == HttpMethod.Get;

    private static HttpResponseMessage CreateFlareSolverrFailureResponse(HttpRequestMessage request)
    {
        return new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            RequestMessage = request,
            Content = new StringContent("FlareSolverr request failed"),
        };
    }

    private static HttpResponseMessage BuildHttpResponseFromSolution(JsonElement solution, HttpRequestMessage request)
    {
        var status = solution.GetProperty("status").GetInt32();
        var httpResponse = new HttpResponseMessage((HttpStatusCode)status)
        {
            RequestMessage = request,
        };

        var bodyBytes = ExtractSolutionResponseBytes(solution);
        string contentType = null;

        if (solution.TryGetProperty("headers", out var headersElement))
        {
            ApplySolutionHeaders(httpResponse, headersElement, ref contentType);
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = InferContentType(bodyBytes, request);
        }

        var content = new ByteArrayContent(bodyBytes);
        if (!string.IsNullOrWhiteSpace(contentType) &&
            MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            content.Headers.ContentType = mediaType;
        }

        httpResponse.Content = content;
        return httpResponse;
    }

    private static byte[] ExtractSolutionResponseBytes(JsonElement solution)
    {
        if (!solution.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind != JsonValueKind.String)
        {
            return Array.Empty<byte>();
        }

        var responseText = responseElement.GetString() ?? string.Empty;
        if (solution.TryGetProperty("responseEncoding", out var encodingElement) &&
            string.Equals(encodingElement.GetString(), "base64", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(responseText) ? Array.Empty<byte>() : Convert.FromBase64String(responseText);
        }

        return Encoding.UTF8.GetBytes(responseText);
    }

    private static void ApplySolutionHeaders(HttpResponseMessage message, JsonElement headersElement, ref string contentType)
    {
        if (headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headersElement.EnumerateObject())
            {
                ApplySolutionHeader(message, header.Name, header.Value.GetString() ?? string.Empty, ref contentType);
            }

            return;
        }

        if (headersElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var header in headersElement.EnumerateArray())
        {
            if (!header.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            var value = header.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : string.Empty;
            ApplySolutionHeader(message, name, value ?? string.Empty, ref contentType);
        }
    }

    private static void ApplySolutionHeader(HttpResponseMessage message, string name, string value, ref string contentType)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
        {
            contentType = value;
            return;
        }

        if (name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!message.Headers.TryAddWithoutValidation(name, value))
        {
            message.Content?.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static string InferContentType(byte[] bodyBytes, HttpRequestMessage request)
    {
        if (bodyBytes.Length >= 4 &&
            bodyBytes[0] == (byte)'d' &&
            bodyBytes[1] == (byte)'8' &&
            bodyBytes[2] == (byte)':' &&
            (bodyBytes[3] == (byte)'a' || bodyBytes[3] == (byte)'A'))
        {
            return "application/x-bittorrent";
        }

        var prefixLength = Math.Min(bodyBytes.Length, 512);
        var prefix = prefixLength == 0 ? string.Empty : Encoding.UTF8.GetString(bodyBytes, 0, prefixLength).TrimStart();

        if (prefix.StartsWith("<?xml", StringComparison.Ordinal) ||
            prefix.StartsWith("<rss", StringComparison.OrdinalIgnoreCase) ||
            prefix.StartsWith("<feed", StringComparison.OrdinalIgnoreCase))
        {
            return "application/rss+xml; charset=utf-8";
        }

        if (prefix.StartsWith("{", StringComparison.Ordinal) || prefix.StartsWith("[", StringComparison.Ordinal))
        {
            return "application/json; charset=utf-8";
        }

        if (prefix.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return "text/html; charset=utf-8";
        }

        var requestPath = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (requestPath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            return "application/x-bittorrent";
        }

        return "application/octet-stream";
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            try
            {
                this.ClearSessionsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to destroy FlareSolverr sessions during dispose");
            }

            this.solverHttpClient.Dispose();

            if (!ReferenceEquals(this.solverHttpClient, this.fallbackHttpClient))
            {
                this.fallbackHttpClient.Dispose();
            }
        }
    }
}
