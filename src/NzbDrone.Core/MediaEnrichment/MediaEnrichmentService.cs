// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.MediaEnrichment;

public class MediaEnrichedEvent : IEvent
{
    public int TorrentId { get; set; }

    public TorrentMediaMetadata Metadata { get; set; }
}

public interface IMediaEnrichmentService
{
    Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null);

    TorrentMediaMetadata GetMetadata(int torrentId);

    Dictionary<int, TorrentMediaMetadata> GetAllMetadata()
    {
        return new Dictionary<int, TorrentMediaMetadata>();
    }

    void DeleteMetadata(int torrentId);

    void CleanupTorrentCache(int torrentId);

    Task<string> CacheArtworkAsync(string url, int torrentId, string type);
}

public class MediaEnrichmentService : IMediaEnrichmentService, IHandle<TorrentDownloadCompletedEvent>, IHandle<ArchiveExtractionCompletedEvent>
{
    private readonly ITorrentMediaMetadataRepository repository;
    private readonly IMediaContainerInspector inspector;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IEventAggregator eventAggregator;
    private readonly IMediaMetadataService mediaMetadataService;
    private readonly IArrConnectionRepository arrRepository;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly HttpClient httpClient;
    private readonly ITorrentFileRepository torrentFileRepository;
    private readonly ITorrentFileService torrentFileService;
    private readonly Logger logger;

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".m2ts", ".iso",
        ".mp3", ".flac", ".aac", ".ogg", ".wav", ".m4a", ".opus", ".wma",
    };

    public MediaEnrichmentService(
        ITorrentMediaMetadataRepository repository,
        IMediaContainerInspector inspector,
        IConfigService configService,
        IAppFolderInfo appFolderInfo,
        IEventAggregator eventAggregator,
        IMediaMetadataService mediaMetadataService = null,
        IArrConnectionRepository arrRepository = null,
        ISafeHttpClientService safeHttpClientService = null,
        HttpClient httpClient = null,
        IHttpTransportEngine transportEngine = null,
        ITorrentFileRepository torrentFileRepository = null,
        ITorrentFileService torrentFileService = null)
    {
        this.repository = repository;
        this.inspector = inspector;
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
        this.eventAggregator = eventAggregator;
        this.mediaMetadataService = mediaMetadataService;
        this.arrRepository = arrRepository;
        this.safeHttpClientService = safeHttpClientService ?? (httpClient != null ? new SafeHttpClientService(httpClient) : (transportEngine != null ? new SafeHttpClientService(transportEngine) : new SafeHttpClientService()));
        this.httpClient = httpClient ?? (transportEngine != null ? new HttpClient(new DynamicHttpTransportHandler(transportEngine), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) } : new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        this.torrentFileRepository = torrentFileRepository;
        this.torrentFileService = torrentFileService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null)
    {
        if (torrent == null)
        {
            return null;
        }

        this.logger.Debug("Enriching metadata for torrent: {0}", torrent.Name);

        var existing = this.repository.GetByTorrentId(torrent.Id);
        var metadata = existing ?? new TorrentMediaMetadata { TorrentId = torrent.Id };

        // 1. Inspect container metadata if local file is available
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                var containerInfo = this.inspector.InspectFile(filePath);
                if (containerInfo != null)
                {
                    metadata.MediaInfoJson = JsonSerializer.Serialize(containerInfo);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inspect media file: {0}", filePath);
            }
        }

        // 2. Query dynamic metadata providers if available
        if (this.mediaMetadataService != null && !string.IsNullOrWhiteSpace(torrent.Name))
        {
            try
            {
                var dynamicMeta = await this.mediaMetadataService.GetMetadataAsync(torrent.Name, torrent.Category);
                if (dynamicMeta != null)
                {
                    if (!string.IsNullOrEmpty(dynamicMeta.Title))
                    {
                        metadata.Title = dynamicMeta.Title;
                    }

                    if (dynamicMeta.Year > 0)
                    {
                        metadata.Year = dynamicMeta.Year;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.Overview))
                    {
                        metadata.Overview = dynamicMeta.Overview;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.PosterUrl))
                    {
                        metadata.PosterUrl = dynamicMeta.PosterUrl;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.BackdropUrl))
                    {
                        metadata.BackdropUrl = dynamicMeta.BackdropUrl;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.Genres))
                    {
                        metadata.Genres = dynamicMeta.Genres;
                    }

                    if (dynamicMeta.Rating > 0)
                    {
                        metadata.Rating = dynamicMeta.Rating;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.ImdbId))
                    {
                        metadata.ImdbId = dynamicMeta.ImdbId;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.TmdbId))
                    {
                        metadata.TmdbId = dynamicMeta.TmdbId;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.TvdbId))
                    {
                        metadata.TvdbId = dynamicMeta.TvdbId;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.MediaType))
                    {
                        metadata.ArrType = dynamicMeta.MediaType;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to query dynamic metadata for {0}", torrent.Name);
            }
        }

        // 3. Fallback title and heuristics from Torrent Name if empty
        if (string.IsNullOrEmpty(metadata.Title))
        {
            metadata.Title = torrent.Name;
        }

        if (string.IsNullOrEmpty(metadata.MediaInfoJson))
        {
            try
            {
                var guessed = this.inspector.Inspect(new MemoryStream(new byte[8]), torrent.Name);
                if (guessed != null)
                {
                    metadata.MediaInfoJson = JsonSerializer.Serialize(guessed);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to inspect name heuristics for {0}", torrent.Name);
            }
        }

        if (string.IsNullOrEmpty(metadata.ArrType))
        {
            metadata.ArrType = GuessArrType(torrent.Category, torrent.Name);
        }

        // 4. Cache remote or local poster if URL/path present and local path not yet downloaded
        if (!string.IsNullOrEmpty(metadata.PosterUrl) && string.IsNullOrEmpty(metadata.PosterLocalPath))
        {
            metadata.PosterLocalPath = await this.CacheArtworkAsync(metadata.PosterUrl, torrent.Id, "poster");
        }

        if (!string.IsNullOrEmpty(metadata.BackdropUrl) && string.IsNullOrEmpty(metadata.BackdropLocalPath))
        {
            metadata.BackdropLocalPath = await this.CacheArtworkAsync(metadata.BackdropUrl, torrent.Id, "backdrop");
        }

        if (existing == null)
        {
            this.repository.Insert(metadata);
        }
        else
        {
            this.repository.Update(metadata);
        }

        this.eventAggregator.PublishEvent(new MediaEnrichedEvent { TorrentId = torrent.Id, Metadata = metadata });
        return metadata;
    }

    public TorrentMediaMetadata GetMetadata(int torrentId)
    {
        return this.repository.GetByTorrentId(torrentId);
    }

    public Dictionary<int, TorrentMediaMetadata> GetAllMetadata()
    {
        try
        {
            return this.repository.All()
                .GroupBy(m => m.TorrentId)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to load all media metadata");
            return new Dictionary<int, TorrentMediaMetadata>();
        }
    }

    public void DeleteMetadata(int torrentId)
    {
        var metadata = this.repository.GetByTorrentId(torrentId);
        if (metadata != null)
        {
            // Prune artwork if configured
            if (this.configService.AutoPruneRemovedArtwork)
            {
                DeleteLocalFile(metadata.PosterLocalPath);
                DeleteLocalFile(metadata.BackdropLocalPath);
                this.CleanupTorrentCache(torrentId);
            }

            this.repository.DeleteByTorrentId(torrentId);
        }
    }

    public void CleanupTorrentCache(int torrentId)
    {
        try
        {
            var cacheDir = Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache", torrentId.ToString());
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
                this.logger.Debug("Cleaned up media cache directory for torrent {0}", torrentId);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to clean up media cache directory for torrent: {0}", torrentId);
        }
    }

    public async Task<string> CacheArtworkAsync(string url, int torrentId, string type)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var cacheDir = Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache", torrentId.ToString());
            Directory.CreateDirectory(cacheDir);

            // Handle local file path
            if (File.Exists(url) || Path.IsPathRooted(url))
            {
                if (File.Exists(url))
                {
                    var ext = Path.GetExtension(url);
                    if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                    {
                        ext = ".jpg";
                    }

                    var localFile = Path.Combine(cacheDir, $"{type}{ext}");
                    File.Copy(url, localFile, overwrite: true);
                    this.logger.Debug("Copied local {0} artwork from {1} to {2}", type, url, localFile);
                    return localFile;
                }

                this.logger.Warn("Local artwork file does not exist: {0}", url);
                return null;
            }

            // Remote URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                this.logger.Warn("Refusing to cache artwork from non-HTTP/HTTPS URL: {0}", url);
                return null;
            }

            var extRemote = ".jpg";
            var uriExt = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(uriExt) && uriExt.Length <= 5)
            {
                extRemote = uriExt;
            }

            var destFile = Path.Combine(cacheDir, $"{type}{extRemote}");

            Dictionary<string, string> customHeaders = null;
            var apiKey = this.GetServarrApiKey(url);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                customHeaders = new Dictionary<string, string> { { "X-Api-Key", apiKey } };
            }

            var bytes = await this.safeHttpClientService.DownloadBytesAsync(uri, maxSizeBytes: 10 * 1024 * 1024, customHeaders: customHeaders);

            if (!IsValidImage(bytes))
            {
                this.logger.Warn("Downloaded artwork from {0} has invalid image magic bytes (not JPEG/PNG/WebP/GIF). Discarding.", url);
                return null;
            }

            await File.WriteAllBytesAsync(destFile, bytes);

            this.logger.Debug("Cached {0} artwork to {1}", type, destFile);
            return destFile;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to cache artwork from URL/path: {0}", url);
            return null;
        }
    }

    internal static bool IsValidImage(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12)
        {
            return false;
        }

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        // WebP: RIFF ???? WEBP
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        // GIF: GIF87a or GIF89a
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38 &&
            (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            return true;
        }

        return false;
    }

    internal string GetServarrApiKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.Contains("/api/v3/mediacover/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/mediacover/", StringComparison.OrdinalIgnoreCase))
        {
            if (this.arrRepository != null)
            {
                var connections = this.arrRepository.All()?.ToList();
                if (connections != null)
                {
                    var matched = connections.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(c.Url) &&
                        url.StartsWith(c.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(c.ApiKey));

                    return matched?.ApiKey;
                }
            }
        }

        return null;
    }

    internal void ApplyServarrAuthHeaders(HttpRequestMessage request, string url)
    {
        if (request == null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var apiKey = this.GetServarrApiKey(url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        }
    }

    private static string GuessArrType(string category, string name)
    {
        var cat = (category ?? string.Empty).ToLowerInvariant();
        if (cat.Contains("tv") || cat.Contains("sonarr") || cat.Contains("show") || cat.Contains("season") || cat.Contains("series") || cat.Contains("episode") || cat.Contains("anime"))
        {
            return "Sonarr";
        }

        if (cat.Contains("movie") || cat.Contains("radarr") || cat.Contains("film") || cat.Contains("cinema"))
        {
            return "Radarr";
        }

        if (cat.Contains("music") || cat.Contains("lidarr") || cat.Contains("album") || cat.Contains("audio") || cat.Contains("flac"))
        {
            return "Lidarr";
        }

        if (cat.Contains("book") || cat.Contains("readarr") || cat.Contains("ebook") || cat.Contains("audiobook"))
        {
            return "Readarr";
        }

        return "Unknown";
    }

    private static void DeleteLocalFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Suppress cleanup failure
        }
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
                var files = this.torrentFileRepository?.GetByTorrentId(message.Torrent.Id)?.ToList()
                    ?? this.torrentFileService?.GetFiles(message.Torrent.Id)?.ToList();

                if (files != null && files.Count > 0)
                {
                    var primaryMediaFile = files
                        .Where(f => IsMediaFile(f.Path))
                        .OrderByDescending(f => f.Size)
                        .FirstOrDefault();

                    if (primaryMediaFile != null)
                    {
                        var fullPath = !string.IsNullOrWhiteSpace(message.Torrent.SavePath) && !Path.IsPathRooted(primaryMediaFile.Path)
                            ? Path.Combine(message.Torrent.SavePath, primaryMediaFile.Path)
                            : primaryMediaFile.Path;

                        if (File.Exists(fullPath))
                        {
                            await this.EnrichTorrentAsync(message.Torrent, fullPath);
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(message.Torrent.SavePath) && File.Exists(message.Torrent.SavePath) && IsMediaFile(message.Torrent.SavePath))
                {
                    await this.EnrichTorrentAsync(message.Torrent, message.Torrent.SavePath);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inspect completed media for torrent {0}", message.Torrent.Name);
            }
        });
    }

    public void Handle(ArchiveExtractionCompletedEvent message)
    {
        if (message?.Torrent == null || string.IsNullOrWhiteSpace(message.DestinationDirectory))
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                if (Directory.Exists(message.DestinationDirectory))
                {
                    var extractedFiles = Directory.GetFiles(message.DestinationDirectory, "*.*", SearchOption.AllDirectories);
                    var primaryMedia = extractedFiles
                        .Where(IsMediaFile)
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.Length)
                        .FirstOrDefault();

                    if (primaryMedia != null && primaryMedia.Exists)
                    {
                        await this.EnrichTorrentAsync(message.Torrent, primaryMedia.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inspect extracted media for torrent {0}", message.Torrent.Name);
            }
        });
    }

    private static bool IsMediaFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && MediaExtensions.Contains(ext);
    }
}
