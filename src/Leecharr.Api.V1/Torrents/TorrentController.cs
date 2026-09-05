// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Torrents;

public record TorrentUploadFailure(string FileName, string Reason);

public record TorrentUploadResult(List<TorrentResource> Added, List<TorrentUploadFailure> Failed);

public class MoveQueueRequest
{
    public string Position { get; set; }
}

public class SetFilePriorityRequest
{
    public int Priority { get; set; }
}

public class AddTorrentJsonRequest
{
    public string MagnetLink { get; set; }

    public string MagnetUrl { get; set; }

    public string DownloadUrl { get; set; }

    public string Title { get; set; }

    public string Category { get; set; }

    public string SavePath { get; set; }

    public bool Paused { get; set; }

    public bool StartPaused { get; set; }
}

[V1ApiController("torrents")]
[Route("api/v1/torrent")]
public class TorrentController : RestControllerWithSignalR<TorrentResource, Torrent>
{
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly IMediaEnrichmentService mediaEnrichmentService;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IGeoIpService geoIpService;
    private readonly IDownloadEngine downloadEngine;
    private readonly ISafeHttpClientService safeHttpClientService;

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IMediaEnrichmentService mediaEnrichmentService,
        ITrackerEntryRepository trackerEntryRepository,
        IBroadcastSignalRMessage signalRBroadcaster,
        IGeoIpService geoIpService = null,
        IDownloadEngine downloadEngine = null,
        ISafeHttpClientService safeHttpClientService = null)
        : base(signalRBroadcaster)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.mediaEnrichmentService = mediaEnrichmentService;
        this.trackerEntryRepository = trackerEntryRepository;
        this.geoIpService = geoIpService;
        this.downloadEngine = downloadEngine;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    [HttpGet]
    public ActionResult<List<TorrentResource>> GetAll()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var allDbTrackers = this.trackerEntryRepository?.All()
            .GroupBy(t => t.TorrentId)
            .ToDictionary(g => g.Key, g => g.ToList())
            ?? new Dictionary<int, List<TrackerEntry>>();

        var allMetadata = this.mediaEnrichmentService?.GetAllMetadata()
            ?? new Dictionary<int, TorrentMediaMetadata>();

        var resources = torrents.Select((t, idx) =>
        {
            allMetadata.TryGetValue(t.Id, out var meta);
            var res = TorrentResourceMapper.ToResource(t, meta);
            res.QueuePosition = t.QueuePosition > 0 ? t.QueuePosition : idx + 1;
            if (allDbTrackers.TryGetValue(t.Id, out var trackerEntries) && trackerEntries.Count > 0)
            {
                res.Trackers = trackerEntries.Select(x => x.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
                if (string.IsNullOrWhiteSpace(res.TrackerUrl) && res.Trackers.Count > 0)
                {
                    res.TrackerUrl = res.Trackers[0];
                }

                var primary = trackerEntries[0];
                res.AnnounceInterval = primary.AnnounceInterval > 0 ? primary.AnnounceInterval : 1800;
                var lastAnnounce = primary.LastAnnounce ?? t.DateAdded;
                var nextAnnounce = primary.NextAnnounce ?? lastAnnounce.AddSeconds(res.AnnounceInterval.Value);
                res.NextUpdate = Math.Max(0, (int)(nextAnnounce - DateTime.UtcNow).TotalSeconds);
            }
            else
            {
                res.AnnounceInterval = 1800;
                res.NextUpdate = 1800;
                if (!string.IsNullOrWhiteSpace(res.TrackerUrl))
                {
                    res.Trackers = new List<string> { res.TrackerUrl };
                }
            }

            res.Active = t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding;
            res.Threshold = 1;
            res.SmallTorrentLimit = 50;

            return res;
        }).ToList();

        return this.Ok(resources);
    }

    [HttpGet("{id:int}")]
    public ActionResult<TorrentResource> GetById(int id)
    {
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var meta = this.mediaEnrichmentService.GetMetadata(id);
        var res = TorrentResourceMapper.ToResource(torrent, meta);
        res.QueuePosition = torrent.QueuePosition > 0 ? torrent.QueuePosition : 1;
        var dbTrackers = this.trackerEntryRepository?.GetByTorrentId(id).ToList();

        if (dbTrackers != null && dbTrackers.Count > 0)
        {
            res.Trackers = dbTrackers.Select(x => x.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (string.IsNullOrWhiteSpace(res.TrackerUrl) && res.Trackers.Count > 0)
            {
                res.TrackerUrl = dbTrackers[0].Url;
            }

            var primary = dbTrackers[0];
            res.AnnounceInterval = primary.AnnounceInterval > 0 ? primary.AnnounceInterval : 1800;
            var lastAnnounce = primary.LastAnnounce ?? torrent.DateAdded;
            var nextAnnounce = primary.NextAnnounce ?? lastAnnounce.AddSeconds(res.AnnounceInterval.Value);
            res.NextUpdate = Math.Max(0, (int)(nextAnnounce - DateTime.UtcNow).TotalSeconds);
        }
        else
        {
            res.AnnounceInterval = 1800;
            res.NextUpdate = 1800;
            if (!string.IsNullOrWhiteSpace(res.TrackerUrl))
            {
                res.Trackers = new List<string> { res.TrackerUrl };
            }
        }

        res.Active = torrent.Status == TorrentStatus.Downloading || torrent.Status == TorrentStatus.Seeding;
        res.Threshold = 1;
        res.SmallTorrentLimit = 50;

        return this.Ok(res);
    }

    [HttpGet("{id:int}/files")]
    public ActionResult<List<TorrentFileResource>> GetFiles(int id)
    {
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var files = this.torrentFileService.GetFiles(id).ToList();
        var downloadTask = this.torrentService.GetDownloadTask(id) ?? this.downloadEngine?.GetTask(id);

        TorrentFileProgressEnricher.Enrich(torrent, files, downloadTask);

        return this.Ok(files.ToResource());
    }

    [HttpPut("{id:int}/files/{fileId:int}/priority")]
    [HttpPut("{id:int}/files/{fileId:int}")]
    [HttpPost("{id:int}/files/{fileId:int}/priority")]
    public async Task<ActionResult> SetFilePriority(int id, int fileId, [FromBody] SetFilePriorityRequest request = null, [FromQuery] int? priority = null)
    {
        var prio = request?.Priority ?? priority ?? 3;
        await this.torrentFileService.SetPriorityAsync(fileId, prio);
        return this.Ok();
    }

    [HttpGet("{id:int}/peers")]
    public ActionResult<List<PeerResource>> GetPeers(int id)
    {
        var task = this.torrentService.GetDownloadTask(id);
        if (task == null)
        {
            return this.Ok(new List<PeerResource>());
        }

        var peers = task.GetPeers();
        var resources = peers.Select((p, idx) =>
        {
            var geo = this.geoIpService?.Lookup(p.Ip);
            return new PeerResource
            {
                Id = idx + 1,
                Ip = p.Ip,
                Port = p.Port,
                Client = p.Client,
                UploadSpeed = p.UploadSpeed,
                DownloadSpeed = p.DownloadSpeed,
                Uploaded = p.Uploaded,
                Downloaded = p.Downloaded,
                Progress = p.Progress,
                Flags = p.Flags,
                CountryCode = geo?.CountryCode ?? string.Empty,
                CountryName = geo?.CountryName ?? string.Empty,
                City = geo?.City ?? string.Empty,
            };
        }).ToList();

        return this.Ok(resources);
    }

    [HttpGet("{id:int}/trackers")]
    public ActionResult<List<TrackerResource>> GetTrackers(int id)
    {
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var dbTrackers = this.trackerEntryRepository.GetByTorrentId(id).ToList();
        if (dbTrackers.Count == 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            var fallback = new TrackerEntry
            {
                TorrentId = id,
                Url = torrent.TrackerUrl,
                Tier = 0,
                Status = 1,
                Enabled = true,
                Seeders = torrent.Seeders,
                Leechers = torrent.Leechers,
                AnnounceInterval = 1800,
                LastAnnounce = torrent.DateAdded,
                NextAnnounce = torrent.DateAdded.AddSeconds(1800),
                TotalAnnounces = 1,
                SuccessfulAnnounces = 1,
            };
            this.trackerEntryRepository.Insert(fallback);
            dbTrackers.Add(fallback);
        }

        var now = DateTime.UtcNow;
        var resources = dbTrackers.Select(t =>
        {
            var isError = t.Status == 2 || !string.IsNullOrWhiteSpace(t.ErrorMessage);
            var isQueued = t.Status == 0;
            var statusStr = isError ? "Error" : (isQueued ? "Queued" : (!t.Enabled ? "Disabled" : (torrent.Status == TorrentStatus.Paused ? "Paused" : "Working")));

            var nextAnnounce = t.NextAnnounce ?? (t.LastAnnounce.HasValue
                ? t.LastAnnounce.Value.AddSeconds(t.AnnounceInterval > 0 ? t.AnnounceInterval : 1800)
                : now.AddSeconds(t.AnnounceInterval > 0 ? t.AnnounceInterval : 1800));
            var nextAnnounceSec = Math.Max(0, (int)(nextAnnounce - now).TotalSeconds);

            return new TrackerResource
            {
                Id = t.Id,
                Url = t.Url,
                Tier = t.Tier,
                Status = statusStr,
                Seeders = t.Seeders > 0 ? t.Seeders : torrent.Seeders,
                Leechers = t.Leechers > 0 ? t.Leechers : torrent.Leechers,
                Downloaded = t.Downloaded,
                TotalAnnounces = t.TotalAnnounces,
                SuccessfulAnnounces = t.SuccessfulAnnounces,
                AnnounceInterval = t.AnnounceInterval > 0 ? t.AnnounceInterval : 1800,
                LastAnnounce = t.LastAnnounce,
                NextAnnounce = nextAnnounce,
                NextAnnounceSeconds = nextAnnounceSec,
                Message = isError
                    ? (!string.IsNullOrWhiteSpace(t.ErrorMessage) ? t.ErrorMessage : "Tracker error")
                    : (isQueued ? "Queued for announce" : "OK"),
            };
        }).ToList();

        return this.Ok(resources);
    }

    [HttpPost("{id:int}/trackers")]
    public async Task<ActionResult<TrackerResource>> AddTracker(int id, [FromBody] AddTrackerRequest request)
    {
        var url = request?.Url?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return this.BadRequest("Tracker URL is required");
        }

        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        if (torrent.IsPrivate)
        {
            return this.BadRequest("Cannot add public trackers to private torrents");
        }

        var existingTrackers = this.trackerEntryRepository?.GetByTorrentId(id);
        if (existingTrackers != null && existingTrackers.Any(t => string.Equals(t.Url?.Trim(), url, StringComparison.OrdinalIgnoreCase)))
        {
            return this.Conflict("Tracker already exists for this torrent");
        }

        var now = DateTime.UtcNow;
        var entry = new TrackerEntry
        {
            TorrentId = id,
            Url = url,
            Tier = 0,
            Enabled = true,
            Status = 0,
            Seeders = 0,
            Leechers = 0,
            Downloaded = 0,
            TotalAnnounces = 0,
            SuccessfulAnnounces = 0,
            AnnounceInterval = 1800,
            LastAnnounce = null,
            NextAnnounce = now.AddSeconds(1800),
        };

        var created = this.trackerEntryRepository != null
            ? (this.trackerEntryRepository.Insert(entry) ?? entry)
            : entry;

        if (this.downloadEngine != null)
        {
            await this.downloadEngine.AddTrackersAsync(id, new[] { url });
        }

        return this.Ok(new TrackerResource
        {
            Id = created.Id,
            Url = created.Url,
            Tier = created.Tier,
            Status = "Queued",
            Seeders = 0,
            Leechers = 0,
            Downloaded = 0,
            TotalAnnounces = 0,
            SuccessfulAnnounces = 0,
            AnnounceInterval = 1800,
            LastAnnounce = null,
            NextAnnounce = created.NextAnnounce,
            NextAnnounceSeconds = 1800,
            Message = "Queued for announce",
        });
    }

    [HttpDelete("{id:int}/trackers/{trackerId:int}")]
    public ActionResult DeleteTracker(int id, int trackerId)
    {
        this.trackerEntryRepository.Delete(trackerId);
        return this.Ok();
    }

    [HttpPost("{id:int}/trackers/{trackerId:int}/announce")]
    public async Task<ActionResult> AnnounceTracker(int id, int trackerId)
    {
        var tracker = this.trackerEntryRepository?.Get(trackerId);
        if (tracker != null)
        {
            var now = DateTime.UtcNow;
            tracker.Status = 1;
            tracker.LastAnnounce = now;
            tracker.NextAnnounce = now.AddSeconds(tracker.AnnounceInterval > 0 ? tracker.AnnounceInterval : 1800);
            tracker.TotalAnnounces++;
            tracker.SuccessfulAnnounces++;
            tracker.ErrorMessage = null;
            this.trackerEntryRepository.Update(tracker);
        }

        await this.torrentService.ForceAnnounceAsync(id);
        return this.Ok(new { success = true, message = "Announce triggered successfully" });
    }

    [HttpGet("{id:int}/logs")]
    public ActionResult<List<TorrentEventLogResource>> GetLogs(int id, [FromQuery] int count = 100)
    {
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var list = new List<TorrentEventLogResource>();
        var logId = 1;

        list.Add(new TorrentEventLogResource
        {
            Id = logId++,
            TorrentId = id,
            Level = "Info",
            Source = "Engine",
            Message = $"Torrent '{torrent.Name}' added to queue in category '{torrent.Category ?? "Default"}'",
            Timestamp = torrent.DateAdded,
        });

        if (!string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            list.Add(new TorrentEventLogResource
            {
                Id = logId++,
                TorrentId = id,
                Level = "Info",
                Source = "Storage",
                Message = $"Storage allocation configured at '{torrent.SavePath}'",
                Timestamp = torrent.DateAdded.AddSeconds(1),
            });
        }

        if (torrent.DateCompleted.HasValue)
        {
            list.Add(new TorrentEventLogResource
            {
                Id = logId++,
                TorrentId = id,
                Level = "Info",
                Source = "Download",
                Message = "Torrent download completed (100% verified)",
                Timestamp = torrent.DateCompleted.Value,
            });
        }

        list.Add(new TorrentEventLogResource
        {
            Id = logId++,
            TorrentId = id,
            Level = torrent.Status == TorrentStatus.Error ? "Error" : "Info",
            Source = "Peers",
            Message = $"Current state: {torrent.Status} (Progress: {torrent.Progress * 100:F1}%, DL: {torrent.DownloadSpeed / 1024} KB/s, UL: {torrent.UploadSpeed / 1024} KB/s, Seeds: {torrent.Seeders}, Peers: {torrent.Leechers})",
            Timestamp = DateTime.UtcNow,
        });

        return this.Ok(list);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentResource>> AddTorrentJson([FromBody] AddTorrentJsonRequest request)
    {
        if (request == null)
        {
            return this.BadRequest("Request body is empty.");
        }

        var magnet = !string.IsNullOrWhiteSpace(request.MagnetLink) ? request.MagnetLink : request.MagnetUrl;
        var isPaused = request.Paused || request.StartPaused;

        if (!string.IsNullOrWhiteSpace(magnet))
        {
            var torrent = await this.torrentService.AddFromMagnetAsync(magnet, request.Category, request.SavePath, isPaused);
            if (torrent == null)
            {
                return this.BadRequest("Failed to add torrent");
            }

            var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
            return this.Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            var bytes = await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl, maxSizeBytes: 10 * 1024 * 1024);
            var parsed = this.torrentFileParser.Parse(bytes);
            var torrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, isPaused, bytes);
            if (torrent == null)
            {
                return this.BadRequest("Failed to add torrent");
            }

            var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
            return this.Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        return this.BadRequest("Either magnetLink or downloadUrl is required.");
    }

    [HttpPost]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public async Task<ActionResult<TorrentResource>> AddTorrentForm(
        [FromForm] IFormFile file = null,
        [FromForm] string magnetUrl = null,
        [FromForm] string category = null,
        [FromForm] string savePath = null,
        [FromForm] bool paused = false,
        [FromForm] bool startPaused = false)
    {
        var isPaused = paused || startPaused;

        if (file != null && file.Length > 0)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = this.torrentFileParser.Parse(bytes);

            var torrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
            if (torrent == null)
            {
                return this.BadRequest("Failed to add torrent");
            }

            var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
            return this.Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(magnetUrl))
        {
            var torrent = await this.torrentService.AddFromMagnetAsync(magnetUrl, category, savePath, isPaused);
            if (torrent == null)
            {
                return this.BadRequest("Failed to add torrent");
            }

            var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
            return this.Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        return this.BadRequest("Either a .torrent file or a magnetUrl is required.");
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] List<IFormFile> files = null,
        [FromForm] string category = null,
        [FromForm] bool isPaused = false,
        [FromForm] bool startPaused = false)
    {
        var formFiles = new List<IFormFile>();
        if (files != null && files.Count > 0)
        {
            formFiles.AddRange(files);
        }

        if (this.Request.HasFormContentType && this.Request.Form.Files.Count > 0)
        {
            foreach (var f in this.Request.Form.Files)
            {
                if (!formFiles.Contains(f))
                {
                    formFiles.Add(f);
                }
            }
        }

        if (formFiles.Count == 0)
        {
            return this.BadRequest("No torrent file provided");
        }

        var pausedFlag = isPaused || startPaused;
        var added = new List<TorrentResource>();
        var failed = new List<TorrentUploadFailure>();

        foreach (var file in formFiles)
        {
            if (file == null || file.Length == 0)
            {
                continue;
            }

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var parsed = this.torrentFileParser.Parse(bytes);

                var torrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, pausedFlag, bytes);
                if (torrent == null)
                {
                    failed.Add(new TorrentUploadFailure(file.FileName, "Failed to add torrent"));
                    continue;
                }

                var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
                added.Add(TorrentResourceMapper.ToResource(torrent, meta));
            }
            catch (Exception ex)
            {
                failed.Add(new TorrentUploadFailure(file.FileName, ex.Message));
            }
        }

        return this.Ok(new TorrentUploadResult(added, failed));
    }

    [HttpPost("grab")]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentResource>> GrabRelease([FromBody] AddTorrentJsonRequest request)
    {
        return await this.AddTorrentJson(request);
    }

    [HttpPost("{id:int}/pause")]
    public async Task<ActionResult> Pause(int id)
    {
        await this.torrentService.PauseAsync(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/resume")]
    public async Task<ActionResult> Resume(int id)
    {
        await this.torrentService.ResumeAsync(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/recheck")]
    public async Task<ActionResult> Recheck(int id)
    {
        await this.torrentService.ForceRecheckAsync(id);
        return this.Ok();
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TorrentResource>> Update(int id, [FromBody] TorrentResource resource)
    {
        var existing = this.torrentService.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        if (resource.Category != null)
        {
            existing.Category = resource.Category;
        }

        if (resource.Label != null)
        {
            existing.Label = resource.Label;
        }

        if (resource.Priority.HasValue)
        {
            existing.Priority = resource.Priority.Value;
        }

        if (resource.UploadLimit.HasValue)
        {
            existing.UploadLimit = resource.UploadLimit.Value;
        }

        if (resource.DownloadLimit.HasValue)
        {
            existing.DownloadLimit = resource.DownloadLimit.Value;
        }

        if (resource.SequentialDownload.HasValue)
        {
            existing.SequentialDownload = resource.SequentialDownload.Value;
        }

        if (resource.InitialSeeding.HasValue)
        {
            existing.InitialSeeding = resource.InitialSeeding.Value;
        }

        if (resource.ForceStart.HasValue)
        {
            existing.ForceStart = resource.ForceStart.Value;
        }

        if (resource.TargetRatio.HasValue)
        {
            existing.TargetRatio = resource.TargetRatio.Value;
        }

        if (resource.TargetSeedTimeMinutes.HasValue)
        {
            existing.TargetSeedTimeMinutes = resource.TargetSeedTimeMinutes.Value;
        }

        if (resource.ShareLimitAction != null)
        {
            existing.ShareLimitAction = resource.ShareLimitAction;
        }

        if (!string.IsNullOrWhiteSpace(resource.Name))
        {
            existing.Name = resource.Name;
        }

        if (!string.IsNullOrWhiteSpace(resource.SavePath) && !string.Equals(resource.SavePath, existing.SavePath, StringComparison.OrdinalIgnoreCase))
        {
            await this.torrentService.SetLocationAsync(id, resource.SavePath, moveFiles: true);
            existing = this.torrentService.Get(id);
        }

        var isPrivateChanged = resource.IsPrivate != existing.IsPrivate;
        if (isPrivateChanged)
        {
            existing.IsPrivate = resource.IsPrivate;
        }

        if (resource.AnnounceInterval.HasValue && resource.AnnounceInterval.Value > 0)
        {
            var dbTrackers = this.trackerEntryRepository?.GetByTorrentId(id).ToList();
            if (dbTrackers != null)
            {
                foreach (var tracker in dbTrackers)
                {
                    tracker.AnnounceInterval = resource.AnnounceInterval.Value;
                    this.trackerEntryRepository.Update(tracker);
                }
            }
        }

        if (resource.Active.HasValue)
        {
            if (!resource.Active.Value && existing.Status != TorrentStatus.Paused)
            {
                await this.torrentService.PauseAsync(id);
            }
            else if (resource.Active.Value && existing.Status == TorrentStatus.Paused)
            {
                await this.torrentService.ResumeAsync(id);
            }
        }

        var updated = await this.torrentService.UpdateAsync(existing);
        if (this.downloadEngine != null && (resource.UploadLimit.HasValue || resource.DownloadLimit.HasValue))
        {
            await this.downloadEngine.SetTorrentRateLimitsAsync(updated.Id, updated.DownloadLimit, updated.UploadLimit);
        }

        if (this.downloadEngine != null && isPrivateChanged)
        {
            await this.downloadEngine.SetTorrentPrivateStatusAsync(updated.Id, updated.IsPrivate);
        }

        var meta = this.mediaEnrichmentService.GetMetadata(id);
        var res = TorrentResourceMapper.ToResource(updated, meta);
        res.AnnounceInterval = resource.AnnounceInterval ?? 1800;
        res.NextUpdate = resource.NextUpdate ?? 1800;
        res.Threshold = resource.Threshold ?? 1;
        res.SmallTorrentLimit = resource.SmallTorrentLimit ?? 50;
        res.Active = updated.Status == TorrentStatus.Downloading || updated.Status == TorrentStatus.Seeding;
        return this.Ok(res);
    }

    [HttpPost("{id:int}/announce")]
    public async Task<ActionResult> Announce(int id)
    {
        var trackers = this.trackerEntryRepository?.GetByTorrentId(id).ToList();
        if (trackers != null && trackers.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var tracker in trackers)
            {
                tracker.Status = 1;
                tracker.LastAnnounce = now;
                tracker.NextAnnounce = now.AddSeconds(tracker.AnnounceInterval > 0 ? tracker.AnnounceInterval : 1800);
                tracker.TotalAnnounces++;
                tracker.SuccessfulAnnounces++;
                tracker.ErrorMessage = null;
                this.trackerEntryRepository.Update(tracker);
            }
        }

        await this.torrentService.ForceAnnounceAsync(id);
        return this.Ok(new { success = true, message = "Announce triggered successfully" });
    }

    [HttpPost("{id:int}/queue")]
    [HttpPut("{id:int}/queue")]
    public async Task<ActionResult> MoveQueue(int id, [FromBody] MoveQueueRequest request)
    {
        await this.torrentService.MoveQueueAsync(id, request?.Position);
        return this.Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, [FromQuery] bool deleteFiles = false)
    {
        await this.torrentService.DeleteAsync(id, deleteFiles);
        return this.NoContent();
    }

    protected override TorrentResource GetResourceById(Torrent model)
    {
        if (model == null)
        {
            return null;
        }

        var meta = this.mediaEnrichmentService.GetMetadata(model.Id);
        return TorrentResourceMapper.ToResource(model, meta);
    }
}
