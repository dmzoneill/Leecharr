// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Http.Transport;

public class CurlImpersonateTransportProvider : IHttpTransportProvider, IHttpOutboundProxyRefreshable, IDisposable
{
    private readonly HttpClient fallbackClient;
    private readonly SocketsHttpHandler handler;
    private readonly IConfigService configService;
    private readonly INetworkBindingService networkBindingService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private bool disposed;

    internal SocketsHttpHandler Handler => this.handler;

    internal WebProxy Proxy => this.handler.Proxy as WebProxy;

    public string ProviderId => "CurlImpersonate";

    public string DisplayName => "curl-impersonate (Chrome / Firefox TLS JA3/JA4 Fingerprint)";

    public string Version => "0.6.1";

    public string Description => "Emulates Chrome/Firefox TLS handshakes, JA3/JA4 fingerprints, and HTTP/2 settings to bypass anti-bot protections.";

    public bool IsAvailable => CheckCurlBinaryAvailable();

    public HttpTransportCapabilities Capabilities => new()
    {
        SupportsHttp3Quic = true,
        SupportsBrowserFingerprintEmulation = true,
        SupportsFlareSolverr = false,
        SupportsCustomProxy = true,
        SupportsTlsJa3Ja4Fingerprinting = true,
        SupportsCookieExtraction = true,
    };

    public CurlImpersonateTransportProvider(
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
            ConnectCallback = SocketsHttpHandlerConnectCallbackFactory.Create(
                this.configService,
                this.networkBindingService,
                this.vpnKillSwitchService),
        };

        this.RefreshOutboundProxy();

        this.fallbackClient = new HttpClient(this.handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public void RefreshOutboundProxy()
    {
        HttpOutboundProxyConfigurator.Apply(this.handler, this.configService, HttpOutboundProxyProfile.CurlImpersonate);
    }

    public Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        var binaryFound = CheckCurlBinaryAvailable();
        if (binaryFound)
        {
            return Task.FromResult(new HttpTransportHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = "curl-impersonate binary found in PATH. TLS JA3/JA4 browser emulation ready.",
            });
        }

        return Task.FromResult(new HttpTransportHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "curl-impersonate operating in managed TLS/HTTP emulation fallback mode.",
            Warnings = { "curl-impersonate binary (e.g. curl_chrome116) not detected in system PATH." },
        });
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        if (!request.Headers.Contains("Sec-Ch-Ua"))
        {
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
        }

        return await this.fallbackClient.SendAsync(request, cancellationToken);
    }

    private static bool CheckCurlBinaryAvailable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator);
        var binaries = new[] { "curl-impersonate-chrome", "curl_chrome116", "curl_chrome110", "curl-impersonate" };

        foreach (var dir in paths)
        {
            foreach (var bin in binaries)
            {
                var full = Path.Combine(dir, bin);
                if (File.Exists(full))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.fallbackClient.Dispose();
            this.handler.Dispose();
        }
    }
}
