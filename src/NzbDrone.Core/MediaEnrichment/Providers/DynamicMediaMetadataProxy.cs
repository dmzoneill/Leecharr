// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IEnumerable<IMediaMetadataProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private IMediaMetadataProvider activeProvider;
    private bool disposed;

    public IMediaMetadataProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "ServarrSync";

    public DynamicMediaMetadataProxy(
        IEnumerable<IMediaMetadataProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? Enumerable.Empty<IMediaMetadataProvider>();
        this.configService = configService;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = this.configService?.ActiveMediaMetadataProvider;
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("ServarrSync", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No media metadata providers are registered in the system container.");
        }

        this.logger.Info("DynamicMediaMetadataProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IMediaMetadataProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IMediaMetadataProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MediaMetadataHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new MediaMetadataHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Media metadata provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." },
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
                Error = "Target provider ID must not be empty.",
            };
        }

        var targetProvider = this.GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new MediaMetadataSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered.",
            };
        }

        if (string.Equals(Volatile.Read(ref this.activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new MediaMetadataSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Media metadata provider '{targetProvider.DisplayName}' is already active.",
            };
        }

        await this.switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new MediaMetadataSwitchResult
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
                { "ActiveMediaMetadataProvider", targetProvider.ProviderId },
            });

            this.logger.Info("Media metadata provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            this.eventAggregator?.PublishEvent(new MediaMetadataProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            return new MediaMetadataSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched media metadata provider to {targetProvider.DisplayName}.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error switching media metadata provider to {0}", targetProviderId);
            return new MediaMetadataSwitchResult
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

    public async Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null)
    {
        var provider = Volatile.Read(ref this.activeProvider);
        if (provider != null)
        {
            var result = await provider.FetchMetadataAsync(title, category, year);
            if (result != null && !string.IsNullOrEmpty(result.PosterUrl))
            {
                return result;
            }

            foreach (var fallback in this.availableProviders.Where(p => p != provider))
            {
                try
                {
                    var fallbackResult = await fallback.FetchMetadataAsync(title, category, year);
                    if (fallbackResult != null && !string.IsNullOrEmpty(fallbackResult.PosterUrl))
                    {
                        if (result != null)
                        {
                            result.PosterUrl ??= fallbackResult.PosterUrl;
                            result.BackdropUrl ??= fallbackResult.BackdropUrl;
                            result.Overview = string.IsNullOrEmpty(result.Overview) ? fallbackResult.Overview : result.Overview;
                            result.Rating = result.Rating > 0 ? result.Rating : fallbackResult.Rating;
                            result.Genres = string.IsNullOrEmpty(result.Genres) ? fallbackResult.Genres : result.Genres;
                            return result;
                        }

                        return fallbackResult;
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Fallback provider {0} failed for {1}", fallback.ProviderId, title);
                }
            }

            if (result != null)
            {
                return result;
            }
        }

        return null;
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
