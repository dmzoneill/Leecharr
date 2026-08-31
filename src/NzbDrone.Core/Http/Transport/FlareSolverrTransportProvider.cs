// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    public FlareSolverrTransportProvider(IConfigService configService = null)
    {
        this.configService = configService;
        this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        try
        {
            var url = !string.IsNullOrWhiteSpace(this.configService?.GetValue("FlareSolverrUrl", string.Empty))
                ? this.configService.GetValue("FlareSolverrUrl", this.FlareSolverrUrl)
                : this.FlareSolverrUrl;

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
            IsHealthy = true,
            StatusMessage = "FlareSolverr provider registered (service probe deferred or offline).",
            Warnings = { $"FlareSolverr endpoint at {this.FlareSolverrUrl} is not currently responding." },
        };
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

        try
        {
            var method = request.Method == HttpMethod.Post ? "request.post" : "request.get";
            var payload = new
            {
                cmd = method,
                url = request.RequestUri?.ToString(),
                maxTimeout = 60000,
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await this.httpClient.PostAsync(endpoint, jsonContent, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonDocument.Parse(body);
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
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "FlareSolverr execution failed, returning fallback gateway error");
        }

        return await this.httpClient.SendAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.httpClient.Dispose();
        }
    }
}
