// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network;

public class NetworkStatus
{
    public string LocalIp { get; set; } = "127.0.0.1";

    public string ExternalIp { get; set; } = string.Empty;

    public int ListenPort { get; set; } = 7889;

    public bool PortOpen { get; set; } = true;

    public string ActiveInterface { get; set; } = "Auto";

    public bool UpnpAvailable { get; set; } = true;

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
    private readonly Logger logger;

    public NetworkStatusService(
        IExternalIpService externalIpService,
        IConfigFileProvider configFileProvider,
        IConfigService configService = null)
    {
        this.externalIpService = externalIpService;
        this.configFileProvider = configFileProvider;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var externalIp = this.externalIpService.CachedIp;
        if (string.IsNullOrEmpty(externalIp))
        {
            _ = this.externalIpService.GetExternalIpAsync();
        }

        var localAddresses = this.GetLocalAddresses();
        var primaryLocal = localAddresses.FirstOrDefault() ?? "127.0.0.1";
        var port = this.configFileProvider?.Port ?? 7889;
        var btPort = this.configService?.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        var activeInterface = !string.IsNullOrWhiteSpace(this.configService?.BindInterface) ? this.configService.BindInterface : "Auto";

        return new NetworkStatus
        {
            LocalIp = primaryLocal,
            ExternalIp = externalIp,
            ListenPort = port,
            PortOpen = true,
            ActiveInterface = activeInterface,
            UpnpAvailable = this.configService?.UpnpEnabled ?? true,
            ProxyEnabled = this.configService?.ProxyType != null && this.configService.ProxyType != "none",
            LocalAddresses = localAddresses,
            PortMappings = new List<PortMappingInfo>
            {
                new()
                {
                    InternalPort = port,
                    ExternalPort = port,
                    Protocol = "TCP",
                    Description = "Leecharr Web UI & API",
                    IsActive = true
                },
                new()
                {
                    InternalPort = btPort,
                    ExternalPort = btPort,
                    Protocol = "TCP/UDP",
                    Description = "BitTorrent Peer Swarm & DHT",
                    IsActive = true
                }
            },
        };
    }

    public List<string> GetLocalAddresses()
    {
        var addresses = new List<string>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(ip.Address.ToString());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to enumerate network interfaces");
        }

        return addresses.Distinct().ToList();
    }
}
