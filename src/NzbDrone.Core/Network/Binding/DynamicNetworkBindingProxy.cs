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
    private readonly IEnumerable<INetworkBindingProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private INetworkBindingProvider _activeProvider;
    private bool _disposed;

    public INetworkBindingProvider ActiveProvider => Volatile.Read(ref _activeProvider);
    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "ManagedSocket";

    public DynamicNetworkBindingProxy(
        IEnumerable<INetworkBindingProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? Enumerable.Empty<INetworkBindingProvider>();
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = _configService?.ActiveNetworkBindingProvider;
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("ManagedSocket", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No network binding providers are registered in the system container.");
        }

        _logger.Info("DynamicNetworkBindingProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<INetworkBindingProvider> GetProviders()
    {
        return _availableProviders;
    }

    public INetworkBindingProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<NetworkBindingHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new NetworkBindingHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Network binding provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." }
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
                Error = "Target provider ID must not be empty."
            };
        }

        var targetProvider = GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new NetworkBindingSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered."
            };
        }

        if (string.Equals(Volatile.Read(ref _activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new NetworkBindingSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref _activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Network binding provider '{targetProvider.DisplayName}' is already active."
            };
        }

        await _switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new NetworkBindingSwitchResult
                {
                    Success = false,
                    PreviousProvider = Volatile.Read(ref _activeProvider).ProviderId,
                    ActiveProvider = Volatile.Read(ref _activeProvider).ProviderId,
                    Error = $"Cannot switch to provider '{targetProvider.DisplayName}': health check failed ({health.StatusMessage})."
                };
            }

            var previousProvider = Volatile.Read(ref _activeProvider);
            Volatile.Write(ref _activeProvider, targetProvider);

            _configService?.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveNetworkBindingProvider", targetProvider.ProviderId }
            });

            _logger.Info("Network binding provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            _eventAggregator?.PublishEvent(new NetworkBindingProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            return new NetworkBindingSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched network binding provider to {targetProvider.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error switching network binding provider to {0}", targetProviderId);
            return new NetworkBindingSwitchResult
            {
                Success = false,
                PreviousProvider = Volatile.Read(ref _activeProvider)?.ProviderId,
                ActiveProvider = Volatile.Read(ref _activeProvider)?.ProviderId,
                Error = $"Hot-swap failed: {ex.Message}"
            };
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public void BindSocket(Socket socket, string interfaceName)
    {
        Volatile.Read(ref _activeProvider).BindSocket(socket, interfaceName);
    }

    public bool IsInterfaceUp(string interfaceName)
    {
        return Volatile.Read(ref _activeProvider).IsInterfaceUp(interfaceName);
    }

    public bool CheckVpnKillSwitch(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return false;
        }

        var isUp = IsInterfaceUp(interfaceName);
        if (!isUp)
        {
            _logger.Error("VPN Kill Switch triggered! Interface '{0}' dropped.", interfaceName);
            _eventAggregator?.PublishEvent(new VpnKillSwitchTriggeredEvent(interfaceName));
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _switchLock.Dispose();
        }
    }
}
