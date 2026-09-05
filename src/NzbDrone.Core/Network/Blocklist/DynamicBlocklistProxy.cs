// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Blocklist;

public class DynamicBlocklistProxy : IBlocklistService, IBlocklistManager, IDisposable
{
    private readonly IEnumerable<IBlocklistProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private readonly List<string> loadedRawRules = new();

    private IBlocklistProvider activeProvider;
    private bool disposed;

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "RadixTree";

    public IBlocklistProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public int TotalRulesLoaded => Volatile.Read(ref this.activeProvider)?.RuleCount ?? 0;

    public DynamicBlocklistProxy(
        IEnumerable<IBlocklistProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? throw new ArgumentNullException(nameof(availableProviders));
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredId = this.configService.GetValue("ActiveBlocklistProvider", "RadixTree");
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("RadixTree", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("P2PDat", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No IP Blocklist providers are registered in the application container.");
        }

        this.logger.Info("DynamicBlocklistProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IBlocklistProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IBlocklistProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BlocklistHealthResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Blocklist provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in Blocklist provider registry." },
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
            this.logger.Warn("Cannot switch to Blocklist provider '{0}': not registered.", providerId);
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
                this.logger.Warn("Cannot switch to Blocklist provider '{0}': health check failed ({1}).", targetProvider.DisplayName, health.StatusMessage);
                return false;
            }

            var previousProvider = Volatile.Read(ref this.activeProvider);

            // Re-hydrate existing loaded rules into new provider
            var migratedCount = 0;
            if (this.loadedRawRules.Count > 0)
            {
                targetProvider.ClearRules();
                migratedCount = await targetProvider.LoadRulesAsync(this.loadedRawRules);
            }

            Volatile.Write(ref this.activeProvider, targetProvider);

            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveBlocklistProvider", targetProvider.ProviderId },
            });

            this.logger.Info("Blocklist provider hot-swapped: {0} -> {1} ({2} rules migrated)", previousProvider.ProviderId, targetProvider.ProviderId, migratedCount);
            this.eventAggregator.PublishEvent(new BlocklistProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId, migratedCount));
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to hot-swap Blocklist provider to '{0}'", providerId);
            return false;
        }
        finally
        {
            this.switchLock.Release();
        }
    }

    public bool IsIpBlocked(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        return Volatile.Read(ref this.activeProvider).IsIpBlocked(ipAddress);
    }

    public async Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        if (rules == null)
        {
            return 0;
        }

        await this.switchLock.WaitAsync();
        try
        {
            var ruleList = rules.ToList();
            this.loadedRawRules.Clear();
            this.loadedRawRules.AddRange(ruleList);

            return await Volatile.Read(ref this.activeProvider).LoadRulesAsync(ruleList);
        }
        finally
        {
            this.switchLock.Release();
        }
    }

    public void ClearRules()
    {
        this.switchLock.Wait();
        try
        {
            this.loadedRawRules.Clear();
            Volatile.Read(ref this.activeProvider).ClearRules();
        }
        finally
        {
            this.switchLock.Release();
        }
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
