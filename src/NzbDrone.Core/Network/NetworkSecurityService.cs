// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
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

public class NetworkSecurityService : INetworkSecurityService, IExecute<VpnKillSwitchCheckCommand>, IExecuteAsync<VpnKillSwitchCheckCommand>
{
    private readonly INetworkSettingsRepository repository;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Vpn.IVpnKillSwitchService vpnKillSwitchService;
    private readonly Logger logger;
    private bool isKillSwitchActive;

    public NetworkSecurityService(
        INetworkSettingsRepository repository,
        IEventAggregator eventAggregator,
        Configuration.IConfigService configService = null,
        Vpn.IVpnKillSwitchService vpnKillSwitchService = null)
    {
        this.repository = repository;
        this.eventAggregator = eventAggregator;
        this.configService = configService;
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task ExecuteAsync(VpnKillSwitchCheckCommand message, CancellationToken cancellationToken = default)
    {
        this.CheckVpnKillSwitch();
        return Task.CompletedTask;
    }

    public void Execute(VpnKillSwitchCheckCommand message)
    {
        this.CheckVpnKillSwitch();
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
        if (this.vpnKillSwitchService != null)
        {
            return this.vpnKillSwitchService.CheckVpnState();
        }

        var settings = this.GetCurrentSettings();
        var iface = settings.BindInterface?.Trim();
        if (!settings.EnableVpnKillSwitch || string.IsNullOrWhiteSpace(iface) ||
            string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (this.isKillSwitchActive)
            {
                this.isKillSwitchActive = false;
                this.logger.Info("VPN Kill switch disabled or unconfigured. Restoring interface state.");
                this.eventAggregator?.PublishEvent(new Vpn.VpnInterfaceRestoredEvent(iface ?? string.Empty));
            }

            return false; // Kill switch not engaged
        }

        var isUp = this.IsInterfaceActive(iface);
        if (!isUp)
        {
            if (!this.isKillSwitchActive)
            {
                this.isKillSwitchActive = true;
                this.logger.Error("VPN Kill Switch Triggered! Interface '{0}' dropped. BitTorrent traffic suspended.", iface);
                this.eventAggregator?.PublishEvent(new VpnKillSwitchTriggeredEvent(iface));
            }

            return true;
        }
        else
        {
            if (this.isKillSwitchActive)
            {
                this.isKillSwitchActive = false;
                this.logger.Info("VPN interface '{0}' restored and operational. Resuming BitTorrent traffic.", iface);
                this.eventAggregator?.PublishEvent(new Vpn.VpnInterfaceRestoredEvent(iface));
            }

            return false;
        }
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

            if (!string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding))
            {
                settings.BindInterface = this.configService.NetworkInterfaceBinding;
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

        if (this.configService != null)
        {
            var dict = new Dictionary<string, object>
            {
                { "EnableVpnKillSwitch", settings.EnableVpnKillSwitch },
            };

            if (!string.IsNullOrWhiteSpace(settings.BindInterface))
            {
                dict["BindInterface"] = settings.BindInterface;
            }

            this.configService.SaveConfigDictionary(dict);
        }

        this.vpnKillSwitchService?.CheckVpnState();
    }
}
