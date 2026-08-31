// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IEnumerable<IAiEngineProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private readonly RuleHeuristicAiProvider defaultFallback = new();

    private IAiEngineProvider activeProvider;
    private bool disposed;

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "RuleHeuristic";

    public IAiEngineProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public DynamicAiProxy(
        IEnumerable<IAiEngineProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? throw new ArgumentNullException(nameof(availableProviders));
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredId = this.configService.GetValue("ActiveAiProvider", "RuleHeuristic");
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("RuleHeuristic", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("OnnxLocal", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No AI providers are registered in the application container.");
        }

        this.logger.Info("DynamicAiProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IAiEngineProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IAiEngineProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AiHealthResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new AiHealthResult
            {
                IsHealthy = false,
                StatusMessage = $"AI provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in AI provider registry." },
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
            this.logger.Warn("Cannot switch to AI provider '{0}': not registered.", providerId);
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
                this.logger.Warn("Cannot switch to AI provider '{0}': health check failed ({1}).", targetProvider.DisplayName, health.StatusMessage);
                return false;
            }

            var previousProvider = Volatile.Read(ref this.activeProvider);
            Volatile.Write(ref this.activeProvider, targetProvider);

            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveAiProvider", targetProvider.ProviderId },
            });

            this.logger.Info("AI provider hot-swapped: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            this.eventAggregator.PublishEvent(new AiProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to hot-swap AI provider to '{0}'", providerId);
            return false;
        }
        finally
        {
            this.switchLock.Release();
        }
    }

    public async Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        var provider = Volatile.Read(ref this.activeProvider);
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
            this.logger.Debug(ex, "Active AI provider '{0}' failed to parse release, falling back to heuristic engine.", provider.ProviderId);
        }

        return await this.defaultFallback.ParseReleaseAsync(releaseName);
    }

    public AiParsedRelease ParseRelease(string releaseName)
    {
        return this.ParseReleaseAsync(releaseName).GetAwaiter().GetResult();
    }

    public async Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        var provider = Volatile.Read(ref this.activeProvider);
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
            this.logger.Debug(ex, "Active AI provider '{0}' failed to diagnose torrent, falling back to heuristic engine.", provider.ProviderId);
        }

        return await this.defaultFallback.DiagnoseTorrentHealthAsync(torrent, peers, trackers);
    }

    public AiDiagnosticReport DiagnoseTorrentHealth(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        return this.DiagnoseTorrentHealthAsync(torrent, peers, trackers).GetAwaiter().GetResult();
    }

    public async Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery)
    {
        var provider = Volatile.Read(ref this.activeProvider);
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
            this.logger.Debug(ex, "Active AI provider '{0}' failed to process natural query, falling back to heuristic engine.", provider.ProviderId);
        }

        return await this.defaultFallback.ProcessNaturalLanguageSearchAsync(naturalQuery);
    }

    public AiSearchParameters ProcessNaturalLanguageSearch(string naturalQuery)
    {
        return this.ProcessNaturalLanguageSearchAsync(naturalQuery).GetAwaiter().GetResult();
    }

    public async Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        var provider = Volatile.Read(ref this.activeProvider);
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
            this.logger.Debug(ex, "Active AI provider '{0}' failed to analyze malware risk, falling back to heuristic engine.", provider.ProviderId);
        }

        return await this.defaultFallback.AnalyzeMalwareRiskAsync(torrentName, files);
    }

    public AiMalwareRiskAssessment AnalyzeMalwareRisk(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        return this.AnalyzeMalwareRiskAsync(torrentName, files).GetAwaiter().GetResult();
    }

    public async Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null)
    {
        var provider = Volatile.Read(ref this.activeProvider);
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
            this.logger.Debug(ex, "Active AI provider '{0}' failed to generate chat response, falling back to heuristic assistant.", provider.ProviderId);
        }

        return await this.defaultFallback.GenerateChatResponseAsync(userMessage, systemContext);
    }

    public string GenerateChatResponse(string userMessage, string systemContext = null)
    {
        return this.GenerateChatResponseAsync(userMessage, systemContext).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.switchLock.Dispose();

            foreach (var provider in this.availableProviders)
            {
                if (provider is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Error disposing AI provider '{0}'", provider.ProviderId);
                    }
                }
            }
        }
    }
}
