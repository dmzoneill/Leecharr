using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Http.Transport;

public class DynamicHttpTransportProxy : IHttpTransportEngine, IHttpTransportManager, IDisposable
{
    private readonly IEnumerable<IHttpTransportProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private IHttpTransportProvider _activeProvider;
    private bool _disposed;

    public IHttpTransportProvider ActiveProvider => Volatile.Read(ref _activeProvider);
    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "SocketsHttpHandler";

    public DynamicHttpTransportProxy(
        IEnumerable<IHttpTransportProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? Enumerable.Empty<IHttpTransportProvider>();
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = _configService?.ActiveHttpTransportProvider;
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("SocketsHttpHandler", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No HTTP transport providers are registered in the system container.");
        }

        _logger.Info("DynamicHttpTransportProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IHttpTransportProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IHttpTransportProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<HttpTransportHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new HttpTransportHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"HTTP transport provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." }
            };
        }

        return await provider.ProbeHealthAsync();
    }

    public async Task<HttpTransportSwitchResult> SwitchProviderAsync(string targetProviderId)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new HttpTransportSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty."
            };
        }

        var targetProvider = GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new HttpTransportSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered."
            };
        }

        if (string.Equals(Volatile.Read(ref _activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new HttpTransportSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref _activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"HTTP transport provider '{targetProvider.DisplayName}' is already active."
            };
        }

        await _switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new HttpTransportSwitchResult
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
                { "ActiveHttpTransportProvider", targetProvider.ProviderId }
            });

            _logger.Info("HTTP transport provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            _eventAggregator?.PublishEvent(new HttpTransportProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            return new HttpTransportSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched HTTP transport provider to {targetProvider.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error switching HTTP transport provider to {0}", targetProviderId);
            return new HttpTransportSwitchResult
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

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        return Volatile.Read(ref _activeProvider).SendAsync(request, cancellationToken);
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
