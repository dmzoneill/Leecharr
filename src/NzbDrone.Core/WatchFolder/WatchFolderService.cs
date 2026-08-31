// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private readonly IConfigService configService;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

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
        this.configService = configService;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ScanWatchFolderAsync()
    {
        if (!this.configService.WatchFolderEnabled)
        {
            return;
        }

        var folder = this.configService.WatchFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !this.diskProvider.FolderExists(folder))
        {
            return;
        }

        this.logger.Debug("Scanning watch folder: {0}", folder);

        var torrentFiles = this.diskProvider.GetFiles(folder, false);
        foreach (var file in torrentFiles)
        {
            if (!file.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file);
                var parsed = this.torrentFileParser.Parse(bytes);

                var category = this.MatchCategoryFromReleaseName(parsed.Name);
                this.logger.Info("Watch folder adding: {0} with auto-matched category: {1}", parsed.Name, category);

                await this.torrentService.AddFromParsedTorrentAsync(
                    parsed,
                    category: category,
                    startPaused: !this.configService.WatchFolderAutoStartTorrents,
                    rawBytes: bytes);

                if (this.configService.WatchFolderDeleteAddedTorrents)
                {
                    this.diskProvider.DeleteFile(file);
                }
                else
                {
                    var loadedDir = Path.Combine(folder, "loaded");
                    this.diskProvider.EnsureFolder(loadedDir);
                    var dest = Path.Combine(loadedDir, Path.GetFileName(file));
                    this.diskProvider.MoveFile(file, dest, true);
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to process watch folder torrent: {0}", file);
            }
        }
    }

    public string MatchCategoryFromReleaseName(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return this.configService.DefaultCategory;
        }

        if (AnimePattern.IsMatch(releaseName))
        {
            return "anime";
        }

        if (TvPattern.IsMatch(releaseName))
        {
            return "tv";
        }

        if (MoviePattern.IsMatch(releaseName))
        {
            return "movies";
        }

        if (MusicPattern.IsMatch(releaseName))
        {
            return "music";
        }

        return !string.IsNullOrEmpty(this.configService.DefaultCategory)
            ? this.configService.DefaultCategory
            : "other";
    }
}
