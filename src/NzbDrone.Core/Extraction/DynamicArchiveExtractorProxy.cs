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

public class DynamicArchiveExtractorProxy : IArchiveExtractorService, IArchiveExtractorManager, IDisposable
{
    private readonly IEnumerable<IArchiveExtractorProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly SemaphoreSlim switchLock = new(1, 1);
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

        var desiredProviderId = this.configService.ActiveArchiveExtractor;
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

            this.eventAggregator.PublishEvent(new ArchiveExtractorSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId));

            this.logger.Info("Archive extractor hot-swap completed: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);

            return new ExtractorSwitchResult
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
                Error = $"Extractor switch failed: {ex.Message}",
            };
        }
        finally
        {
            this.switchLock.Release();
        }
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
            targetDir = Path.GetDirectoryName(archiveFilePath) ?? "/tmp";
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

    private long EstimateArchiveUncompressedSize(string archiveFilePath)
    {
        try
        {
            var baseSize = this.diskProvider.GetFileSize(archiveFilePath);
            var dir = Path.GetDirectoryName(archiveFilePath);
            if (string.IsNullOrEmpty(dir) || !this.diskProvider.FolderExists(dir))
            {
                return Math.Max(0L, baseSize);
            }

            var fileName = Path.GetFileName(archiveFilePath);
            var partMatch = Regex.Match(fileName, @"^(.*?)\.part\d+\.(rar|7z|zip)$", RegexOptions.IgnoreCase);
            if (partMatch.Success)
            {
                var prefix = partMatch.Groups[1].Value;
                var ext = partMatch.Groups[2].Value;
                var companionFiles = this.diskProvider.GetFiles(dir, false);
                long totalVolumeSize = 0;
                foreach (var file in companionFiles)
                {
                    var fn = Path.GetFileName(file);
                    if (Regex.IsMatch(fn, $@"^{Regex.Escape(prefix)}\.part\d+\.{Regex.Escape(ext)}$", RegexOptions.IgnoreCase))
                    {
                        totalVolumeSize += this.diskProvider.GetFileSize(file);
                    }
                }

                return totalVolumeSize > 0 ? totalVolumeSize : Math.Max(0L, baseSize);
            }

            var numMatch = Regex.Match(fileName, @"^(.*?)\.(r\d{2}|\d{3}|z\d{2}|001)$", RegexOptions.IgnoreCase);
            if (numMatch.Success)
            {
                var prefix = numMatch.Groups[1].Value;
                var companionFiles = this.diskProvider.GetFiles(dir, false);
                long totalVolumeSize = 0;
                foreach (var file in companionFiles)
                {
                    var fn = Path.GetFileName(file);
                    if (Regex.IsMatch(fn, $@"^{Regex.Escape(prefix)}\.(r\d{{2}}|\d{{3}}|z\d{{2}}|rar)$", RegexOptions.IgnoreCase))
                    {
                        totalVolumeSize += this.diskProvider.GetFileSize(file);
                    }
                }

                return totalVolumeSize > 0 ? totalVolumeSize : Math.Max(0L, baseSize);
            }

            return Math.Max(0L, baseSize);
        }
        catch
        {
            return 0L;
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
