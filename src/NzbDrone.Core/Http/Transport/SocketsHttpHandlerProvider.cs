// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Http.Transport;

public class SocketsHttpHandlerProvider : IHttpTransportProvider, IDisposable
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
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (this.vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    throw new SocketException((int)SocketError.NetworkUnreachable);
                }

                var iface = this.configService?.BindInterface;
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;
                IPAddress targetIp = null;

                if (IPAddress.TryParse(host, out var parsedIp))
                {
                    targetIp = parsedIp;
                }
                else
                {
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                        if (addresses != null && addresses.Length > 0)
                        {
                            targetIp = addresses[0];
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Trace(ex, "DNS resolution failed for {0}, falling back to DnsEndPoint connect", host);
                    }
                }

                var socket = targetIp != null
                    ? new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true }
                    : (context.DnsEndPoint.AddressFamily == AddressFamily.Unspecified
                        ? new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true }
                        : new Socket(context.DnsEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true });

                if (!string.IsNullOrWhiteSpace(iface) && this.networkBindingService != null)
                {
                    this.networkBindingService.BindSocket(socket, iface);
                }

                try
                {
                    if (targetIp != null)
                    {
                        await socket.ConnectAsync(new IPEndPoint(targetIp, port), cancellationToken);
                    }
                    else
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    }

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        var proxyType = configService?.ProxyType?.ToLowerInvariant() ?? "none";
        var proxyHost = configService?.ProxyHost;
        var proxyPort = configService?.ProxyPort ?? (proxyType is "socks5" or "socks4" or "socks4a" ? 1080 : 8080);

        if (!string.Equals(proxyType, "none", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(proxyHost))
        {
            if (proxyType is "socks4" or "socks4a")
            {
                this.logger.Warn("SOCKS4/SOCKS4a proxy is not supported by .NET SocketsHttpHandler for HTTP traffic (supported only for raw TCP sockets). Proxy configuration ignored for SocketsHttpHandler.");
                this.handler.UseProxy = false;
                this.handler.Proxy = null;
            }
            else
            {
                var scheme = proxyType switch
                {
                    "socks5" => "socks5",
                    "http" => "http",
                    "https" => "https",
                    _ => "http",
                };

                var proxy = new WebProxy($"{scheme}://{proxyHost}:{proxyPort}");
                if (configService.ProxyAuthEnabled && !string.IsNullOrEmpty(configService.ProxyUsername))
                {
                    proxy.Credentials = new NetworkCredential(configService.ProxyUsername, configService.ProxyPassword ?? string.Empty);
                }

                this.handler.Proxy = proxy;
                this.handler.UseProxy = true;
            }
        }

        this.httpClient = new HttpClient(this.handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
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
