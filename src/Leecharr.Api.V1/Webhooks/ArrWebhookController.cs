// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Webhooks;

[V1ApiController("webhook")]
public class ArrWebhookController : Controller
{
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentMediaMetadataRepository mediaMetadataRepository;
    private readonly IArrConnectionRepository arrConnectionRepository;
    private readonly Logger logger;

    public ArrWebhookController(
        ITorrentRepository torrentRepository,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        IArrConnectionRepository arrConnectionRepository = null)
    {
        this.torrentRepository = torrentRepository;
        this.mediaMetadataRepository = mediaMetadataRepository;
        this.arrConnectionRepository = arrConnectionRepository;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    [HttpPost("arr")]
    public ActionResult<ArrWebhookResult> HandleArr([FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook("Arr", payload);
    }

    [HttpPost("sonarr")]
    public ActionResult<ArrWebhookResult> HandleSonarr([FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook("Sonarr", payload);
    }

    [HttpPost("radarr")]
    public ActionResult<ArrWebhookResult> HandleRadarr([FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook("Radarr", payload);
    }

    [HttpPost("lidarr")]
    public ActionResult<ArrWebhookResult> HandleLidarr([FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook("Lidarr", payload);
    }

    [HttpPost("readarr")]
    public ActionResult<ArrWebhookResult> HandleReadarr([FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook("Readarr", payload);
    }

    [HttpPost("{arrType}")]
    public ActionResult<ArrWebhookResult> HandleGeneric(string arrType, [FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook(arrType, payload);
    }

    [HttpPost]
    public ActionResult<ArrWebhookResult> HandleDefault([FromBody] ArrWebhookPayload payload)
    {
        return this.ProcessWebhook(null, payload);
    }

    private ActionResult<ArrWebhookResult> ProcessWebhook(string arrType, ArrWebhookPayload payload)
    {
        if (payload == null)
        {
            return this.BadRequest(new ArrWebhookResult
            {
                Success = false,
                Message = "Payload is required.",
            });
        }

        var eventType = payload.EventType ?? "Unknown";
        this.logger.Info("Received Arr webhook event '{0}' for type '{1}' from instance '{2}'", eventType, arrType ?? "Unknown", payload.InstanceName ?? "Unknown");

        if (string.Equals(eventType, "Test", StringComparison.OrdinalIgnoreCase))
        {
            return this.Ok(new ArrWebhookResult
            {
                Success = true,
                EventType = eventType,
                Message = "Webhook test received successfully.",
                Updated = false,
            });
        }

        var torrent = this.FindMatchingTorrent(payload);
        var updated = false;

        if (torrent != null)
        {
            if (this.IsImportEvent(eventType))
            {
                torrent.IsImported = true;
                torrent.ImportedAt = DateTime.UtcNow;

                var importPath = this.ExtractImportPath(payload);
                if (!string.IsNullOrWhiteSpace(importPath))
                {
                    torrent.ImportPath = importPath;
                }

                var resolvedArr = !string.IsNullOrWhiteSpace(arrType) && !string.Equals(arrType, "arr", StringComparison.OrdinalIgnoreCase)
                    ? arrType
                    : (payload.InstanceName ?? "Arr");
                torrent.ImportedByArr = resolvedArr;

                this.torrentRepository.Update(torrent);
                updated = true;
                this.logger.Info("Updated import state for torrent {0} (InfoHash: {1}) by {2}", torrent.Name, torrent.InfoHash, resolvedArr);
            }

            this.TryEnrichMetadata(torrent, arrType, payload);
        }
        else
        {
            this.logger.Warn(
                "Could not find matching torrent for Arr webhook event '{0}' (DownloadClientId: {1}, DownloadId: {2})",
                eventType,
                payload.DownloadClientId,
                payload.DownloadId);
        }

        return this.Ok(new ArrWebhookResult
        {
            Success = true,
            EventType = eventType,
            TorrentId = torrent?.Id,
            InfoHash = torrent?.InfoHash,
            Updated = updated,
            Message = torrent != null
                ? $"Webhook event '{eventType}' processed for torrent '{torrent.Name}'."
                : $"Webhook event '{eventType}' processed, but no matching torrent was found.",
        });
    }

    private Torrent FindMatchingTorrent(ArrWebhookPayload payload)
    {
        if (payload == null)
        {
            return null;
        }

        // 1. Try downloadClientId
        if (!string.IsNullOrWhiteSpace(payload.DownloadClientId))
        {
            var byHash = this.torrentRepository.GetByInfoHash(payload.DownloadClientId);
            if (byHash != null)
            {
                return byHash;
            }

            if (int.TryParse(payload.DownloadClientId, out var id))
            {
                var byId = this.torrentRepository.Get(id);
                if (byId != null)
                {
                    return byId;
                }
            }
        }

        // 2. Try downloadId
        if (!string.IsNullOrWhiteSpace(payload.DownloadId))
        {
            var byHash = this.torrentRepository.GetByInfoHash(payload.DownloadId);
            if (byHash != null)
            {
                return byHash;
            }

            if (int.TryParse(payload.DownloadId, out var id))
            {
                var byId = this.torrentRepository.Get(id);
                if (byId != null)
                {
                    return byId;
                }
            }
        }

        // 3. Try release downloadId
        if (!string.IsNullOrWhiteSpace(payload.Release?.DownloadId))
        {
            var byHash = this.torrentRepository.GetByInfoHash(payload.Release.DownloadId);
            if (byHash != null)
            {
                return byHash;
            }

            if (int.TryParse(payload.Release.DownloadId, out var id))
            {
                var byId = this.torrentRepository.Get(id);
                if (byId != null)
                {
                    return byId;
                }
            }
        }

        // 4. Scan all torrents for hash / name / path match
        var allTorrents = this.torrentRepository.All()?.ToList();
        if (allTorrents == null || allTorrents.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(payload.DownloadClientId))
        {
            var match = allTorrents.FirstOrDefault(t =>
                string.Equals(t.InfoHash, payload.DownloadClientId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, payload.DownloadClientId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.DownloadId))
        {
            var match = allTorrents.FirstOrDefault(t =>
                string.Equals(t.InfoHash, payload.DownloadId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, payload.DownloadId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        var releaseTitle = payload.Release?.ReleaseTitle;
        if (!string.IsNullOrWhiteSpace(releaseTitle))
        {
            var match = allTorrents.FirstOrDefault(t =>
                string.Equals(t.Name, releaseTitle, StringComparison.OrdinalIgnoreCase) ||
                releaseTitle.Contains(t.Name, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(releaseTitle, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        var sourcePath = payload.SourcePath ?? payload.EpisodeFile?.Path ?? payload.MovieFile?.Path;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var normalizedSource = sourcePath.Replace('\\', '/').TrimEnd('/');
            var match = allTorrents.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.SavePath) &&
                (normalizedSource.Contains(t.SavePath.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                 t.SavePath.Replace('\\', '/').TrimEnd('/').Contains(normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                 normalizedSource.EndsWith(t.Name, StringComparison.OrdinalIgnoreCase)));
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private bool IsImportEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        return string.Equals(eventType, "Import", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "Upgrade", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "Rename", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "DownloadFolderImported", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "EpisodeImport", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "MovieImport", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "EpisodeFileUpgrade", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "MovieFileUpgrade", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "SeriesRename", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "MovieRename", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "TrackImport", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "BookImport", StringComparison.OrdinalIgnoreCase);
    }

    private string ExtractImportPath(ArrWebhookPayload payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(payload.EpisodeFile?.Path))
        {
            return payload.EpisodeFile.Path;
        }

        if (!string.IsNullOrWhiteSpace(payload.MovieFile?.Path))
        {
            return payload.MovieFile.Path;
        }

        if (payload.RenamedFiles != null && payload.RenamedFiles.Count > 0)
        {
            var firstRenamed = payload.RenamedFiles.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Path));
            if (firstRenamed != null)
            {
                return firstRenamed.Path;
            }
        }

        if (payload.TrackFiles != null && payload.TrackFiles.Count > 0)
        {
            var firstTrack = payload.TrackFiles.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Path));
            if (firstTrack != null)
            {
                return firstTrack.Path;
            }
        }

        if (payload.BookFiles != null && payload.BookFiles.Count > 0)
        {
            var firstBook = payload.BookFiles.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Path));
            if (firstBook != null)
            {
                return firstBook.Path;
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.DestinationPath))
        {
            return payload.DestinationPath;
        }

        if (!string.IsNullOrWhiteSpace(payload.Path))
        {
            return payload.Path;
        }

        if (!string.IsNullOrWhiteSpace(payload.Series?.Path))
        {
            return payload.Series.Path;
        }

        if (!string.IsNullOrWhiteSpace(payload.Movie?.Path))
        {
            return payload.Movie.Path;
        }

        if (!string.IsNullOrWhiteSpace(payload.Artist?.Path))
        {
            return payload.Artist.Path;
        }

        if (!string.IsNullOrWhiteSpace(payload.Author?.Path))
        {
            return payload.Author.Path;
        }

        return null;
    }

    private void TryEnrichMetadata(Torrent torrent, string arrType, ArrWebhookPayload payload)
    {
        if (this.mediaMetadataRepository == null || torrent == null || payload == null)
        {
            return;
        }

        try
        {
            var existingMeta = this.mediaMetadataRepository.GetByTorrentId(torrent.Id);
            if (existingMeta == null)
            {
                var resolvedType = !string.IsNullOrWhiteSpace(arrType) && !string.Equals(arrType, "arr", StringComparison.OrdinalIgnoreCase)
                    ? arrType
                    : (payload.InstanceName ?? "Arr");

                var meta = new TorrentMediaMetadata
                {
                    TorrentId = torrent.Id,
                    ArrType = resolvedType,
                };

                if (payload.Series != null)
                {
                    meta.ArrType = "Sonarr";
                    meta.ArrMediaId = payload.Series.Id;
                    meta.Title = payload.Series.Title;
                    meta.Year = payload.Series.Year;
                    meta.TvdbId = payload.Series.TvdbId > 0 ? payload.Series.TvdbId.ToString() : null;
                    meta.ImdbId = payload.Series.ImdbId;
                }
                else if (payload.Movie != null)
                {
                    meta.ArrType = "Radarr";
                    meta.ArrMediaId = payload.Movie.Id;
                    meta.Title = payload.Movie.Title;
                    meta.Year = payload.Movie.Year;
                    meta.TmdbId = payload.Movie.TmdbId > 0 ? payload.Movie.TmdbId.ToString() : null;
                    meta.ImdbId = payload.Movie.ImdbId;
                }
                else if (payload.Artist != null)
                {
                    meta.ArrType = "Lidarr";
                    meta.ArrMediaId = payload.Artist.Id;
                    meta.Title = payload.Artist.Name;
                }
                else if (payload.Author != null)
                {
                    meta.ArrType = "Readarr";
                    meta.ArrMediaId = payload.Author.Id;
                    meta.Title = payload.Author.Name;
                }

                if (!string.IsNullOrWhiteSpace(meta.Title))
                {
                    this.mediaMetadataRepository.Insert(meta);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to enrich metadata from webhook payload for torrent {0}", torrent.Id);
        }
    }
}
