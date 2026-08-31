using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.WatchFolder;

public interface IWatchFolderService
{
    Task ScanWatchFolderAsync();
    string MatchCategoryFromReleaseName(string releaseName);
}

public class WatchFolderService : IWatchFolderService
{
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger;

    private static readonly Regex TvPattern = new(@"(\bS\d{1,2}(E\d{1,2})?\b|\bSeason[\s\._]*\d+|\bComplete[\s\._]*Series\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnimePattern = new(@"(\[[^\]]+\]|\b(SubsPlease|Erai-raws|HorribleSubs|Judas)\b|(\b(Batch|Complete)\b.*\b(1080p|720p)\b.*(Subs?|Dual|FLAC)))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MoviePattern = new(@"\b(19\d{2}|20\d{2})\b.*\b(2160p|1080p|720p|UHD|BluRay|WEB-DL|Remux)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MusicPattern = new(@"\b(FLAC|MP3|320kbps|Vinyl|Lossless|CD|Album|Discography)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public WatchFolderService(
        IConfigService configService,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IDiskProvider diskProvider)
    {
        _configService = configService;
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _categoryService = categoryService;
        _diskProvider = diskProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ScanWatchFolderAsync()
    {
        if (!_configService.WatchFolderEnabled)
        {
            return;
        }

        var folder = _configService.WatchFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !_diskProvider.FolderExists(folder))
        {
            return;
        }

        _logger.Debug("Scanning watch folder: {0}", folder);

        var torrentFiles = _diskProvider.GetFiles(folder, false);
        foreach (var file in torrentFiles)
        {
            if (!file.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file);
                var parsed = _torrentFileParser.Parse(bytes);

                var category = MatchCategoryFromReleaseName(parsed.Name);
                _logger.Info("Watch folder adding: {0} with auto-matched category: {1}", parsed.Name, category);

                await _torrentService.AddFromParsedTorrentAsync(
                    parsed,
                    category: category,
                    startPaused: !_configService.WatchFolderAutoStartTorrents,
                    rawBytes: bytes);

                if (_configService.WatchFolderDeleteAddedTorrents)
                {
                    _diskProvider.DeleteFile(file);
                }
                else
                {
                    var loadedDir = Path.Combine(folder, "loaded");
                    _diskProvider.EnsureFolder(loadedDir);
                    var dest = Path.Combine(loadedDir, Path.GetFileName(file));
                    _diskProvider.MoveFile(file, dest, true);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process watch folder torrent: {0}", file);
            }
        }
    }

    public string MatchCategoryFromReleaseName(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return _configService.DefaultCategory;
        }

        if (TvPattern.IsMatch(releaseName))
        {
            return "tv";
        }

        if (AnimePattern.IsMatch(releaseName))
        {
            return "anime";
        }

        if (MoviePattern.IsMatch(releaseName))
        {
            return "movies";
        }

        if (MusicPattern.IsMatch(releaseName))
        {
            return "music";
        }

        return !string.IsNullOrEmpty(_configService.DefaultCategory)
            ? _configService.DefaultCategory
            : "other";
    }
}
