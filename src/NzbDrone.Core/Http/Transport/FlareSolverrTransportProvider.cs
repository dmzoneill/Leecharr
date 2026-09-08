// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Http.Transport;

public class FlareSolverrTransportProvider : IHttpTransportProvider, IDisposable
{
    private readonly IConfigService configService;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<string, string> sessionPool = new(StringComparer.OrdinalIgnoreCase);
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

    public FlareSolverrTransportProvider(IConfigService configService = null, HttpClient httpClient = null)
    {
        this.configService = configService;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        var url = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
            ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
            : this.FlareSolverrUrl;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var resp = await this.httpClient.GetAsync(url, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                return new HttpTransportHealthCheckResult
                {
                    IsHealthy = true,
                    StatusMessage = $"FlareSolverr service is responding at {url}.",
                };
            }
        }
        catch
        {
            // Endpoint uncontactable
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

        var endpoint = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
            ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
            : this.FlareSolverrUrl;

        try
        {
            var sanitizedDomain = Regex.Replace(domain, @"[^a-zA-Z0-9_-]", "_");
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
            var response = await this.httpClient.PostAsync(endpoint, jsonContent, cancellationToken);

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
            var response = await this.httpClient.PostAsync(endpoint, jsonContent, cancellationToken);
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
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
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
        }

        return await this.httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> ExecuteFlareSolverrRequestAsync(
        string endpoint,
        HttpRequestMessage request,
        string domain,
        string session,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        var method = request.Method == HttpMethod.Post ? "request.post" : "request.get";
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
        var response = await this.httpClient.PostAsync(endpoint, jsonContent, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var st) && st.GetString() == "ok")
            {
                var solution = root.GetProperty("solution");
                var status = solution.GetProperty("status").GetInt32();
                var responseBody = solution.GetProperty("response").GetString() ?? string.Empty;

                var httpResponse = new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "text/html"),
                    RequestMessage = request,
                };

                return httpResponse;
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

        return await this.httpClient.SendAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.sessionPool.Clear();
            this.httpClient.Dispose();
        }
    }
}
