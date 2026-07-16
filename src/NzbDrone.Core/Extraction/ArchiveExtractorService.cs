using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace NzbDrone.Core.Extraction;

public interface IArchiveExtractorService
{
    Task<bool> ExtractArchiveAsync(string archiveFilePath, string destinationDirectory = null);
    bool IsArchiveFile(string filePath);
}

public class ArchiveExtractorService : IArchiveExtractorService
{
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger;

    private static readonly string[] SupportedExtensions = { ".rar", ".zip", ".7z", ".tar", ".gz" };

    public ArchiveExtractorService(IDiskProvider diskProvider)
    {
        _diskProvider = diskProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsArchiveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    public async Task<bool> ExtractArchiveAsync(string archiveFilePath, string destinationDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !_diskProvider.FileExists(archiveFilePath))
        {
            _logger.Warn("Archive file does not exist: {0}", archiveFilePath);
            return false;
        }

        var targetDir = destinationDirectory;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            targetDir = Path.GetDirectoryName(archiveFilePath) ?? "/tmp";
        }

        _diskProvider.EnsureFolder(targetDir);

        return await Task.Run(() =>
        {
            try
            {
                _logger.Info("Extracting archive '{0}' to '{1}'...", archiveFilePath, targetDir);

                using var stream = File.OpenRead(archiveFilePath);
                using var archive = ArchiveFactory.OpenArchive(stream);

                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    entry.WriteToDirectory(targetDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                _logger.Info("Archive '{0}' successfully extracted.", archiveFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract archive: {0}", archiveFilePath);
                return false;
            }
        });
    }
}
