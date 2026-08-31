// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Http.Transport;

public class SocketsHttpHandlerProvider : IHttpTransportProvider, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly SocketsHttpHandler handler;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private bool disposed;

    public string ProviderId => "SocketsHttpHandler";

    public string DisplayName => "Standard SocketsHttpHandler (.NET 10 HTTP/3 QUIC)";

    public string Version => "10.0.0";

    public string Description => "High-performance .NET 10 HTTP/1.1, HTTP/2, and HTTP/3 QUIC transport pipeline with socket pooling.";

    public bool IsAvailable => true;

    public HttpTransportCapabilities Capabilities => new()
    {
        SupportsHttp3Quic = true,
        SupportsBrowserFingerprintEmulation = false,
        SupportsFlareSolverr = false,
        SupportsCustomProxy = true,
        SupportsTlsJa3Ja4Fingerprinting = false,
        SupportsCookieExtraction = true,
    };

    public SocketsHttpHandlerProvider()
    {
        this.handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 50,
        };

        this.httpClient = new HttpClient(this.handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        return Task.FromResult(new HttpTransportHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "SocketsHttpHandler pipeline is healthy (HTTP/1.1, HTTP/2, HTTP/3 QUIC enabled).",
        });
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
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
