using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class DynamicMediaMetadataProxy : IMediaMetadataService, IMediaMetadataManager, IDisposable
{
    private readonly IEnumerable<IMediaMetadataProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private IMediaMetadataProvider _activeProvider;
    private bool _disposed;

    public IMediaMetadataProvider ActiveProvider => Volatile.Read(ref _activeProvider);
    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "ServarrSync";

    public DynamicMediaMetadataProxy(
        IEnumerable<IMediaMetadataProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? Enumerable.Empty<IMediaMetadataProvider>();
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = _configService?.ActiveMediaMetadataProvider;
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("ServarrSync", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No media metadata providers are registered in the system container.");
        }

        _logger.Info("DynamicMediaMetadataProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IMediaMetadataProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IMediaMetadataProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MediaMetadataHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new MediaMetadataHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Media metadata provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." }
            };
        }

        return await provider.ProbeHealthAsync();
    }

    public async Task<MediaMetadataSwitchResult> SwitchProviderAsync(string targetProviderId)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new MediaMetadataSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty."
            };
        }

        var targetProvider = GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new MediaMetadataSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered."
            };
        }

        if (string.Equals(Volatile.Read(ref _activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new MediaMetadataSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref _activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Media metadata provider '{targetProvider.DisplayName}' is already active."
            };
        }

        await _switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new MediaMetadataSwitchResult
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
                { "ActiveMediaMetadataProvider", targetProvider.ProviderId }
            });

            _logger.Info("Media metadata provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            _eventAggregator?.PublishEvent(new MediaMetadataProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            return new MediaMetadataSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched media metadata provider to {targetProvider.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error switching media metadata provider to {0}", targetProviderId);
            return new MediaMetadataSwitchResult
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

    public Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null)
    {
        return Volatile.Read(ref _activeProvider).FetchMetadataAsync(title, category, year);
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
