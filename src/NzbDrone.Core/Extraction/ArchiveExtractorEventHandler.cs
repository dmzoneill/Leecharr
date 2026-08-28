using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractionCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }
    public string ArchivePath { get; set; }
    public string DestinationDirectory { get; set; }
}

public class ArchiveExtractorEventHandler : IHandle<TorrentDownloadCompletedEvent>
{
    private readonly IArchiveExtractorService _extractorService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IDiskProvider _diskProvider;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ArchiveExtractorEventHandler(
        IArchiveExtractorService extractorService,
        ITorrentFileService torrentFileService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator)
    {
        _extractorService = extractorService;
        _torrentFileService = torrentFileService;
        _diskProvider = diskProvider;
        _eventAggregator = eventAggregator;
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
                    if (_extractorService.IsArchiveFile(file.Path) && !IsSecondaryVolume(file.Path))
                    {
                        var fullPath = Path.Combine(savePath, file.Path);
                        if (_diskProvider.FileExists(fullPath))
                        {
                            _logger.Info("Auto-extracting archive {0} for completed torrent {1}", fullPath, message.Torrent.Name);
                            var destDir = Path.GetDirectoryName(fullPath);
                            var success = await _extractorService.ExtractArchiveAsync(fullPath, destDir);
                            if (success)
                            {
                                _eventAggregator.PublishEvent(new ArchiveExtractionCompletedEvent
                                {
                                    Torrent = message.Torrent,
                                    ArchivePath = fullPath,
                                    DestinationDirectory = destDir
                                });
                            }
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

    private static bool IsSecondaryVolume(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Regex.IsMatch(ext, @"^\.(r\d{2}|\d{3}|z\d{2})$", RegexOptions.IgnoreCase) && ext != ".r00" && ext != ".001" && ext != ".z01")
        {
            return true;
        }

        if (Regex.IsMatch(path, @"\.(part0*[2-9]\d*|part[2-9]\d*)\.(rar|7z|zip)$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(path, @"\.(7z|tar|zip)\.0*[2-9]\d*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }
}
