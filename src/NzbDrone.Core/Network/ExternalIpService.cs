// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Network.Binding;

namespace NzbDrone.Core.Network;

public interface IExternalIpService
{
    string CachedIp { get; }

    Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default);
}

public class ExternalIpService : BackgroundService, IExternalIpService
{
    private const string PrimaryEndpointTemplate = "https://www.leecharr.net/ip/?uuid={0}";
    private const string PrimaryHttpEndpointTemplate = "http://www.leecharr.net/ip/?uuid={0}";

    private static readonly TimeSpan FallbackInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private static readonly string[] FallbackSources =
    {
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
        "https://checkip.amazonaws.com",
    };

    private readonly HttpClient client;
    private readonly IConfigService configService;
    private readonly INetworkBindingService networkBindingService;
    private readonly IHttpTransportEngine transportEngine;
    private readonly Logger logger;
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private string cachedIp = string.Empty;
    private DateTime lastFetch = DateTime.MinValue;
    private volatile bool networkChanged;

    public string CachedIp => this.cachedIp;

    public ExternalIpService(
        IConfigService configService,
        INetworkBindingService networkBindingService = null,
        IHttpTransportEngine transportEngine = null,
        HttpClient httpClient = null)
    {
        this.configService = configService;
        this.networkBindingService = networkBindingService;
        this.transportEngine = transportEngine;
        this.client = httpClient;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public ExternalIpService(IConfigService configService, HttpClient httpClient)
        : this(configService, null, null, httpClient)
    {
    }

    public ExternalIpService(HttpClient httpClient)
        : this(null, null, null, httpClient)
    {
    }

    public ExternalIpService()
        : this(null, null, null, null)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAddressChanged += this.OnNetworkChanged;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await this.FetchExternalIpAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                if (this.networkChanged)
                {
                    this.networkChanged = false;
                    this.logger.Info("Network change detected, refreshing external IP");
                    await this.RefreshIp(stoppingToken);
                }
                else if (DateTime.UtcNow - this.lastFetch > FallbackInterval)
                {
                    await this.RefreshIp(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= this.OnNetworkChanged;
        }
    }

    private void OnNetworkChanged(object sender, EventArgs e)
    {
        this.networkChanged = true;
    }

    private async Task RefreshIp(CancellationToken cancellationToken)
    {
        try
        {
            var oldIp = this.cachedIp;
            var newIp = await this.FetchExternalIpAsync(cancellationToken);

            if (!string.IsNullOrEmpty(newIp) && newIp != oldIp)
            {
                this.logger.Info("External IP changed: {0} -> {1}", oldIp, newIp);
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "External IP refresh failed");
        }
    }

    public async Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(this.cachedIp) && DateTime.UtcNow - this.lastFetch < CacheDuration)
        {
            return this.cachedIp;
        }

