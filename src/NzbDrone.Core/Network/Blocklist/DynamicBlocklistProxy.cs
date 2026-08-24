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
    private readonly IEnumerable<IBlocklistProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly List<string> _loadedRawRules = new();

    private IBlocklistProvider _activeProvider;
    private bool _disposed;

    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "RadixTree";
    public IBlocklistProvider ActiveProvider => Volatile.Read(ref _activeProvider);
    public int TotalRulesLoaded => Volatile.Read(ref _activeProvider)?.RuleCount ?? 0;

    public DynamicBlocklistProxy(
        IEnumerable<IBlocklistProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? throw new ArgumentNullException(nameof(availableProviders));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = LogManager.GetCurrentClassLogger();

        var desiredId = _configService.GetValue("ActiveBlocklistProvider", "RadixTree");
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("RadixTree", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("P2PDat", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No IP Blocklist providers are registered in the application container.");
        }

        _logger.Info("DynamicBlocklistProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IBlocklistProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IBlocklistProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BlocklistHealthResult> ProbeProviderAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"Blocklist provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in Blocklist provider registry." }
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
            _logger.Warn("Cannot switch to Blocklist provider '{0}': not registered.", providerId);
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
                _logger.Warn("Cannot switch to Blocklist provider '{0}': health check failed ({1}).", targetProvider.DisplayName, health.StatusMessage);
                return false;
            }

            var previousProvider = Volatile.Read(ref _activeProvider);

            // Re-hydrate existing loaded rules into new provider
            var migratedCount = 0;
            if (_loadedRawRules.Count > 0)
            {
                targetProvider.ClearRules();
                migratedCount = await targetProvider.LoadRulesAsync(_loadedRawRules);
            }

            Volatile.Write(ref _activeProvider, targetProvider);

            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveBlocklistProvider", targetProvider.ProviderId }
            });

            _logger.Info("Blocklist provider hot-swapped: {0} -> {1} ({2} rules migrated)", previousProvider.ProviderId, targetProvider.ProviderId, migratedCount);
            _eventAggregator.PublishEvent(new BlocklistProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId, migratedCount));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to hot-swap Blocklist provider to '{0}'", providerId);
            return false;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public bool IsIpBlocked(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        return Volatile.Read(ref _activeProvider).IsIpBlocked(ipAddress);
    }

    public async Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        if (rules == null)
        {
            return 0;
        }

        await _switchLock.WaitAsync();
        try
        {
            var ruleList = rules.ToList();
            _loadedRawRules.Clear();
            _loadedRawRules.AddRange(ruleList);

            return await Volatile.Read(ref _activeProvider).LoadRulesAsync(ruleList);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public void ClearRules()
    {
        lock (_loadedRawRules)
        {
            _loadedRawRules.Clear();
            Volatile.Read(ref _activeProvider).ClearRules();
        }
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
