// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IEnumerable<IGeoIpProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);

    private IGeoIpProvider activeProvider;
    private bool disposed;

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "MaxMind";

    public IGeoIpProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public DynamicGeoIpProxy(
        IEnumerable<IGeoIpProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? throw new ArgumentNullException(nameof(availableProviders));
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredId = this.configService.GetValue("ActiveGeoIpProvider", "MaxMind");
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("MaxMind", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("OnlineApi", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No GeoIP providers are registered in the application container.");
        }

        this.logger.Info("DynamicGeoIpProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IGeoIpProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IGeoIpProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<GeoIpHealthResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new GeoIpHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"GeoIP provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in GeoIP provider registry." },
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

        var targetProvider = this.GetProvider(providerId);
        if (targetProvider == null)
        {
            this.logger.Warn("Cannot switch to GeoIP provider '{0}': not registered.", providerId);
            return false;
        }

        var current = Volatile.Read(ref this.activeProvider);
        if (string.Equals(current.ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await this.switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                this.logger.Warn("Cannot switch to GeoIP provider '{0}': health check failed ({1}).", targetProvider.DisplayName, health.StatusMessage);
                return false;
            }

            var previousProvider = Volatile.Read(ref this.activeProvider);
            Volatile.Write(ref this.activeProvider, targetProvider);

            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveGeoIpProvider", targetProvider.ProviderId },
            });

            this.logger.Info("GeoIP provider hot-swapped: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            this.eventAggregator.PublishEvent(new GeoIpProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to hot-swap GeoIP provider to '{0}'", providerId);
            return false;
        }
        finally
        {
            this.switchLock.Release();
        }
    }

    public async Task<GeoLocationInfo> LookupAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var provider = Volatile.Read(ref this.activeProvider);
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
            this.logger.Debug(ex, "Active GeoIP provider '{0}' failed lookup for {1}", provider.ProviderId, ipAddress);
        }

        return new GeoLocationInfo { IpAddress = ipAddress };
    }

    public GeoLocationInfo Lookup(string ipAddress)
    {
        return this.LookupAsync(ipAddress).GetAwaiter().GetResult();
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
