using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaInspection;

public class DynamicMediaInspectorProxy : IMediaContainerInspector, IMediaInspectorManager, IDisposable
{
    private readonly IEnumerable<IMediaInspectorProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private IMediaInspectorProvider _activeProvider;
    private bool _disposed;

    public IMediaInspectorProvider ActiveProvider => Volatile.Read(ref _activeProvider);
    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "TagLib";

    public DynamicMediaInspectorProxy(
        IEnumerable<IMediaInspectorProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? Array.Empty<IMediaInspectorProvider>();
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = _configService.ActiveMediaInspector;
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("TagLib", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No media inspector providers are registered in the system container.");
        }

        _logger.Info("DynamicMediaInspectorProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IMediaInspectorProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IMediaInspectorProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MediaInspectorHealthCheckResult> ProbeProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new MediaInspectorHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Media inspector provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in media inspector registry." }
            };
        }

        return await provider.ProbeHealthAsync(cancellationToken);
    }

    public async Task<MediaInspectorSwitchResult> SwitchProviderAsync(string targetProviderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new MediaInspectorSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty."
            };
        }

        var targetProvider = GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new MediaInspectorSwitchResult
            {
                Success = false,
                Error = $"Target media inspector provider '{targetProviderId}' is not registered."
            };
        }

        var current = Volatile.Read(ref _activeProvider);
        if (string.Equals(current.ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new MediaInspectorSwitchResult
            {
                Success = true,
                PreviousProvider = current.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Media inspector provider '{targetProvider.DisplayName}' is already active."
            };
        }

        await _switchLock.WaitAsync(cancellationToken);
        try
        {
            var health = await targetProvider.ProbeHealthAsync(cancellationToken);
            if (!health.IsHealthy)
            {
                return new MediaInspectorSwitchResult
                {
                    Success = false,
                    PreviousProvider = Volatile.Read(ref _activeProvider).ProviderId,
                    ActiveProvider = Volatile.Read(ref _activeProvider).ProviderId,
                    Error = $"Cannot switch to media inspector provider '{targetProvider.DisplayName}': health check failed ({health.StatusMessage})."
                };
            }

            var previousProvider = Volatile.Read(ref _activeProvider);
            _logger.Info("Switching media inspector: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            Volatile.Write(ref _activeProvider, targetProvider);

            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveMediaInspector", targetProvider.ProviderId }
            });

            _eventAggregator.PublishEvent(new MediaInspectorSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            _logger.Info("Media inspector hot-swap completed: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            return new MediaInspectorSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched media inspector to {targetProvider.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fatal error during media inspector hot-swap to {0}", targetProviderId);
            return new MediaInspectorSwitchResult
            {
                Success = false,
                PreviousProvider = Volatile.Read(ref _activeProvider)?.ProviderId,
                ActiveProvider = Volatile.Read(ref _activeProvider)?.ProviderId,
                Error = $"Media inspector switch failed: {ex.Message}"
            };
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        var active = Volatile.Read(ref _activeProvider);
        try
        {
            var result = active.InspectFile(filePath);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Active inspector '{0}' failed for '{1}'", active.ProviderId, filePath);
        }

        if (!active.ProviderId.Equals("TagLib", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = GetProvider("TagLib");
            if (fallback != null)
            {
                return fallback.InspectFile(filePath);
            }
        }

        return null;
    }

    public MediaContainerInfo Inspect(Stream stream, string fileName = "")
    {
        var active = Volatile.Read(ref _activeProvider);
        return active.Inspect(stream, fileName);
    }

    public async Task<MediaContainerInfo> InspectMediaAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        var active = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await active.InspectMediaAsync(mediaPath, cancellationToken);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Active inspector '{0}' failed for '{1}'", active.ProviderId, mediaPath);
        }

        if (!active.ProviderId.Equals("TagLib", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = GetProvider("TagLib");
            if (fallback != null)
            {
                return await fallback.InspectMediaAsync(mediaPath, cancellationToken);
            }
        }

        return null;
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
