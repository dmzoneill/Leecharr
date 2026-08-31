// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IEnumerable<IHttpTransportProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private IHttpTransportProvider activeProvider;
    private bool disposed;

    public IHttpTransportProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "SocketsHttpHandler";

    public DynamicHttpTransportProxy(
        IEnumerable<IHttpTransportProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? Enumerable.Empty<IHttpTransportProvider>();
        this.configService = configService;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = this.configService?.ActiveHttpTransportProvider;
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("SocketsHttpHandler", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No HTTP transport providers are registered in the system container.");
        }

        this.logger.Info("DynamicHttpTransportProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IHttpTransportProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IHttpTransportProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<HttpTransportHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new HttpTransportHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"HTTP transport provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." },
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
                Error = "Target provider ID must not be empty.",
            };
        }

        var targetProvider = this.GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new HttpTransportSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered.",
            };
        }

        if (string.Equals(Volatile.Read(ref this.activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new HttpTransportSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"HTTP transport provider '{targetProvider.DisplayName}' is already active.",
            };
        }

        await this.switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new HttpTransportSwitchResult
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
                { "ActiveHttpTransportProvider", targetProvider.ProviderId },
            });

            this.logger.Info("HTTP transport provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            this.eventAggregator?.PublishEvent(new HttpTransportProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            return new HttpTransportSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched HTTP transport provider to {targetProvider.DisplayName}.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error switching HTTP transport provider to {0}", targetProviderId);
            return new HttpTransportSwitchResult
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

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        return Volatile.Read(ref this.activeProvider).SendAsync(request, cancellationToken);
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
