// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly SemaphoreSlim extractionSemaphore;

    public SemaphoreSlim ConcurrencySemaphore => this.extractionSemaphore;

    public int ConcurrencyLimit { get; }

    public ArchiveExtractorService(IDiskProvider diskProvider, int maxConcurrentExtractions = DefaultMaxConcurrentExtractions)
    {
        this.provider = new SharpCompressExtractorProvider(diskProvider);
        this.ConcurrencyLimit = Math.Max(1, maxConcurrentExtractions);
        this.extractionSemaphore = new SemaphoreSlim(this.ConcurrencyLimit, this.ConcurrencyLimit);
    }

    public ArchiveExtractorService(IArchiveExtractorProvider provider, int maxConcurrentExtractions = DefaultMaxConcurrentExtractions)
    {
        this.provider = provider;
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
