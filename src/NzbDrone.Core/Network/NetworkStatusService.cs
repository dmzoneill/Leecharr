// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network.PortMapping;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Network;

public class NetworkStatus
{
    public string LocalIp { get; set; } = "127.0.0.1";

    public string ExternalIp { get; set; } = string.Empty;

    public int ListenPort { get; set; } = 7889;

    public bool PortOpen { get; set; }

    public string ActiveInterface { get; set; } = "Auto";

    public bool UpnpAvailable { get; set; }

    public bool ProxyEnabled { get; set; }

    public bool VpnKillSwitchActive { get; set; }

    public List<string> LocalAddresses { get; set; } = new();

    public List<PortMappingInfo> PortMappings { get; set; } = new();
}

public class PortMappingInfo
{
    public int InternalPort { get; set; }

    public int ExternalPort { get; set; }

    public string Protocol { get; set; } = "TCP";

    public string Description { get; set; } = "Leecharr BitTorrent";

    public bool IsActive { get; set; } = true;
}

public interface INetworkStatusService
{
    NetworkStatus GetStatus();

    List<string> GetLocalAddresses();
}

public class NetworkStatusService : INetworkStatusService
{
    private readonly IExternalIpService externalIpService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IConfigService configService;
    private readonly INatPmpPortMapperService natPmpPortMapperService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger;

    public NetworkStatusService(
        IExternalIpService externalIpService,
        IConfigFileProvider configFileProvider,
        IConfigService configService = null,
        INatPmpPortMapperService natPmpPortMapperService = null,
        IVpnKillSwitchService vpnKillSwitchService = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.externalIpService = externalIpService;
        this.configFileProvider = configFileProvider;
        this.configService = configService;
        this.natPmpPortMapperService = natPmpPortMapperService;
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.safeHttpClientService = safeHttpClientService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var externalIp = this.externalIpService.GetExternalIpAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var localAddresses = this.GetLocalAddresses();
        var primaryLocal = localAddresses.FirstOrDefault() ?? "127.0.0.1";
        var port = this.configFileProvider?.Port ?? 7889;
        var btPort = this.configService?.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        var boundInterface = !string.IsNullOrWhiteSpace(this.configService?.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService?.BindInterface;
        var activeInterface = !string.IsNullOrWhiteSpace(boundInterface) ? boundInterface : "Auto";

        var portMappings = new List<PortMappingInfo>();
        if (this.natPmpPortMapperService?.ActiveMappings != null && this.natPmpPortMapperService.ActiveMappings.Count > 0)
        {
            foreach (var mapping in this.natPmpPortMapperService.ActiveMappings)
            {
                var protocolStr = mapping.Protocol switch
                {
                    NatPmpProtocol.Tcp => "TCP",
                    NatPmpProtocol.Udp => "UDP",
                    _ => mapping.Protocol.ToString().ToUpperInvariant(),
                };

                var description = mapping.InternalPort == port
                    ? "Leecharr Web UI & API"
                    : (mapping.InternalPort == btPort ? "BitTorrent Peer Swarm & DHT" : $"NAT-PMP Port Mapping ({protocolStr})");

                portMappings.Add(new PortMappingInfo
                {
                    InternalPort = mapping.InternalPort,
                    ExternalPort = mapping.ExternalPort,
                    Protocol = protocolStr,
                    Description = description,
                    IsActive = true,
                });
            }
        }

        var boundInterfaceForGateway = !string.IsNullOrWhiteSpace(boundInterface) && !string.Equals(boundInterface, "Auto", StringComparison.OrdinalIgnoreCase)
            ? boundInterface
            : null;
        var natGateway = NatPmpPortMapperService.DiscoverDefaultGateway(boundInterfaceForGateway);
        var upnpAvailable = portMappings.Count > 0 || natGateway != null;
        var portOpen = this.IsBitTorrentPortOpen(btPort);

        return new NetworkStatus
        {
            LocalIp = primaryLocal,
            ExternalIp = externalIp,
            ListenPort = port,
            PortOpen = portOpen,
            ActiveInterface = activeInterface,
            UpnpAvailable = upnpAvailable,
            ProxyEnabled = this.configService?.ProxyType != null && this.configService.ProxyType != "none",
            VpnKillSwitchActive = this.vpnKillSwitchService?.IsFailClosedActive ?? false,
            LocalAddresses = localAddresses,
            PortMappings = portMappings,
        };
    }

    private bool IsBitTorrentPortOpen(int btPort)
    {
        if (this.vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return false;
        }

        if (btPort < 1 || btPort > 65535)
        {
            return false;
        }

        var httpClient = this.safeHttpClientService ?? new SafeHttpClientService();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = httpClient.DownloadStringDirectAsync(
                $"https://portcheck.transmissionbt.com/{btPort}",
                TimeSpan.FromSeconds(5),
                cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            return !string.IsNullOrWhiteSpace(response) && (response.Trim() == "1" || response.Contains('1'));
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "BitTorrent port check failed for port {0}", btPort);
            return false;
        }
    }

    public List<string> GetLocalAddresses()
    {
        var addresses = new List<string>();

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to enumerate network interfaces");
            return addresses;
        }

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            try
            {
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(ip.Address.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to read IP properties for network interface {0}", ni.Name);
            }
        }

        return addresses.Distinct().ToList();
    }
}
