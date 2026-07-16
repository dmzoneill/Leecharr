using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using NLog;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network;

public class VpnKillSwitchTriggeredEvent : IEvent
{
    public string InterfaceName { get; }

    public VpnKillSwitchTriggeredEvent(string interfaceName)
    {
        InterfaceName = interfaceName;
    }
}

public interface INetworkSecurityService
{
    IEnumerable<string> GetAvailableNetworkInterfaces();
    bool IsInterfaceActive(string interfaceName);
    bool CheckVpnKillSwitch();
    NetworkSettings GetCurrentSettings();
    void SaveSettings(NetworkSettings settings);
}

public class NetworkSecurityService : INetworkSecurityService
{
    private readonly INetworkSettingsRepository _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public NetworkSecurityService(
        INetworkSettingsRepository repository,
        IEventAggregator eventAggregator)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<string> GetAvailableNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .Select(nic => nic.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to query network interfaces.");
            return Enumerable.Empty<string>();
        }
    }

    public bool IsInterfaceActive(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return true; // No interface binding active
        }

        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase));

            return nic != null && nic.OperationalStatus == OperationalStatus.Up;
        }
        catch
        {
            return false;
        }
    }

    public bool CheckVpnKillSwitch()
    {
        var settings = GetCurrentSettings();
        if (!settings.EnableVpnKillSwitch || string.IsNullOrWhiteSpace(settings.BindInterface))
        {
            return false; // Kill switch not engaged
        }

        var isUp = IsInterfaceActive(settings.BindInterface);
        if (!isUp)
        {
            _logger.Error("VPN Kill Switch Triggered! Interface '{0}' dropped. BitTorrent traffic suspended.", settings.BindInterface);
            _eventAggregator.PublishEvent(new VpnKillSwitchTriggeredEvent(settings.BindInterface));
            return true;
        }

        return false;
    }

    public NetworkSettings GetCurrentSettings()
    {
        return _repository.GetSettings();
    }

    public void SaveSettings(NetworkSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        if (settings.Id == 0)
        {
            _repository.Insert(settings);
        }
        else
        {
            _repository.Update(settings);
        }
    }
}
