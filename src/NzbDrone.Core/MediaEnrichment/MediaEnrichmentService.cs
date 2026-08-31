// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
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

    void DeleteMetadata(int torrentId);
}

public class MediaEnrichmentService : IMediaEnrichmentService
{
    private readonly ITorrentMediaMetadataRepository repository;
    private readonly IMediaContainerInspector inspector;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IEventAggregator eventAggregator;
    private readonly HttpClient httpClient;
    private readonly Logger logger;

    public MediaEnrichmentService(
        ITorrentMediaMetadataRepository repository,
        IMediaContainerInspector inspector,
        IConfigService configService,
        IAppFolderInfo appFolderInfo,
        IEventAggregator eventAggregator)
    {
        this.repository = repository;
        this.inspector = inspector;
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
        this.eventAggregator = eventAggregator;
        this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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

        // 2. Parse title and heuristics from Torrent Name if empty
        if (string.IsNullOrEmpty(metadata.Title))
        {
            var guessed = this.inspector.Inspect(new MemoryStream(new byte[8]), torrent.Name);
            metadata.Title = torrent.Name;
            metadata.ArrType = GuessArrType(torrent.Category, torrent.Name);
        }

        // 3. Cache remote poster if URL present and local path not yet downloaded
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
            }

            this.repository.DeleteByTorrentId(torrentId);
        }
    }

    private static string GuessArrType(string category, string name)
    {
        var cat = (category ?? string.Empty).ToLowerInvariant();
        if (cat.Contains("tv") || cat.Contains("sonarr") || cat.Contains("show") || cat.Contains("season"))
        {
            return "Sonarr";
        }

        if (cat.Contains("movie") || cat.Contains("radarr") || cat.Contains("film"))
        {
            return "Radarr";
        }

        if (cat.Contains("music") || cat.Contains("lidarr") || cat.Contains("album"))
        {
            return "Lidarr";
        }

        if (cat.Contains("book") || cat.Contains("readarr"))
        {
            return "Readarr";
        }

        return "Unknown";
    }

    private async Task<string> CacheArtworkAsync(string url, int torrentId, string type)
    {
        try
        {
            var cacheDir = Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache", torrentId.ToString());
            Directory.CreateDirectory(cacheDir);

            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5)
            {
                ext = ".jpg";
            }

            var localFile = Path.Combine(cacheDir, $"{type}{ext}");
            var bytes = await this.httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localFile, bytes);

            this.logger.Debug("Cached {0} artwork to {1}", type, localFile);
            return localFile;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to cache artwork from URL: {0}", url);
            return null;
        }
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
}
