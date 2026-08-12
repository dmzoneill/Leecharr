using System;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractorEventHandler : IHandle<TorrentDownloadCompletedEvent>
{
    private readonly IArchiveExtractorService _extractorService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ArchiveExtractorEventHandler(
        IArchiveExtractorService extractorService,
        ITorrentFileService torrentFileService,
        IDiskProvider diskProvider)
    {
        _extractorService = extractorService;
        _torrentFileService = torrentFileService;
        _diskProvider = diskProvider;
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var files = _torrentFileService.GetFiles(message.Torrent.Id);
                var savePath = message.Torrent.SavePath;
                if (string.IsNullOrEmpty(savePath))
                {
                    return;
                }

                foreach (var file in files)
                {
                    if (_extractorService.IsArchiveFile(file.Path))
                    {
                        var fullPath = Path.Combine(savePath, file.Path);
                        if (_diskProvider.FileExists(fullPath))
                        {
                            _logger.Info("Auto-extracting archive {0} for completed torrent {1}", fullPath, message.Torrent.Name);
                            var destDir = Path.GetDirectoryName(fullPath);
                            await _extractorService.ExtractArchiveAsync(fullPath, destDir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to auto-extract archives for torrent {0}", message.Torrent.Name);
            }
        });
    }
}
