using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.GeoIp;

public class DynamicGeoIpProxy : IGeoIpService, IGeoIpManager, IDisposable
{
    private readonly IEnumerable<IGeoIpProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    private IGeoIpProvider _activeProvider;
    private bool _disposed;

    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "MaxMind";
    public IGeoIpProvider ActiveProvider => Volatile.Read(ref _activeProvider);

    public DynamicGeoIpProxy(
        IEnumerable<IGeoIpProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? throw new ArgumentNullException(nameof(availableProviders));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = LogManager.GetCurrentClassLogger();

        var desiredId = _configService.GetValue("ActiveGeoIpProvider", "MaxMind");
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("MaxMind", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("OnlineApi", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No GeoIP providers are registered in the application container.");
        }

        _logger.Info("DynamicGeoIpProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IGeoIpProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IGeoIpProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<GeoIpHealthResult> ProbeProviderAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"GeoIP provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in GeoIP provider registry." }
            };
        }

        return await provider.ProbeHealthAsync();
    }

    public async Task<bool> SwitchProviderAsync(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        var targetProvider = GetProvider(providerId);
        if (targetProvider == null)
        {
            _logger.Warn("Cannot switch to GeoIP provider '{0}': not registered.", providerId);
            return false;
        }

        var current = Volatile.Read(ref _activeProvider);
        if (string.Equals(current.ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await _switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                _logger.Warn("Cannot switch to GeoIP provider '{0}': health check failed ({1}).", targetProvider.DisplayName, health.StatusMessage);
                return false;
            }

            var previousProvider = Volatile.Read(ref _activeProvider);
            Volatile.Write(ref _activeProvider, targetProvider);

            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveGeoIpProvider", targetProvider.ProviderId }
            });

            _logger.Info("GeoIP provider hot-swapped: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            _eventAggregator.PublishEvent(new GeoIpProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to hot-swap GeoIP provider to '{0}'", providerId);
            return false;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task<GeoLocationInfo> LookupAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var provider = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await provider.LookupAsync(ipAddress);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Active GeoIP provider '{0}' failed lookup for {1}", provider.ProviderId, ipAddress);
        }

        return new GeoLocationInfo { IpAddress = ipAddress };
    }

    public GeoLocationInfo Lookup(string ipAddress)
    {
        return LookupAsync(ipAddress).GetAwaiter().GetResult();
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
