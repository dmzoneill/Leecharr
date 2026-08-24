using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Ai;

public class DynamicAiProxy : IAiService, IAiManager, IDisposable
{
    private readonly IEnumerable<IAiEngineProvider> _availableProviders;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly RuleHeuristicAiProvider _defaultFallback = new();

    private IAiEngineProvider _activeProvider;
    private bool _disposed;

    public string ActiveProviderId => Volatile.Read(ref _activeProvider)?.ProviderId ?? "RuleHeuristic";
    public IAiEngineProvider ActiveProvider => Volatile.Read(ref _activeProvider);

    public DynamicAiProxy(
        IEnumerable<IAiEngineProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        _availableProviders = availableProviders ?? throw new ArgumentNullException(nameof(availableProviders));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = LogManager.GetCurrentClassLogger();

        var desiredId = _configService.GetValue("ActiveAiProvider", "RuleHeuristic");
        _activeProvider = _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredId, StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("RuleHeuristic", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault(p => p.ProviderId.Equals("OnnxLocal", StringComparison.OrdinalIgnoreCase))
                          ?? _availableProviders.FirstOrDefault();

        if (_activeProvider == null)
        {
            throw new InvalidOperationException("No AI providers are registered in the application container.");
        }

        _logger.Info("DynamicAiProxy initialized with active provider: {0} ({1})", _activeProvider.DisplayName, _activeProvider.ProviderId);
    }

    public IEnumerable<IAiEngineProvider> GetProviders()
    {
        return _availableProviders;
    }

    public IAiEngineProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AiHealthResult> ProbeProviderAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"AI provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in AI provider registry." }
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
            _logger.Warn("Cannot switch to AI provider '{0}': not registered.", providerId);
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
                _logger.Warn("Cannot switch to AI provider '{0}': health check failed ({1}).", targetProvider.DisplayName, health.StatusMessage);
                return false;
            }

            var previousProvider = Volatile.Read(ref _activeProvider);
            Volatile.Write(ref _activeProvider, targetProvider);

            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveAiProvider", targetProvider.ProviderId }
            });

            _logger.Info("AI provider hot-swapped: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            _eventAggregator.PublishEvent(new AiProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to hot-swap AI provider to '{0}'", providerId);
            return false;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        var provider = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await provider.ParseReleaseAsync(releaseName);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Active AI provider '{0}' failed to parse release, falling back to heuristic engine.", provider.ProviderId);
        }

        return await _defaultFallback.ParseReleaseAsync(releaseName);
    }

    public AiParsedRelease ParseRelease(string releaseName)
    {
        return ParseReleaseAsync(releaseName).GetAwaiter().GetResult();
    }

    public async Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        var provider = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await provider.DiagnoseTorrentHealthAsync(torrent, peers, trackers);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Active AI provider '{0}' failed to diagnose torrent, falling back to heuristic engine.", provider.ProviderId);
        }

        return await _defaultFallback.DiagnoseTorrentHealthAsync(torrent, peers, trackers);
    }

    public AiDiagnosticReport DiagnoseTorrentHealth(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        return DiagnoseTorrentHealthAsync(torrent, peers, trackers).GetAwaiter().GetResult();
    }

    public async Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery)
    {
        var provider = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await provider.ProcessNaturalLanguageSearchAsync(naturalQuery);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Active AI provider '{0}' failed to process natural query, falling back to heuristic engine.", provider.ProviderId);
        }

        return await _defaultFallback.ProcessNaturalLanguageSearchAsync(naturalQuery);
    }

    public AiSearchParameters ProcessNaturalLanguageSearch(string naturalQuery)
    {
        return ProcessNaturalLanguageSearchAsync(naturalQuery).GetAwaiter().GetResult();
    }

    public async Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        var provider = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await provider.AnalyzeMalwareRiskAsync(torrentName, files);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Active AI provider '{0}' failed to analyze malware risk, falling back to heuristic engine.", provider.ProviderId);
        }

        return await _defaultFallback.AnalyzeMalwareRiskAsync(torrentName, files);
    }

    public AiMalwareRiskAssessment AnalyzeMalwareRisk(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        return AnalyzeMalwareRiskAsync(torrentName, files).GetAwaiter().GetResult();
    }

    public async Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null)
    {
        var provider = Volatile.Read(ref _activeProvider);
        try
        {
            var result = await provider.GenerateChatResponseAsync(userMessage, systemContext);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Active AI provider '{0}' failed to generate chat response, falling back to heuristic assistant.", provider.ProviderId);
        }

        return await _defaultFallback.GenerateChatResponseAsync(userMessage, systemContext);
    }

    public string GenerateChatResponse(string userMessage, string systemContext = null)
    {
        return GenerateChatResponseAsync(userMessage, systemContext).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _switchLock.Dispose();

            foreach (var provider in _availableProviders)
            {
                if (provider is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error disposing AI provider '{0}'", provider.ProviderId);
                    }
                }
            }
        }
    }
}
