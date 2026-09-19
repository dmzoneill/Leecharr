// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Webhooks;

[V1ApiController("webhook")]
public class ArrWebhookController : Controller
{
    private readonly ITorrentRepository torrentRepository;
    private readonly ITorrentMediaMetadataRepository mediaMetadataRepository;
    private readonly IArrConnectionRepository arrConnectionRepository;
    private readonly ITorrentService torrentService;
    private readonly IProwlarrSyncService prowlarrSyncService;
    private readonly Logger logger;

    public ArrWebhookController(
        ITorrentRepository torrentRepository,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        IArrConnectionRepository arrConnectionRepository = null,
        ITorrentService torrentService = null,
        IProwlarrSyncService prowlarrSyncService = null)
    {
        this.torrentRepository = torrentRepository;
        this.mediaMetadataRepository = mediaMetadataRepository;
        this.arrConnectionRepository = arrConnectionRepository;
        this.torrentService = torrentService;
        this.prowlarrSyncService = prowlarrSyncService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    [HttpPost("arr")]
    public async Task<ActionResult<ArrWebhookResult>> HandleArr([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync("Arr", payload);
    }

    [HttpPost("sonarr")]
    public async Task<ActionResult<ArrWebhookResult>> HandleSonarr([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync("Sonarr", payload);
    }

    [HttpPost("radarr")]
    public async Task<ActionResult<ArrWebhookResult>> HandleRadarr([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync("Radarr", payload);
    }

    [HttpPost("lidarr")]
    public async Task<ActionResult<ArrWebhookResult>> HandleLidarr([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync("Lidarr", payload);
    }

    [HttpPost("readarr")]
    public async Task<ActionResult<ArrWebhookResult>> HandleReadarr([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync("Readarr", payload);
    }

    [HttpPost("prowlarr")]
    public async Task<ActionResult<ArrWebhookResult>> HandleProwlarr([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync("Prowlarr", payload);
    }

    [HttpPost("{arrType}")]
    public async Task<ActionResult<ArrWebhookResult>> HandleGeneric(string arrType, [FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync(arrType, payload);
    }

    [HttpPost]
    public async Task<ActionResult<ArrWebhookResult>> HandleDefault([FromBody] ArrWebhookPayload payload)
    {
        return await this.ProcessWebhookAsync(null, payload);
    }

    private async Task<ActionResult<ArrWebhookResult>> ProcessWebhookAsync(string arrType, ArrWebhookPayload payload)
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

        if (string.Equals(arrType, "Prowlarr", StringComparison.OrdinalIgnoreCase) || IsProwlarrIndexerEvent(eventType))
        {
            var syncedCount = 0;
            if (this.prowlarrSyncService != null)
            {
                syncedCount = await this.prowlarrSyncService.SyncAllAsync();
            }

            return this.Ok(new ArrWebhookResult
            {
                Success = true,
                EventType = eventType,
                Updated = true,
                Message = $"Prowlarr indexer sync triggered successfully ({syncedCount} indexers synced).",
            });
        }

        var torrent = this.FindMatchingTorrent(payload);
        var updated = false;

        if (torrent != null)
        {
            var torrentNeedsRepoUpdate = false;

            if (string.IsNullOrWhiteSpace(torrent.Category) || string.Equals(torrent.Category, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedCategory = string.Equals(arrType, "Sonarr", StringComparison.OrdinalIgnoreCase) || payload.Series != null
                    ? "tv-sonarr"
                    : (string.Equals(arrType, "Radarr", StringComparison.OrdinalIgnoreCase) || payload.Movie != null
                        ? "radarr"
                        : (string.Equals(arrType, "Lidarr", StringComparison.OrdinalIgnoreCase) || payload.Artist != null
                            ? "music"
                            : (string.Equals(arrType, "Readarr", StringComparison.OrdinalIgnoreCase) || payload.Author != null
                                ? "books"
                                : null)));

                if (!string.IsNullOrWhiteSpace(resolvedCategory))
                {
                    if (this.torrentService != null)
                    {
                        await this.torrentService.SetCategoryAsync(torrent.Id, resolvedCategory);
                        torrent = this.GetTorrentById(torrent.Id) ?? torrent;
                    }
                    else
                    {
                        torrent.Category = resolvedCategory;
                        torrentNeedsRepoUpdate = true;
                    }

                    updated = true;
                    this.logger.Info("Assigned category '{0}' to torrent {1} from webhook", resolvedCategory, torrent.Name);
                }
            }

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

                updated = true;
                torrentNeedsRepoUpdate = true;
                this.logger.Info("Updated import state for torrent {0} (InfoHash: {1}) by {2}", torrent.Name, torrent.InfoHash, resolvedArr);
            }
            else if (string.Equals(eventType, "ImportFailed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventType, "DownloadFolderImportFailed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventType, "EpisodeImportFailed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventType, "MovieImportFailed", StringComparison.OrdinalIgnoreCase))
            {
                torrent.IsImported = false;
                updated = true;
                torrentNeedsRepoUpdate = true;
                this.logger.Warn("Import failed for torrent {0} (InfoHash: {1})", torrent.Name, torrent.InfoHash);
            }
            else if (string.Equals(eventType, "Grab", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventType, "ReleaseGrabbed", StringComparison.OrdinalIgnoreCase))
            {
                this.logger.Info("Grabbed event received for torrent {0} (InfoHash: {1})", torrent.Name, torrent.InfoHash);
            }
            else if (this.IsDeleteEvent(eventType))
            {
                torrent.IsImported = false;
                torrent.ImportPath = null;
                updated = true;
                torrentNeedsRepoUpdate = true;
                this.logger.Info("Media/file deleted in Arr for torrent {0} (InfoHash: {1})", torrent.Name, torrent.InfoHash);
            }
            else if (string.Equals(eventType, "DownloadFailed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventType, "DownloadWarning", StringComparison.OrdinalIgnoreCase))
            {
                this.logger.Warn("Download failed/warning event received for torrent {0} (InfoHash: {1})", torrent.Name, torrent.InfoHash);
            }

            if (torrentNeedsRepoUpdate)
            {
                this.torrentRepository.Update(torrent);
            }

            if (!this.IsDeleteEvent(eventType))
            {
                this.TryEnrichMetadata(torrent, arrType, payload);
            }
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

    private static bool IsProwlarrIndexerEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        return string.Equals(eventType, "IndexerSync", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "IndexerUpdated", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "IndexerDeleted", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "IndexerAdded", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "Sync", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "SyncAll", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "IndexerStatusChanged", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCandidateInfoHash(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var clean = candidate.Trim();

        if (clean.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) || clean.Contains("xt=", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var magnetUri = clean;
                if (!magnetUri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var magIdx = magnetUri.IndexOf("magnet:?", StringComparison.OrdinalIgnoreCase);
                    if (magIdx >= 0)
                    {
                        magnetUri = magnetUri.Substring(magIdx);
                    }
                }

                var parsed = MagnetLinkParser.Parse(magnetUri);
                if (!string.IsNullOrWhiteSpace(parsed.InfoHash))
                {
                    return parsed.InfoHash.ToLowerInvariant();
                }
            }
            catch
            {
                var xtIdx = clean.IndexOf("xt=", StringComparison.OrdinalIgnoreCase);
                if (xtIdx >= 0)
                {
                    var xtVal = clean.Substring(xtIdx + 3);
                    var ampIdx = xtVal.IndexOf('&');
                    if (ampIdx >= 0)
                    {
                        xtVal = xtVal.Substring(0, ampIdx);
                    }

                    clean = xtVal.Trim();
                }
            }
        }

        if (clean.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("urn:btih:".Length).Trim();
        }
        else if (clean.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("urn:btmh:".Length).Trim();
        }

        var unpadded = clean.TrimEnd('=');
        if (unpadded.Length is 32 or 52 or 55 or 56)
        {
            clean = unpadded;
        }

        var normalized = MagnetLinkParser.NormalizeInfoHash(clean);
        return string.IsNullOrWhiteSpace(normalized) ? clean.ToLowerInvariant() : normalized.ToLowerInvariant();
    }

    private Torrent GetTorrentByInfoHash(string hash)
    {
        return this.torrentService != null
            ? this.torrentService.GetByInfoHash(hash)
            : this.torrentRepository.GetByInfoHash(hash);
    }

    private Torrent GetTorrentById(int id)
    {
        return this.torrentService != null
            ? this.torrentService.Get(id)
            : this.torrentRepository.Get(id);
    }

    private IEnumerable<Torrent> GetAllTorrents()
    {
        return this.torrentService != null
            ? this.torrentService.GetAll()
            : this.torrentRepository.All();
    }

    private static readonly HashSet<string> CommonReleaseTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "1080p", "720p", "2160p", "4k", "remux", "web-dl", "webrip", "bluray", "hdtv",
        "x264", "x265", "hevc", "repack", "proper", "hdr", "aac", "dts", "flac",
        "truehd", "atmos", "uhd", "sdr", "subpack", "raw",
    };

    private Torrent FindMatchingTorrent(ArrWebhookPayload payload)
    {
        if (payload == null)
        {
            return null;
        }

        var candidateStrings = new List<string>
        {
            payload.DownloadClientId,
            payload.DownloadId,
            payload.Release?.DownloadId,
            payload.Release?.DownloadUrl,
            payload.DownloadUrl,
        };

        if (payload.Data != null)
        {
            foreach (var kvp in payload.Data)
            {
                if (string.Equals(kvp.Key, "downloadId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "infoHash", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "downloadClientId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "hash", StringComparison.OrdinalIgnoreCase))
                {
                    string val = null;
                    if (kvp.Value is JsonElement jsonElement)
                    {
                        val = jsonElement.ValueKind == JsonValueKind.String
                            ? jsonElement.GetString()
                            : jsonElement.ToString();
                    }
                    else
                    {
                        val = kvp.Value?.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        candidateStrings.Add(val);
                    }
                }
            }
        }

        foreach (var candidate in candidateStrings)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalizedHash = NormalizeCandidateInfoHash(candidate);
            if (!string.IsNullOrWhiteSpace(normalizedHash))
            {
                var byNormalized = this.GetTorrentByInfoHash(normalizedHash);
                if (byNormalized != null)
                {
                    return byNormalized;
                }
            }

            var byRaw = this.GetTorrentByInfoHash(candidate);
            if (byRaw != null)
            {
                return byRaw;
            }

            if (int.TryParse(candidate, out var id))
            {
                var byId = this.GetTorrentById(id);
                if (byId != null)
                {
                    return byId;
                }
            }
        }

        var allTorrents = this.GetAllTorrents()?.ToList();
        if (allTorrents == null || allTorrents.Count == 0)
        {
            return null;
        }

        foreach (var candidate in candidateStrings)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalizedHash = NormalizeCandidateInfoHash(candidate);
            var match = allTorrents.FirstOrDefault(t =>
                (!string.IsNullOrWhiteSpace(normalizedHash) && string.Equals(t.InfoHash, normalizedHash, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(t.InfoHash, candidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        var candidateTitles = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.Release?.ReleaseTitle))
        {
            candidateTitles.Add(payload.Release.ReleaseTitle);
        }

        if (!string.IsNullOrWhiteSpace(payload.EpisodeFile?.SceneName))
        {
            candidateTitles.Add(payload.EpisodeFile.SceneName);
        }

        if (!string.IsNullOrWhiteSpace(payload.MovieFile?.SceneName))
        {
            candidateTitles.Add(payload.MovieFile.SceneName);
        }

        foreach (var title in candidateTitles)
        {
            var match = allTorrents.FirstOrDefault(t => IsTitleMatch(t.Name, title));
            if (match != null)
            {
                return match;
            }
        }

        var candidatePaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.SourcePath))
        {
            candidatePaths.Add(payload.SourcePath);
        }

        if (!string.IsNullOrWhiteSpace(payload.EpisodeFile?.Path))
        {
            candidatePaths.Add(payload.EpisodeFile.Path);
        }

        if (!string.IsNullOrWhiteSpace(payload.MovieFile?.Path))
        {
            candidatePaths.Add(payload.MovieFile.Path);
        }

        if (payload.RenamedFiles != null)
        {
            foreach (var rf in payload.RenamedFiles)
            {
                if (!string.IsNullOrWhiteSpace(rf.PreviousPath))
                {
                    candidatePaths.Add(rf.PreviousPath);
                }

                if (!string.IsNullOrWhiteSpace(rf.Path))
                {
                    candidatePaths.Add(rf.Path);
                }
            }
        }

        if (payload.DeletedFiles != null)
        {
            foreach (var df in payload.DeletedFiles)
            {
                if (!string.IsNullOrWhiteSpace(df.Path))
                {
                    candidatePaths.Add(df.Path);
                }
            }
        }

        foreach (var path in candidatePaths)
        {
            var normalizedSource = path.Replace('\\', '/').TrimEnd('/');
            var match = allTorrents.FirstOrDefault(t => IsSourcePathMatch(normalizedSource, t));
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static string GetNormalizedTorrentRoot(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            return null;
        }

        var normalizedSavePath = torrent.SavePath.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(torrent.Name))
        {
            return normalizedSavePath;
        }

        var normalizedName = torrent.Name.Trim().TrimStart('/');
        if (normalizedSavePath.EndsWith("/" + normalizedName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedSavePath, normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedSavePath;
        }

        return normalizedSavePath + "/" + normalizedName;
    }

    private static bool IsSourcePathMatch(string normalizedSource, Torrent torrent)
    {
        if (string.IsNullOrWhiteSpace(normalizedSource) || torrent == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(torrent.ImportPath))
        {
            var normalizedImport = torrent.ImportPath.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(normalizedImport, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.StartsWith(normalizedImport + "/", StringComparison.OrdinalIgnoreCase) ||
                normalizedImport.StartsWith(normalizedSource + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(torrent.Name))
        {
            var cleanName = torrent.Name.Trim();
            if (string.Equals(normalizedSource, cleanName, StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.EndsWith("/" + cleanName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var nameWithoutExt = Path.GetFileNameWithoutExtension(cleanName);
            if (!string.IsNullOrWhiteSpace(nameWithoutExt) && nameWithoutExt.Length >= 4)
            {
                var sourceFileName = Path.GetFileName(normalizedSource);
                var sourceNoExt = Path.GetFileNameWithoutExtension(normalizedSource);
                if (string.Equals(sourceNoExt, nameWithoutExt, StringComparison.OrdinalIgnoreCase) &&
                    (normalizedSource.EndsWith("/" + sourceFileName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(normalizedSource, sourceFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        var torrentRoot = GetNormalizedTorrentRoot(torrent);
        if (!string.IsNullOrWhiteSpace(torrentRoot))
        {
            if (string.Equals(normalizedSource, torrentRoot, StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.StartsWith(torrentRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTitleMatch(string torrentName, string releaseTitle)
    {
        if (string.IsNullOrWhiteSpace(torrentName) || string.IsNullOrWhiteSpace(releaseTitle))
        {
            return false;
        }

        var cleanTorrent = torrentName.Trim();
        var cleanRelease = releaseTitle.Trim();

        if (string.Equals(cleanTorrent, cleanRelease, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var torrentNoExt = Path.GetFileNameWithoutExtension(cleanTorrent);
        var releaseNoExt = Path.GetFileNameWithoutExtension(cleanRelease);
        if (!string.IsNullOrWhiteSpace(torrentNoExt) && !string.IsNullOrWhiteSpace(releaseNoExt))
        {
            if (string.Equals(torrentNoExt, releaseNoExt, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var normTorrent = NormalizeTitleDelimiters(torrentNoExt ?? cleanTorrent);
        var normRelease = NormalizeTitleDelimiters(releaseNoExt ?? cleanRelease);
        if (string.Equals(normTorrent, normRelease, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cleanTorrent.Length >= 8 && !CommonReleaseTags.Contains(cleanTorrent))
        {
            if (IsWordBoundaryMatch(cleanRelease, cleanTorrent))
            {
                return true;
            }
        }

        if (cleanRelease.Length >= 8 && !CommonReleaseTags.Contains(cleanRelease))
        {
            if (IsWordBoundaryMatch(cleanTorrent, cleanRelease))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTitleDelimiters(string title)
    {
        return string.Join(" ", title.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsWordBoundaryMatch(string fullText, string phrase)
    {
        var pattern = @"(^|[\s._\-\[\]\(\)])" + Regex.Escape(phrase) + @"([\s._\-\[\]\(\)]|$)";
        return Regex.IsMatch(fullText, pattern, RegexOptions.IgnoreCase);
    }

    private bool IsDeleteEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        return string.Equals(eventType, "EpisodeFileDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "MovieFileDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "SeriesDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "MovieDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "TrackFileDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "BookFileDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "ArtistDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "AuthorDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "FileDelete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "Delete", StringComparison.OrdinalIgnoreCase);
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
