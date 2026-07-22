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
    public string ExternalIp { get; set; } = string.Empty;
    public int ListenPort { get; set; } = 7889;
    public bool PortOpen { get; set; } = true;
    public string ActiveInterface { get; set; } = "Auto";
    public bool VpnKillSwitchActive { get; set; }
    public List<string> LocalAddresses { get; set; } = new();
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
    private readonly Logger _logger;

    public NetworkStatusService(IExternalIpService externalIpService, IConfigFileProvider configFileProvider)
    {
        _externalIpService = externalIpService;
        _configFileProvider = configFileProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var externalIp = _externalIpService.CachedIp;
        if (string.IsNullOrEmpty(externalIp))
        {
            _ = _externalIpService.GetExternalIpAsync();
        }

        return new NetworkStatus
        {
            ExternalIp = externalIp,
            ListenPort = _configFileProvider?.Port ?? 7889,
            PortOpen = true,
            ActiveInterface = "Auto",
            LocalAddresses = GetLocalAddresses()
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
