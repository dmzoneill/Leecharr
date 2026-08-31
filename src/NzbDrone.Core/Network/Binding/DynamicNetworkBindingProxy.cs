// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Binding;

public class DynamicNetworkBindingProxy : INetworkBindingService, INetworkBindingManager, IDisposable
{
    private readonly IEnumerable<INetworkBindingProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private INetworkBindingProvider activeProvider;
    private bool disposed;

    public INetworkBindingProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "ManagedSocket";

    public DynamicNetworkBindingProxy(
        IEnumerable<INetworkBindingProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? Enumerable.Empty<INetworkBindingProvider>();
        this.configService = configService;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = this.configService?.ActiveNetworkBindingProvider;
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("ManagedSocket", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No network binding providers are registered in the system container.");
        }

        this.logger.Info("DynamicNetworkBindingProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<INetworkBindingProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public INetworkBindingProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<NetworkBindingHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Network binding provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." },
            };
        }

        return await provider.ProbeHealthAsync();
    }

    public async Task<NetworkBindingSwitchResult> SwitchProviderAsync(string targetProviderId)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new NetworkBindingSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty.",
            };
        }

        var targetProvider = this.GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new NetworkBindingSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered.",
            };
        }

        if (string.Equals(Volatile.Read(ref this.activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new NetworkBindingSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Network binding provider '{targetProvider.DisplayName}' is already active.",
            };
        }

        await this.switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new NetworkBindingSwitchResult
                {
                    Success = false,
                    PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                    ActiveProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                    Error = $"Cannot switch to provider '{targetProvider.DisplayName}': health check failed ({health.StatusMessage}).",
                };
            }

            var previousProvider = Volatile.Read(ref this.activeProvider);
            Volatile.Write(ref this.activeProvider, targetProvider);

            this.configService?.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveNetworkBindingProvider", targetProvider.ProviderId },
            });

            this.logger.Info("Network binding provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            this.eventAggregator?.PublishEvent(new NetworkBindingProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            return new NetworkBindingSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched network binding provider to {targetProvider.DisplayName}.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error switching network binding provider to {0}", targetProviderId);
            return new NetworkBindingSwitchResult
            {
                Success = false,
                PreviousProvider = Volatile.Read(ref this.activeProvider)?.ProviderId,
                ActiveProvider = Volatile.Read(ref this.activeProvider)?.ProviderId,
                Error = $"Hot-swap failed: {ex.Message}",
            };
        }
        finally
        {
            this.switchLock.Release();
        }
    }

    public void BindSocket(Socket socket, string interfaceName)
    {
        Volatile.Read(ref this.activeProvider).BindSocket(socket, interfaceName);
    }

    public bool IsInterfaceUp(string interfaceName)
    {
        return Volatile.Read(ref this.activeProvider).IsInterfaceUp(interfaceName);
    }

    public bool CheckVpnKillSwitch(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return false;
        }

        var isUp = this.IsInterfaceUp(interfaceName);
        if (!isUp)
        {
            this.logger.Error("VPN Kill Switch triggered! Interface '{0}' dropped.", interfaceName);
            this.eventAggregator?.PublishEvent(new VpnKillSwitchTriggeredEvent(interfaceName));
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.switchLock.Dispose();
        }
    }
}
