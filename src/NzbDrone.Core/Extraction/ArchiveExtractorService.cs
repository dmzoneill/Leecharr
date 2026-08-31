// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Extraction;

public interface IArchiveExtractorService
{
    Task<bool> ExtractArchiveAsync(string archiveFilePath, string destinationDirectory = null);

    bool IsArchiveFile(string filePath);
}

public class ArchiveExtractorService : IArchiveExtractorService
{
    private readonly IArchiveExtractorProvider provider;

    public ArchiveExtractorService(IDiskProvider diskProvider)
    {
        this.provider = new SharpCompressExtractorProvider(diskProvider);
    }

    public ArchiveExtractorService(IArchiveExtractorProvider provider)
    {
        this.provider = provider;
    }

    public bool IsArchiveFile(string filePath)
    {
        return this.provider.CanExtract(filePath);
    }

    public Task<bool> ExtractArchiveAsync(string archiveFilePath, string destinationDirectory = null)
    {
        return this.provider.ExtractAsync(archiveFilePath, destinationDirectory);
    }
}
