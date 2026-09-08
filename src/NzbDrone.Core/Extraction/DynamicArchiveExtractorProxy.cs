// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extraction;

public class DynamicArchiveExtractorProxy : IArchiveExtractorService, IArchiveExtractorManager, IHandle<ConfigSavedEvent>, IDisposable
{
    private readonly IEnumerable<IArchiveExtractorProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly SemaphoreSlim switchLock = new(1, 1);
    private readonly SemaphoreSlim extractionSemaphore;
    private IArchiveExtractorProvider activeProvider;
    private bool disposed;

    public IArchiveExtractorProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "SharpCompress";

    public DynamicArchiveExtractorProxy(
        IEnumerable<IArchiveExtractorProvider> availableProviders,
        IConfigService configService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? Array.Empty<IArchiveExtractorProvider>();
        this.configService = configService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var maxConcurrency = this.configService != null && this.configService.MaxConcurrentExtractions > 0
            ? this.configService.MaxConcurrentExtractions
            : 2;
        this.extractionSemaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));

        var desiredProviderId = this.configService?.ActiveArchiveExtractor;
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("SharpCompress", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No archive extractor providers are registered in the system container.");
        }

        this.logger.Info("DynamicArchiveExtractorProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IArchiveExtractorProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IArchiveExtractorProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ExtractorHealthCheckResult> ProbeProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new ExtractorHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Extractor provider '{providerId}' is not recognized or registered.",
                Warnings = new List<string> { "Provider identifier not found in extractor registry." },
            };
        }

        return await provider.ProbeHealthAsync(cancellationToken);
    }

    public async Task<ExtractorSwitchResult> SwitchProviderAsync(string targetProviderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new ExtractorSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty.",
            };
        }

        var targetProvider = this.GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new ExtractorSwitchResult
            {
                Success = false,
                Error = $"Target extractor provider '{targetProviderId}' is not registered.",
            };
        }

        var current = Volatile.Read(ref this.activeProvider);
        if (string.Equals(current.ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new ExtractorSwitchResult
            {
                Success = true,
                PreviousProvider = current.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Extractor provider '{targetProvider.DisplayName}' is already active.",
            };
        }

        ArchiveExtractorSwitchedEvent switchedEvent = null;
        ExtractorSwitchResult result;

        await this.switchLock.WaitAsync(cancellationToken);
        try
        {
            var health = await targetProvider.ProbeHealthAsync(cancellationToken);
            if (!health.IsHealthy)
            {
                return new ExtractorSwitchResult
                {
                    Success = false,
                    PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                    ActiveProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                    Error = $"Cannot switch to extractor provider '{targetProvider.DisplayName}': health check failed ({health.StatusMessage}).",
                };
            }

            var previousProvider = Volatile.Read(ref this.activeProvider);
            this.logger.Info("Switching archive extractor: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            Volatile.Write(ref this.activeProvider, targetProvider);

            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveArchiveExtractor", targetProvider.ProviderId },
            });

            switchedEvent = new ArchiveExtractorSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId);

            this.logger.Info("Archive extractor hot-swap completed: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            result = new ExtractorSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched archive extractor to {targetProvider.DisplayName}.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Fatal error during extractor hot-swap to {0}", targetProviderId);
            return new ExtractorSwitchResult
            {
                Success = false,
                PreviousProvider = Volatile.Read(ref this.activeProvider)?.ProviderId,
                ActiveProvider = Volatile.Read(ref this.activeProvider)?.ProviderId,
                Error = $"Archive extractor switch failed: {ex.Message}",
            };
        }
        finally
        {
            this.switchLock.Release();
        }

        if (switchedEvent != null)
        {
            this.eventAggregator.PublishEvent(switchedEvent);
        }

        return result;
    }

    public bool IsArchiveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var active = Volatile.Read(ref this.activeProvider);
        if (active != null && active.CanExtract(filePath))
        {
            return true;
        }

        return this.availableProviders.Any(p => p.CanExtract(filePath));
    }

    public async Task<bool> ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory = null,
        string password = null,
        IReadOnlyList<string> passwordCandidates = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !this.diskProvider.FileExists(archiveFilePath))
        {
            this.logger.Warn("Archive file does not exist: {0}", archiveFilePath);
            return false;
        }

        var targetDir = destinationDirectory;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            var dir = Path.GetDirectoryName(archiveFilePath);
            targetDir = !string.IsNullOrWhiteSpace(dir)
                ? dir
                : (!string.IsNullOrWhiteSpace(this.configService?.ExtractorTempDir)
                    ? this.configService.ExtractorTempDir
                    : (!string.IsNullOrWhiteSpace(this.configService?.IncompleteDownloadDir)
                        ? this.configService.IncompleteDownloadDir
                        : Path.GetTempPath()));
        }

        this.diskProvider.EnsureFolder(targetDir);

        var estimatedSize = this.EstimateArchiveUncompressedSize(archiveFilePath);
        var requiredSpace = (long)(estimatedSize * 1.5);
        var availableSpace = this.diskProvider.GetAvailableSpace(targetDir);

        if (availableSpace.HasValue && availableSpace.Value < requiredSpace)
        {
            this.logger.Warn(
                "Insufficient free disk space on '{0}' for extracting '{1}'. Required: {2} bytes (1.5x estimated uncompressed size), Available: {3} bytes.",
                targetDir,
                archiveFilePath,
                requiredSpace,
                availableSpace.Value);
            return false;
        }

        await this.extractionSemaphore.WaitAsync(cancellationToken);
        try
        {
            var active = Volatile.Read(ref this.activeProvider);
            var success = false;

            try
            {
                success = await active.ExtractAsync(archiveFilePath, targetDir, password, passwordCandidates, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Active extractor '{0}' failed for '{1}' with exception.", active.ProviderId, archiveFilePath);
            }

            if (!success && !active.ProviderId.Equals("SharpCompress", StringComparison.OrdinalIgnoreCase))
            {
                var fallback = this.GetProvider("SharpCompress");
                if (fallback != null)
                {
                    this.logger.Warn("Active extractor '{0}' failed for '{1}'. Attempting fallback to SharpCompress...", active.ProviderId, archiveFilePath);
                    try
                    {
                        success = await fallback.ExtractAsync(archiveFilePath, targetDir, password, passwordCandidates, cancellationToken);
                        if (success)
                        {
                            this.logger.Info("SharpCompress fallback extraction succeeded for '{0}'.", archiveFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "SharpCompress fallback extraction failed for '{0}'.", archiveFilePath);
                    }
                }
            }

            return success;
        }
        finally
        {
            this.extractionSemaphore.Release();
        }
    }

    private long EstimateArchiveUncompressedSize(string archiveFilePath)
    {
        return ArchiveTimeoutCalculator.EstimateTotalArchiveSize(archiveFilePath, this.diskProvider);
    }

    public void Handle(ConfigSavedEvent message)
    {
        var desiredProviderId = this.configService?.ActiveArchiveExtractor;
        if (!string.IsNullOrWhiteSpace(desiredProviderId) &&
            !string.Equals(this.ActiveProviderId, desiredProviderId, StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.SwitchProviderAsync(desiredProviderId);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Failed to switch active archive extractor on ConfigSavedEvent to {0}", desiredProviderId);
                }
            });
        }
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.switchLock.Dispose();
            this.extractionSemaphore.Dispose();
        }
    }
}
