// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Http.Transport;

public class SocketsHttpHandlerProvider : IHttpTransportProvider, IHttpOutboundProxyRefreshable, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly SocketsHttpHandler handler;
    private readonly IConfigService configService;
    private readonly INetworkBindingService networkBindingService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private bool disposed;

    internal SocketsHttpHandler Handler => this.handler;

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

    public SocketsHttpHandlerProvider(
        IConfigService configService = null,
        INetworkBindingService networkBindingService = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        this.configService = configService;
        this.networkBindingService = networkBindingService;
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 50,
            UseProxy = false,
            ConnectCallback = SocketsHttpHandlerConnectCallbackFactory.Create(
                this.configService,
                this.networkBindingService,
                this.vpnKillSwitchService,
                this.logger),
        };

        this.RefreshOutboundProxy();

        this.httpClient = new HttpClient(this.handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public void RefreshOutboundProxy()
    {
        HttpOutboundProxyConfigurator.Apply(this.handler, this.configService, HttpOutboundProxyProfile.SocketsHttpHandler, this.logger);
    }

    public Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        if (this.vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return Task.FromResult(new HttpTransportHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "VPN kill switch is active. Outbound HTTP transport is fail-closed until the VPN interface is restored.",
            });
        }

        var proxyType = this.configService?.ProxyType?.ToLowerInvariant() ?? "none";
        var result = new HttpTransportHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "SocketsHttpHandler pipeline is healthy (HTTP/1.1, HTTP/2, HTTP/3 QUIC enabled).",
        };

        if (proxyType is "socks4" or "socks4a")
        {
            result.Warnings.Add("SOCKS4 proxy is configured but is not supported by SocketsHttpHandler. SOCKS4 is only supported for raw TCP socket connections.");
        }

        return Task.FromResult(result);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await this.httpClient.SendAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.httpClient.Dispose();
            this.handler.Dispose();
        }
    }
}
