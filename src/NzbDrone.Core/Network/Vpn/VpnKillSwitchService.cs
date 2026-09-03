// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Vpn;

public class VpnKillSwitchService : IVpnKillSwitchService
{
    private readonly INetworkSettingsRepository repository;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly object stateLock = new();

    private Timer heartbeatTimer;
    private bool isFailClosedActive;
    private bool lastKnownInterfaceUp = true;
    private bool disposed;

    public event Action<string> VpnDropped;

    public event Action<string> VpnRestored;

    // Delegate for testability and mocking interface status
    internal Func<string, bool> InterfaceStatusCheck { get; set; }

    internal Func<string, AddressFamily, IPAddress> InterfaceIpResolver { get; set; }

    public VpnKillSwitchService(
        INetworkSettingsRepository repository,
        IEventAggregator eventAggregator,
        IConfigService configService = null)
    {
        this.repository = repository;
        this.eventAggregator = eventAggregator;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();

        this.InterfaceStatusCheck = this.CheckInterfaceStatusDefault;
        this.InterfaceIpResolver = this.ResolveInterfaceIpDefault;

        // 1. Subscribe to OS network change events for instantaneous drop and restoration detection
        try
        {
            NetworkChange.NetworkAddressChanged += this.OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += this.OnNetworkAvailabilityChanged;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to subscribe to OS NetworkChange events. Falling back to heartbeat timer.");
        }

        // 2. Heartbeat safety timer
        this.heartbeatTimer = new Timer(_ => this.CheckVpnState(), null, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1500));

        // 3. Initial check
        this.CheckVpnState();
    }

    public bool IsKillSwitchEnabled
    {
        get
        {
            var settings = this.GetNetworkSettings();
            return settings.EnableVpnKillSwitch;
        }
    }

    public string VpnInterfaceName
    {
        get
        {
            var settings = this.GetNetworkSettings();
            return settings.BindInterface?.Trim() ?? string.Empty;
        }
    }

    public bool IsVpnInterfaceUp
    {
        get
        {
            var iface = this.VpnInterfaceName;
            return string.IsNullOrWhiteSpace(iface) || this.InterfaceStatusCheck(iface);
        }
    }

    public bool IsFailClosedActive
    {
        get
        {
            lock (this.stateLock)
            {
                return this.isFailClosedActive;
            }
        }
    }

    public bool CheckVpnState()
    {
        if (this.disposed)
        {
            return false;
        }

        lock (this.stateLock)
        {
            var settings = this.GetNetworkSettings();
            var enabled = settings.EnableVpnKillSwitch;
            var iface = settings.BindInterface?.Trim();

            // If kill switch is not enabled or interface is wildcard, disengage fail-closed
            if (!enabled || string.IsNullOrWhiteSpace(iface) ||
                string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (this.isFailClosedActive)
                {
                    this.isFailClosedActive = false;
                    this.logger.Info("VPN Kill switch disabled or binding unconfigured. Disengaging fail-closed state.");
                }

                this.lastKnownInterfaceUp = true;
                return false;
            }

            var isUp = this.InterfaceStatusCheck(iface);

            if (!isUp)
            {
                // Transition: Interface Dropped
                if (!this.isFailClosedActive || this.lastKnownInterfaceUp)
                {
                    this.isFailClosedActive = true;
                    this.lastKnownInterfaceUp = false;
                    this.logger.Error("VPN Kill Switch Triggered! Interface '{0}' dropped or unavailable. Halting all BitTorrent network traffic (fail-closed).", iface);

                    try
                    {
                        this.VpnDropped?.Invoke(iface);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error in VpnDropped subscriber callback");
                    }

                    this.eventAggregator?.PublishEvent(new VpnKillSwitchTriggeredEvent(iface));
                }

                return true; // Fail-closed engaged
            }
            else
            {
                // Transition: Interface Restored
                if (this.isFailClosedActive || !this.lastKnownInterfaceUp)
                {
                    this.isFailClosedActive = false;
                    this.lastKnownInterfaceUp = true;
                    this.logger.Info("VPN interface '{0}' verified online and operational. Disengaging fail-closed state.", iface);

                    try
                    {
                        this.VpnRestored?.Invoke(iface);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error in VpnRestored subscriber callback");
                    }

                    this.eventAggregator?.PublishEvent(new VpnInterfaceRestoredEvent(iface));
                }

                return false;
            }
        }
    }

    public IPAddress GetVpnInterfaceIpAddress(AddressFamily family = AddressFamily.InterNetwork)
    {
        var iface = this.VpnInterfaceName;
        if (string.IsNullOrWhiteSpace(iface))
        {
            return null;
        }

        if (this.IsFailClosedActive)
        {
            return null;
        }

        return this.InterfaceIpResolver(iface, family);
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;

            try
            {
                NetworkChange.NetworkAddressChanged -= this.OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged -= this.OnNetworkAvailabilityChanged;
            }
            catch
            {
            }

            this.heartbeatTimer?.Dispose();
            this.heartbeatTimer = null;
        }
    }

    private void OnNetworkChanged(object sender, EventArgs e)
    {
        this.logger.Debug("OS NetworkAddressChanged event detected. Validating VPN kill switch state immediately.");
        this.CheckVpnState();
    }

    private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
    {
        this.logger.Debug("OS NetworkAvailabilityChanged event detected (IsAvailable={0}). Validating VPN kill switch state immediately.", e.IsAvailable);
        this.CheckVpnState();
    }

    private NetworkSettings GetNetworkSettings()
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

    private bool CheckInterfaceStatusDefault(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return true;
        }

        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != OperationalStatus.Up)
            {
                return false;
            }

            var unicast = nic.GetIPProperties()?.UnicastAddresses;
            return unicast != null && unicast.Any(a =>
                !IPAddress.IsLoopback(a.Address) &&
                !a.Address.Equals(IPAddress.Any) &&
                !a.Address.Equals(IPAddress.None));
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to query network interface status for '{0}'", interfaceName);
            return false;
        }
    }

    private IPAddress ResolveInterfaceIpDefault(string interfaceName, AddressFamily family)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != OperationalStatus.Up)
            {
                return null;
            }

            var addr = nic.GetIPProperties()?.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == family &&
                                     !IPAddress.IsLoopback(a.Address) &&
                                     !a.Address.Equals(IPAddress.Any) &&
                                     !a.Address.Equals(IPAddress.None));

            return addr?.Address;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to resolve IP for interface '{0}'", interfaceName);
            return null;
        }
    }
}
