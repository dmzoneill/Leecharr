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
    private readonly IExternalIpService _externalIpService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public NetworkStatusService(
        IExternalIpService externalIpService,
        IConfigFileProvider configFileProvider,
        IConfigService configService = null)
    {
        _externalIpService = externalIpService;
        _configFileProvider = configFileProvider;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var externalIp = _externalIpService.CachedIp;
        if (string.IsNullOrEmpty(externalIp))
        {
            _ = _externalIpService.GetExternalIpAsync();
        }

        var localAddresses = GetLocalAddresses();
        var primaryLocal = localAddresses.FirstOrDefault() ?? "127.0.0.1";
        var port = _configFileProvider?.Port ?? 7889;

        return new NetworkStatus
        {
            LocalIp = primaryLocal,
            ExternalIp = externalIp,
            ListenPort = port,
            PortOpen = true,
            ActiveInterface = "Auto",
            UpnpAvailable = _configService?.UpnpEnabled ?? true,
            ProxyEnabled = _configService?.ProxyType != null && _configService.ProxyType != "none",
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
                }
            }
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
            _logger.Debug(ex, "Failed to enumerate network interfaces");
        }

        return addresses.Distinct().ToList();
    }
}
