// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.WatchFolder;

public interface IWatchFolderService : IDisposable
{
    Task ScanWatchFolderAsync();

    string MatchCategoryFromReleaseName(string releaseName);

    bool IsFileReady(string path);

    Task<bool> ProcessFileAsync(string file, string folder = null);

    void StartWatcher();

    void StopWatcher();

    void OnFileSystemWatcherCreated(object sender, FileSystemEventArgs e);

    void OnFileSystemWatcherRenamed(object sender, RenamedEventArgs e);
}

public class WatchFolderService : IWatchFolderService, IHandle<ConfigSavedEvent>, IHandle<ConfigFileSavedEvent>
{
    private readonly IConfigService configService;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<string, int> failedAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> processingFiles = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private FileSystemWatcher watcher;

    private static readonly Regex AnimeGroupPattern = new(
        @"\b(SubsPlease|Erai-raws|HorribleSubs|Judas|Commie|Dame-Desu|ASW|Golumpa|LostYears|PAS|Coalgirls|Anime Time|EMBER)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TvPattern = new(
        @"(\bS\d{1,2}(E\d{1,2})?\b|\bSeason[\s\._]*\d+|\bComplete[\s\._]*Series\b|\b(EZTV|ETTV)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MoviePattern = new(
        @"(\b(19\d{2}|20\d{2})\b.*\b(2160p|1080p|720p|UHD|BluRay|WEB-DL|Remux)\b|\b(2160p|1080p|720p|UHD|BluRay|WEB-DL|Remux)\b.*\b(19\d{2}|20\d{2})\b|\b(YTS|YIFY)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnimePattern = new(
        @"(\b(SubsPlease|Erai-raws|HorribleSubs|Judas|Commie|Dame-Desu|ASW|Golumpa|LostYears|PAS|Coalgirls|Anime Time|EMBER)\b|(\b(Batch|Complete)\b.*\b(1080p|720p)\b.*(Subs?|Dual|FLAC)))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MusicPattern = new(
        @"\b(FLAC|MP3|320kbps|Vinyl|Lossless|CD|Album|Discography)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    public virtual bool IsFileReady(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void StartWatcher()
    {
        if (!this.configService.WatchFolderEnabled)
        {
            this.StopWatcher();
            return;
        }

        var folder = this.configService.WatchFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !this.diskProvider.FolderExists(folder))
        {
            this.StopWatcher();
            return;
        }

        try
        {
            this.StopWatcher();
            this.watcher = new FileSystemWatcher(folder, "*.torrent")
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            this.watcher.Created += this.OnFileSystemWatcherCreated;
            this.watcher.Renamed += this.OnFileSystemWatcherRenamed;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to initialize FileSystemWatcher on {0}", folder);
        }
    }

    public void StopWatcher()
    {
        if (this.watcher != null)
        {
            try
            {
                this.watcher.EnableRaisingEvents = false;
                this.watcher.Created -= this.OnFileSystemWatcherCreated;
                this.watcher.Renamed -= this.OnFileSystemWatcherRenamed;
                this.watcher.Dispose();
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error disposing FileSystemWatcher");
            }
            finally
            {
                this.watcher = null;
            }
        }
    }

    public void Dispose()
    {
        this.StopWatcher();
    }

    public void Handle(ConfigSavedEvent message)
    {
        this.RestartOrStopWatcher();
    }

    public void Handle(ConfigFileSavedEvent message)
    {
        this.RestartOrStopWatcher();
    }

