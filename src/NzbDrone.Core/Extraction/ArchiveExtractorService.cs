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
    private readonly IArchiveExtractorProvider _provider;

    public ArchiveExtractorService(IDiskProvider diskProvider)
    {
        _provider = new SharpCompressExtractorProvider(diskProvider);
    }

    public ArchiveExtractorService(IArchiveExtractorProvider provider)
    {
        _provider = provider;
    }

    public bool IsArchiveFile(string filePath)
    {
        return _provider.CanExtract(filePath);
    }

    public Task<bool> ExtractArchiveAsync(string archiveFilePath, string destinationDirectory = null)
    {
        return _provider.ExtractAsync(archiveFilePath, destinationDirectory);
    }
}
