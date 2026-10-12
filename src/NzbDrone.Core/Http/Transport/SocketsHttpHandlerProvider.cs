// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
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
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (this.vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    throw new SocketException((int)SocketError.NetworkUnreachable);
                }

                var iface = this.configService?.BindInterface;
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;
                IPAddress[] resolvedAddresses = null;

                if (IPAddress.TryParse(host, out var parsedIp))
                {
                    resolvedAddresses = [parsedIp];
                }
                else
                {
                    try
                    {
                        resolvedAddresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Trace(ex, "DNS resolution failed for {0}, falling back to DnsEndPoint connect", host);
                    }
                }

                if (resolvedAddresses != null && resolvedAddresses.Length > 0)
                {
                    Exception lastError = null;
                    foreach (var address in resolvedAddresses)
                    {
                        var targetIp = address;
                        if (targetIp.IsIPv4MappedToIPv6)
                        {
                            targetIp = targetIp.MapToIPv4();
                        }

                        var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        if (!string.IsNullOrWhiteSpace(iface) && this.networkBindingService != null)
                        {
                            this.networkBindingService.BindSocket(socket, iface);
                        }

                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(targetIp, port), cancellationToken);
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

                var fallbackSocket = context.DnsEndPoint.AddressFamily == AddressFamily.Unspecified
                    ? new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true }
                    : new Socket(context.DnsEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                if (!string.IsNullOrWhiteSpace(iface) && this.networkBindingService != null)
                {
                    this.networkBindingService.BindSocket(fallbackSocket, iface);
                }

                try
                {
                    await fallbackSocket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(fallbackSocket, ownsSocket: true);
                }
                catch
                {
                    fallbackSocket.Dispose();
                    throw;
                }
            },
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
