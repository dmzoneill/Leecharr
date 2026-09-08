// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
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

    void DeleteMediaCache(int torrentId);

    Task<string> CacheArtworkAsync(string url, int torrentId, string type);
}

public class MediaEnrichmentService : IMediaEnrichmentService
{
    private readonly ITorrentMediaMetadataRepository repository;
    private readonly IMediaContainerInspector inspector;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IEventAggregator eventAggregator;
    private readonly IMediaMetadataService mediaMetadataService;
    private readonly IArrConnectionRepository arrRepository;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger;

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
        IHttpTransportEngine transportEngine = null)
    {
        this.repository = repository;
        this.inspector = inspector;
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
        this.eventAggregator = eventAggregator;
        this.mediaMetadataService = mediaMetadataService;
        this.arrRepository = arrRepository;
        this.safeHttpClientService = safeHttpClientService ?? (httpClient != null ? new SafeHttpClientService(httpClient) : (transportEngine != null ? new SafeHttpClientService(transportEngine) : new SafeHttpClientService()));
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null)
    {
        if (torrent == null)
        {
            return null;
        }

        this.logger.Debug("Enriching metadata for torrent: {0}", torrent.Name);

        var existing = torrent.Id > 0 ? this.repository.GetByTorrentId(torrent.Id) : null;
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
                var dynamicMeta = await this.mediaMetadataService.GetMetadataAsync(torrent.Name, torrent.Category, null, torrent.InfoHash);
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

                    if (!string.IsNullOrEmpty(dynamicMeta.BannerUrl))
                    {
                        metadata.BannerUrl = dynamicMeta.BannerUrl;
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

                    if (dynamicMeta.ArrMediaId > 0)
                    {
                        metadata.ArrMediaId = dynamicMeta.ArrMediaId;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.MusicBrainzId))
                    {
                        metadata.MusicBrainzId = dynamicMeta.MusicBrainzId;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.ArtistName))
                    {
                        metadata.ArtistName = dynamicMeta.ArtistName;
                    }

                    if (!string.IsNullOrEmpty(dynamicMeta.AlbumTitle))
                    {
                        metadata.AlbumTitle = dynamicMeta.AlbumTitle;
                    }

                    if (dynamicMeta.Cast != null && dynamicMeta.Cast.Count > 0)
                    {
                        metadata.Cast = string.Join(", ", dynamicMeta.Cast);
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
            var guessed = this.inspector.Inspect(new MemoryStream(new byte[8]), torrent.Name);
            metadata.Title = torrent.Name;
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

        if (torrent.Id > 0)
        {
            if (existing == null)
            {
                this.repository.Insert(metadata);
            }
            else
            {
                this.repository.Update(metadata);
            }

            this.eventAggregator.PublishEvent(new MediaEnrichedEvent { TorrentId = torrent.Id, Metadata = metadata });
        }

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
        this.CleanupTorrentCache(torrentId);

        var metadata = this.repository.GetByTorrentId(torrentId);
        if (metadata != null)
        {
            // Prune artwork if configured
            if (this.configService.AutoPruneRemovedArtwork)
            {
                DeleteLocalFile(metadata.PosterLocalPath);
                DeleteLocalFile(metadata.BackdropLocalPath);
            }

            this.repository.DeleteByTorrentId(torrentId);
        }
    }

    public void DeleteMediaCache(int torrentId)
    {
        this.CleanupTorrentCache(torrentId);
    }

    public void CleanupTorrentCache(int torrentId)
    {
        try
        {
            var mediaCacheBase = Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache");
            if (Directory.Exists(mediaCacheBase))
            {
                var cacheDir = Path.Combine(mediaCacheBase, torrentId.ToString());
                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, recursive: true);
                    this.logger.Debug("Cleaned up media cache directory for torrent {0}", torrentId);
                }

                // Prune hash-named cache directories associated with that torrent or pre-enrichment lookups
                var metadata = this.repository?.GetByTorrentId(torrentId);
                if (metadata != null)
                {
                    if (!string.IsNullOrWhiteSpace(metadata.PosterUrl))
                    {
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.PosterUrl)))[..16].ToLowerInvariant();
                        var hashDir = Path.Combine(mediaCacheBase, hash);
                        if (Directory.Exists(hashDir))
                        {
                            Directory.Delete(hashDir, recursive: true);
                            this.logger.Debug("Pruned pre-enrichment poster cache directory {0} for torrent {1}", hash, torrentId);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.BackdropUrl))
                    {
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.BackdropUrl)))[..16].ToLowerInvariant();
                        var hashDir = Path.Combine(mediaCacheBase, hash);
                        if (Directory.Exists(hashDir))
                        {
                            Directory.Delete(hashDir, recursive: true);
                            this.logger.Debug("Pruned pre-enrichment backdrop cache directory {0} for torrent {1}", hash, torrentId);
                        }
                    }

                    PruneCacheDirForFilePath(mediaCacheBase, metadata.PosterLocalPath);
                    PruneCacheDirForFilePath(mediaCacheBase, metadata.BackdropLocalPath);
                }
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
            var folderKey = torrentId > 0
                ? torrentId.ToString()
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
            var cacheDir = Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache", folderKey);
            Directory.CreateDirectory(cacheDir);

            // Handle local file path
            if (File.Exists(url) || Path.IsPathRooted(url))
            {
                if (File.Exists(url))
                {
                    var localBytes = await File.ReadAllBytesAsync(url);
                    if (localBytes == null || localBytes.Length > 10 * 1024 * 1024 || !IsValidImage(localBytes))
                    {
                        this.logger.Warn("Local artwork file is invalid, not an image, or exceeds size limit: {0}", url);
                        return null;
                    }

                    var ext = Path.GetExtension(url);
                    if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                    {
                        ext = ".jpg";
                    }

                    var localFile = Path.Combine(cacheDir, $"{type}{ext}");
                    await File.WriteAllBytesAsync(localFile, localBytes);
                    this.logger.Debug("Copied validated local {0} artwork from {1} to {2}", type, url, localFile);
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

            byte[] bytes = null;
            try
            {
                bytes = await this.safeHttpClientService.DownloadBytesAsync(uri, maxSizeBytes: 10 * 1024 * 1024, customHeaders: customHeaders);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed downloading artwork from {0}", url);
                return null;
            }

            if (bytes == null || !IsValidImage(bytes))
            {
                this.logger.Warn("Downloaded artwork from {0} has invalid image magic bytes or is empty. Discarding.", url);
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
        if (bytes == null || bytes.Length < 4)
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
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        // GIF: GIF87a or GIF89a
        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38 &&
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

    private static void PruneCacheDirForFilePath(string mediaCacheBase, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(mediaCacheBase))
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            var fullCacheBase = Path.GetFullPath(mediaCacheBase);
            if (dir != null &&
                dir.StartsWith(fullCacheBase, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dir, fullCacheBase, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Suppress cleanup failure
        }
    }
}