        return await this.FetchExternalIpAsync(cancellationToken);
    }

    private async Task<string> FetchExternalIpAsync(CancellationToken cancellationToken)
    {
        if (!await this.fetchLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return this.cachedIp;
        }

        var httpClient = this.client ?? this.CreateHttpClient();
        var ownsClient = this.client == null;

        try
        {
            var uuid = this.configService?.InstanceUuid;
            if (string.IsNullOrWhiteSpace(uuid))
            {
                uuid = Guid.NewGuid().ToString().ToLowerInvariant();
            }

            var sources = new List<string>
            {
                string.Format(PrimaryEndpointTemplate, Uri.EscapeDataString(uuid)),
                string.Format(PrimaryHttpEndpointTemplate, Uri.EscapeDataString(uuid)),
            };
            sources.AddRange(FallbackSources);

            foreach (var source in sources)
            {
                try
                {
                    var response = await httpClient.GetStringAsync(source, cancellationToken).ConfigureAwait(false);

                    if (TryExtractIpFromResponse(response, out var ip))
                    {
                        this.cachedIp = ip;
                        this.lastFetch = DateTime.UtcNow;
                        this.logger.Debug("External IP from {0}: {1}", source, ip);
                        return ip;
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to get external IP from {0}", source);
                }
            }

            return this.cachedIp;
        }
        finally
        {
            if (ownsClient)
            {
                httpClient.Dispose();
            }

            this.fetchLock.Release();
        }
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.All,
        };

        var proxyType = this.configService?.ProxyType?.ToLowerInvariant();
        var proxyHost = this.configService?.ProxyHost;
        var proxyPort = this.configService?.ProxyPort > 0 ? this.configService.ProxyPort : (proxyType == "socks5" ? 1080 : 8080);

        if ((proxyType == "socks5" || proxyType == "http") && !string.IsNullOrWhiteSpace(proxyHost))
        {
            var proxyUri = new Uri($"{proxyType}://{proxyHost}:{proxyPort}");
            ICredentials credentials = null;
            if (!string.IsNullOrWhiteSpace(this.configService?.ProxyUsername))
            {
                credentials = new NetworkCredential(this.configService.ProxyUsername, this.configService?.ProxyPassword ?? string.Empty);
            }

            handler.Proxy = new WebProxy(proxyUri)
            {
                Credentials = credentials,
            };
            handler.UseProxy = true;
        }

        var bindInterface = this.configService?.BindInterface;
        if (string.IsNullOrWhiteSpace(bindInterface) ||
            string.Equals(bindInterface, "any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bindInterface, "all", StringComparison.OrdinalIgnoreCase))
        {
            bindInterface = this.configService?.NetworkInterfaceBinding;
        }

        if (!string.IsNullOrWhiteSpace(bindInterface) &&
            !string.Equals(bindInterface, "any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(bindInterface, "all", StringComparison.OrdinalIgnoreCase))
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                IPAddress[] addresses;
                if (IPAddress.TryParse(host, out var parsedIp))
                {
                    addresses = new[] { parsedIp };
                }
                else
                {
                    addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                }

                if (addresses == null || addresses.Length == 0)
                {
                    throw new SocketException((int)SocketError.HostNotFound);
                }

                var targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
                var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };

                try
                {
                    if (this.networkBindingService != null)
                    {
                        this.networkBindingService.BindSocket(socket, bindInterface);
                    }
                    else
                    {
                        var localIp = Binding.ManagedSocketBindingProvider.GetInterfaceIp(bindInterface, targetIp.AddressFamily);
                        if (localIp != null)
                        {
                            socket.Bind(new IPEndPoint(localIp, 0));
                        }
                    }

                    await socket.ConnectAsync(new IPEndPoint(targetIp, port), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public static bool TryExtractIpFromResponse(string responseText, out string ip)
    {
        ip = string.Empty;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var trimmed = responseText.Trim();

        // 1. Try parsing JSON format (e.g. from leecharr.net/ip/?uuid=...)
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
                {
                    if (dataProp.TryGetProperty("ip", out var dataIpProp) && dataIpProp.ValueKind == JsonValueKind.String)
                    {
                        var extractedIp = dataIpProp.GetString();
                        if (IPAddress.TryParse(extractedIp, out _))
                        {
                            ip = extractedIp;
                            return true;
                        }
                    }
                }

                if (root.TryGetProperty("ip", out var ipProp) && ipProp.ValueKind == JsonValueKind.String)
                {
                    var extractedIp = ipProp.GetString();
                    if (IPAddress.TryParse(extractedIp, out _))
                    {
                        ip = extractedIp;
                        return true;
                    }
                }

                if (root.TryGetProperty("ip_address", out var ipAddrProp) && ipAddrProp.ValueKind == JsonValueKind.String)
                {
                    var extractedIp = ipAddrProp.GetString();
                    if (IPAddress.TryParse(extractedIp, out _))
                    {
                        ip = extractedIp;
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON, proceed to plain text
        }

        // 2. Try parsing plain-text IP
        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            ip = parsed.ToString();
            return true;
        }

        return false;
    }
}
