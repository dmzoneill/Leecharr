// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network;

public class VpnKillSwitchTriggeredEvent : IEvent
{
    public string InterfaceName { get; }

    public VpnKillSwitchTriggeredEvent(string interfaceName)
    {
        this.InterfaceName = interfaceName;
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
    private readonly INetworkSettingsRepository repository;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    public NetworkSecurityService(
        INetworkSettingsRepository repository,
        IEventAggregator eventAggregator,
        Configuration.IConfigService configService = null)
    {
        this.repository = repository;
        this.eventAggregator = eventAggregator;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
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
            this.logger.Warn(ex, "Failed to query network interfaces.");
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
        var settings = this.GetCurrentSettings();
        if (!settings.EnableVpnKillSwitch || string.IsNullOrWhiteSpace(settings.BindInterface))
        {
            return false; // Kill switch not engaged
        }

        var isUp = this.IsInterfaceActive(settings.BindInterface);
        if (!isUp)
        {
            this.logger.Error("VPN Kill Switch Triggered! Interface '{0}' dropped. BitTorrent traffic suspended.", settings.BindInterface);
            this.eventAggregator.PublishEvent(new VpnKillSwitchTriggeredEvent(settings.BindInterface));
            return true;
        }

        return false;
    }

    public NetworkSettings GetCurrentSettings()
    {
        var settings = this.repository.GetSettings() ?? new NetworkSettings();
        if (this.configService != null)
        {
            if (!string.IsNullOrWhiteSpace(this.configService.BindInterface))
            {
                settings.BindInterface = this.configService.BindInterface;
            }

            if (this.configService.EnableVpnKillSwitch)
            {
                settings.EnableVpnKillSwitch = this.configService.EnableVpnKillSwitch;
            }
        }

        return settings;
    }

    public void SaveSettings(NetworkSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        if (settings.Id == 0)
        {
            this.repository.Insert(settings);
        }
        else
        {
            this.repository.Update(settings);
        }
    }
}
