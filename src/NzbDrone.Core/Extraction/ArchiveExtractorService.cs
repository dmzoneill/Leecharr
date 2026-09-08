// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Extraction;

public interface IArchiveExtractorService
{
    Task<bool> ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory = null,
        string password = null,
        IReadOnlyList<string> passwordCandidates = null,
        CancellationToken cancellationToken = default);

    bool IsArchiveFile(string filePath);
}

public class ArchiveExtractorService : IArchiveExtractorService
{
    public const int DefaultMaxConcurrentExtractions = 2;

    private readonly IArchiveExtractorProvider provider;
    private readonly IDiskProvider diskProvider;
    private readonly SemaphoreSlim extractionSemaphore;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public SemaphoreSlim ConcurrencySemaphore => this.extractionSemaphore;

    public int ConcurrencyLimit { get; }

    public ArchiveExtractorService(IDiskProvider diskProvider, int maxConcurrentExtractions = DefaultMaxConcurrentExtractions)
    {
        this.diskProvider = diskProvider;
        this.provider = new SharpCompressExtractorProvider(diskProvider);
        this.ConcurrencyLimit = Math.Max(1, maxConcurrentExtractions);
        this.extractionSemaphore = new SemaphoreSlim(this.ConcurrencyLimit, this.ConcurrencyLimit);
    }

    public ArchiveExtractorService(IArchiveExtractorProvider provider, int maxConcurrentExtractions = DefaultMaxConcurrentExtractions)
        : this(provider, null, maxConcurrentExtractions)
    {
    }

    public ArchiveExtractorService(IArchiveExtractorProvider provider, IDiskProvider diskProvider, int maxConcurrentExtractions = DefaultMaxConcurrentExtractions)
    {
        this.provider = provider;
        this.diskProvider = diskProvider;
        this.ConcurrencyLimit = Math.Max(1, maxConcurrentExtractions);
        this.extractionSemaphore = new SemaphoreSlim(this.ConcurrencyLimit, this.ConcurrencyLimit);
    }

    public bool IsArchiveFile(string filePath)
    {
        return this.provider.CanExtract(filePath);
    }

    public async Task<bool> ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory = null,
        string password = null,
        IReadOnlyList<string> passwordCandidates = null,
        CancellationToken cancellationToken = default)
    {
        if (this.diskProvider != null)
        {
            if (string.IsNullOrWhiteSpace(archiveFilePath) || !this.diskProvider.FileExists(archiveFilePath))
            {
                this.logger.Warn("Archive file does not exist: {0}", archiveFilePath);
                return false;
            }

            var targetDir = destinationDirectory;
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                targetDir = Path.GetDirectoryName(archiveFilePath) ?? Path.GetTempPath();
            }

            this.diskProvider.EnsureFolder(targetDir);

            var estimatedSize = ArchiveTimeoutCalculator.EstimateTotalArchiveSize(archiveFilePath, this.diskProvider);
            var requiredSpace = (long)(estimatedSize * 1.5);
            var availableSpace = this.diskProvider.GetAvailableSpace(targetDir);

            if (availableSpace.HasValue && availableSpace.Value < requiredSpace)
            {
                this.logger.Warn(
                    "Insufficient free disk space on '{0}' for extracting '{1}'. Required: {2} bytes (1.5x estimated size), Available: {3} bytes.",
                    targetDir,
                    archiveFilePath,
                    requiredSpace,
                    availableSpace.Value);
                return false;
            }
        }

        await this.extractionSemaphore.WaitAsync(cancellationToken);
        try
        {
            return await this.provider.ExtractAsync(archiveFilePath, destinationDirectory, password, passwordCandidates, cancellationToken);
        }
        finally
        {
            this.extractionSemaphore.Release();
        }
    }
}