    public void OnFileSystemWatcherCreated(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.HandleFileSystemWatcherCreatedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error processing created watch folder file: {0}", e?.FullPath);
            }
        });
    }

    public async Task HandleFileSystemWatcherCreatedAsync(FileSystemEventArgs e)
    {
        if (e == null || string.IsNullOrWhiteSpace(e.FullPath))
        {
            return;
        }

        if (!e.FullPath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var folder = Path.GetDirectoryName(e.FullPath) ?? this.configService.WatchFolderPath;
        await this.ProcessFileAsync(e.FullPath, folder).ConfigureAwait(false);
    }

    public void OnFileSystemWatcherRenamed(object sender, RenamedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.HandleFileSystemWatcherRenamedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error processing renamed watch folder file: {0}", e?.FullPath);
            }
        });
    }

    public async Task HandleFileSystemWatcherRenamedAsync(RenamedEventArgs e)
    {
        if (e == null || string.IsNullOrWhiteSpace(e.FullPath))
        {
            return;
        }

        if (!e.FullPath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var folder = Path.GetDirectoryName(e.FullPath) ?? this.configService.WatchFolderPath;
        await this.ProcessFileAsync(e.FullPath, folder).ConfigureAwait(false);
    }

    private void RestartOrStopWatcher()
    {
        if (this.configService.WatchFolderEnabled)
        {
            this.StartWatcher();
        }
        else
        {
            this.StopWatcher();
        }
    }

    public async Task<bool> ProcessFileAsync(string file, string folder = null)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(file);
        if (!this.processingFiles.TryAdd(fullPath, 0))
        {
            return false;
        }

        try
        {
            folder ??= this.configService.WatchFolderPath;

            if (!this.IsFileReady(file))
            {
                this.logger.Debug("Watch folder file '{0}' is locked or still being written. Skipping this scan cycle.", file);
                return false;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                var parsed = this.torrentFileParser.Parse(bytes);

                var category = this.MatchCategoryFromReleaseName(parsed.Name);
                this.logger.Info("Watch folder adding: {0} with auto-matched category: {1}", parsed.Name, category);

                await this.torrentService.AddFromParsedTorrentAsync(
                    parsed,
                    category: category,
                    startPaused: !this.configService.WatchFolderAutoStartTorrents,
                    rawBytes: bytes).ConfigureAwait(false);

                this.failedAttempts.TryRemove(file, out _);

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

                return true;
            }
            catch (Exception ex)
            {
                var attempts = this.failedAttempts.AddOrUpdate(file, 1, (_, count) => count + 1);
                if (attempts >= 3)
                {
                    this.logger.Warn(ex, "Watch folder torrent '{0}' failed after {1} attempts. Quarantining file.", file, attempts);
                    this.failedAttempts.TryRemove(file, out _);
                    this.QuarantineFile(folder, file);
                }
                else
                {
                    this.logger.Warn(ex, "Failed to process watch folder torrent '{0}' (attempt {1}/3). Will retry.", file, attempts);
                }

                return false;
            }
        }
        finally
        {
            this.processingFiles.TryRemove(fullPath, out _);
        }
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
                await this.ProcessFileAsync(file, folder).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error processing watch folder file: {0}", file);
            }
        }
    }

    public string MatchCategoryFromReleaseName(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return this.configService.DefaultCategory;
        }

        string detected = null;

        if (AnimeGroupPattern.IsMatch(releaseName))
        {
            detected = "anime";
        }
        else if (TvPattern.IsMatch(releaseName))
        {
            detected = "tv";
        }
        else if (MoviePattern.IsMatch(releaseName))
        {
            detected = "movies";
        }
        else if (AnimePattern.IsMatch(releaseName))
        {
            detected = "anime";
        }
        else if (MusicPattern.IsMatch(releaseName))
        {
            detected = "music";
        }

        if (detected != null)
        {
            return this.ResolveConfiguredCategory(detected);
        }

        return !string.IsNullOrEmpty(this.configService.DefaultCategory)
            ? this.configService.DefaultCategory
            : "other";
    }

    private void QuarantineFile(string watchFolder, string file)
    {
        try
        {
            var failedDir = Path.Combine(watchFolder, "failed");
            this.diskProvider.EnsureFolder(failedDir);
            var dest = Path.Combine(failedDir, Path.GetFileName(file));
            this.diskProvider.MoveFile(file, dest, overwrite: true);
            this.logger.Info("Quarantined corrupt watch folder file to '{0}'", dest);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to quarantine corrupt watch folder file: {0}", file);
        }
    }

    private string ResolveConfiguredCategory(string detectedCategory)
    {
        if (this.categoryService == null)
        {
            return detectedCategory;
        }

        try
        {
            var categories = this.categoryService.GetAll()?.ToList();
            if (categories == null || categories.Count == 0)
            {
                return detectedCategory;
            }

            var exactMatch = categories.FirstOrDefault(c =>
                string.Equals(c.Name, detectedCategory, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch.Name;
            }

            var synonyms = detectedCategory switch
            {
                "tv" => new[] { "tv", "shows", "television", "series" },
                "movies" => new[] { "movies", "movie", "films", "film" },
                "anime" => new[] { "anime", "animation" },
                "music" => new[] { "music", "audio", "albums" },
                _ => Array.Empty<string>(),
            };

            foreach (var synonym in synonyms)
            {
                var match = categories.FirstOrDefault(c =>
                    string.Equals(c.Name, synonym, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(synonym, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match.Name;
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error cross-referencing category '{0}' with CategoryService", detectedCategory);
        }

        return detectedCategory;
    }
}
