// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.BitTorrent.Creation;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;

namespace Leecharr.Api.V1.Torrents;

public record TorrentUploadFailure(string FileName, string Reason);

public record TorrentUploadResult(List<TorrentResource> Added, List<TorrentUploadFailure> Failed);

public class BanPeerRequest
{
    [Required]
    public string Ip { get; set; }
}

public class MoveQueueRequest
{
    [Required]
    [StringLength(50)]
    public string Position { get; set; }
}

public class SetFilePriorityRequest
{
    [Range(0, 7)]
    public int Priority { get; set; }
}

public class SetFilePriorityItem
{
    [JsonPropertyName("fileId")]
    public int FileId { get; set; }

    [JsonPropertyName("priority")]
    [Range(0, 7)]
    public int Priority { get; set; }
}

public class SetFilePrioritiesRequestConverter : JsonConverter<SetFilePrioritiesRequest>
{
    public override SetFilePrioritiesRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var request = new SetFilePrioritiesRequest();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var items = JsonSerializer.Deserialize<List<SetFilePriorityItem>>(ref reader, options);
            request.Files = items ?? new List<SetFilePriorityItem>();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<List<SetFilePriorityItem>>(filesProp.GetRawText(), options);
                request.Files = items ?? new List<SetFilePriorityItem>();
            }
        }

        return request;
    }

    public override void Write(Utf8JsonWriter writer, SetFilePrioritiesRequest value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value?.Files ?? new List<SetFilePriorityItem>(), options);
    }
}

[JsonConverter(typeof(SetFilePrioritiesRequestConverter))]
public class SetFilePrioritiesRequest : IEnumerable<SetFilePriorityItem>
{
    [JsonPropertyName("files")]
    public List<SetFilePriorityItem> Files { get; set; } = new();

    public SetFilePrioritiesRequest()
    {
    }

    public SetFilePrioritiesRequest(IEnumerable<SetFilePriorityItem> files)
    {
        this.Files = files?.ToList() ?? new();
    }

    public static implicit operator SetFilePrioritiesRequest(List<SetFilePriorityItem> list) => new(list);

    public IEnumerator<SetFilePriorityItem> GetEnumerator() => this.Files.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

public class AddTorrentJsonRequest
{
    [StringLength(4096)]
    public string MagnetLink { get; set; }

    [StringLength(4096)]
    public string MagnetUrl { get; set; }

    [StringLength(4096)]
    public string DownloadUrl { get; set; }

    [StringLength(500)]
    public string Title { get; set; }

    [StringLength(255)]
    public string Category { get; set; }

    [StringLength(1024)]
    public string SavePath { get; set; }

    public bool Paused { get; set; }

    public bool StartPaused { get; set; }

    public bool? SequentialDownload { get; set; }

    public bool? FirstLastPiecePriority { get; set; }
}

[V1ApiController("torrents")]
[Route("api/v1/torrent")]
[Authorize(Policy = "RequireOperator")]
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
    private readonly ITorrentCreationService torrentCreationService;
    private readonly ITorrentLogService torrentLogService;
    private readonly IConfigService configService;
    private readonly ICategoryService categoryService;
    private readonly IBlocklistService blocklistService;
    private readonly ITagRepository tagRepository;

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IMediaEnrichmentService mediaEnrichmentService,
        ITrackerEntryRepository trackerEntryRepository,
        IBroadcastSignalRMessage signalRBroadcaster,
        IGeoIpService geoIpService = null,
        IDownloadEngine downloadEngine = null,
        ISafeHttpClientService safeHttpClientService = null,
        ITorrentCreationService torrentCreationService = null,
        ITorrentLogService torrentLogService = null,
        IConfigService configService = null,
        ICategoryService categoryService = null,
        IBlocklistService blocklistService = null,
        ITagRepository tagRepository = null)
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
        this.torrentCreationService = torrentCreationService;
        this.torrentLogService = torrentLogService;
        this.configService = configService;
        this.categoryService = categoryService;
        this.blocklistService = blocklistService;
        this.tagRepository = tagRepository;
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
            var task = this.downloadEngine?.GetTask(t.Id) ?? this.torrentService?.GetDownloadTask(t.Id);
            var bitfield = task?.PieceBitfield != null && task.PieceBitfield.Length > 0
                ? TorrentResourceMapper.EncodeBitfield(task.PieceBitfield)
                : null;
            var res = TorrentResourceMapper.ToResource(t, meta, bitfield);
            res.QueuePosition = t.QueuePosition > 0 ? t.QueuePosition : idx + 1;
            if (allDbTrackers.TryGetValue(t.Id, out var trackerEntries) && trackerEntries.Count > 0)
            {
                res.Trackers = trackerEntries.Select(x => TrackerUrlSanitizer.Sanitize(x.Url)).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
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
        var task = this.downloadEngine?.GetTask(id) ?? this.torrentService?.GetDownloadTask(id);
        var bitfield = task?.PieceBitfield != null && task.PieceBitfield.Length > 0
            ? TorrentResourceMapper.EncodeBitfield(task.PieceBitfield)
            : null;
        var res = TorrentResourceMapper.ToResource(torrent, meta, bitfield);
        res.QueuePosition = torrent.QueuePosition > 0 ? torrent.QueuePosition : 1;
        var dbTrackers = this.trackerEntryRepository?.GetByTorrentId(id).ToList();

        if (dbTrackers != null && dbTrackers.Count > 0)
        {
            res.Trackers = dbTrackers.Select(x => TrackerUrlSanitizer.Sanitize(x.Url)).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
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
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var prio = request?.Priority ?? priority;
        if (!prio.HasValue)
        {
            return this.BadRequest("Priority must be specified.");
        }

        if (prio.Value < 0 || prio.Value > 7)
        {
            return this.BadRequest("Priority must be between 0 and 7.");
        }

        var success = await this.torrentFileService.SetPriorityAsync(id, fileId, prio.Value);
        if (!success)
        {
            return this.NotFound("File not found or does not belong to torrent.");
        }

        return this.Ok();
    }

    [HttpPut("{id:int}/files/priorities")]
    [HttpPost("{id:int}/files/priorities")]
    public async Task<ActionResult> SetFilePriorities(int id, [FromBody] SetFilePrioritiesRequest request)
    {
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        if (request?.Files == null || request.Files.Count == 0)
        {
            return this.BadRequest("Files list cannot be empty.");
        }

        foreach (var item in request.Files)
        {
            if (item.Priority < 0 || item.Priority > 7)
            {
                return this.BadRequest("Priority must be between 0 and 7.");
            }
        }

        var priorities = request.Files.Select(f => (f.FileId, f.Priority));
        var success = await this.torrentFileService.SetPrioritiesAsync(id, priorities);
        if (!success)
        {
            return this.NotFound("One or more files not found or do not belong to torrent.");
        }

        return this.Ok();
    }

    [HttpGet("{id:int}/peers")]
    public async Task<ActionResult<List<PeerResource>>> GetPeers(int id)
    {
        var task = this.torrentService.GetDownloadTask(id);
        if (task == null)
        {
            return this.Ok(new List<PeerResource>());
        }

        var peers = task.GetPeers();
        if (peers == null || peers.Count == 0)
        {
            return this.Ok(new List<PeerResource>());
        }

        var peerTasks = peers.Select(async (p, idx) =>
        {
            var geo = this.geoIpService != null ? await this.geoIpService.LookupAsync(p.Ip) : null;
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
                IsEncrypted = p.IsEncrypted,
                IsUtp = p.IsUtp,
                IsIncoming = p.IsIncoming,
                Protocol = p.IsUtp ? "uTP" : "TCP",
                CountryCode = geo?.CountryCode ?? string.Empty,
                CountryName = geo?.CountryName ?? string.Empty,
                City = geo?.City ?? string.Empty,
            };
        });

        var resources = (await Task.WhenAll(peerTasks)).ToList();
        return this.Ok(resources);
    }

    [HttpPost("{id:int}/peers/ban")]
    public async Task<IActionResult> BanPeer(int id, [FromBody] BanPeerRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Ip))
        {
            return this.BadRequest("IP address is required.");
        }

        return await this.BanPeerInternalAsync(id, request.Ip.Trim());
    }

    [HttpDelete("{id:int}/peers/{ip}")]
    public async Task<IActionResult> DisconnectPeer(int id, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return this.BadRequest("IP address is required.");
        }

        return await this.BanPeerInternalAsync(id, ip.Trim());
    }

    private async Task<IActionResult> BanPeerInternalAsync(int id, string ip)
    {
        var torrent = this.torrentService.Get(id);
        if (torrent == null)
        {
            return this.NotFound();
        }

        if (this.blocklistService != null)
        {
            await this.blocklistService.AddRulesAsync(new[] { ip });
        }

        var task = this.torrentService.GetDownloadTask(id);
        if (task != null)
        {
            await task.DisconnectPeerAsync(ip);
        }

        return this.Ok(new { success = true, ip = ip, message = $"Peer {ip} banned and disconnected." });
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
                Status = 0,
                Enabled = true,
                Seeders = torrent.Seeders,
                Leechers = torrent.Leechers,
                AnnounceInterval = 1800,
                LastAnnounce = null,
                NextAnnounce = torrent.DateAdded.AddSeconds(1800),
                TotalAnnounces = 0,
                SuccessfulAnnounces = 0,
            };
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
                Url = TrackerUrlSanitizer.Sanitize(t.Url),
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
        if (request == null)
        {
            return this.BadRequest("Request body cannot be null");
        }

        var url = request.Url?.Trim();
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
    public async Task<ActionResult> DeleteTracker(int id, int trackerId)
    {
        var tracker = this.trackerEntryRepository?.Get(trackerId);
        if (tracker != null && this.downloadEngine != null && !string.IsNullOrWhiteSpace(tracker.Url))
        {
            await this.downloadEngine.RemoveTrackersAsync(id, new[] { tracker.Url });
        }

        this.trackerEntryRepository?.Delete(trackerId);
        return this.Ok();
    }

    [HttpPost("{id:int}/trackers/{trackerId:int}/announce")]
    public async Task<ActionResult> AnnounceTracker(int id, int trackerId)
    {
        var tracker = this.trackerEntryRepository?.Get(trackerId);
        if (tracker != null)
        {
            var now = DateTime.UtcNow;
            tracker.Status = 0;
            tracker.LastAnnounce = now;
            tracker.NextAnnounce = now.AddSeconds(tracker.AnnounceInterval > 0 ? tracker.AnnounceInterval : 1800);
            tracker.TotalAnnounces++;
            this.trackerEntryRepository.Update(tracker);
        }

        this.torrentLogService?.Log(id, "Info", "Tracker", "Manual tracker update requested (announcing to all active trackers...)");
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

        var entries = this.torrentLogService?.GetLogs(id, count);
        if (entries == null || entries.Count == 0)
        {
            if (this.torrentLogService != null)
            {
                this.torrentLogService.Log(
                    id,
                    "Info",
                    "Engine",
                    $"Torrent '{torrent.Name}' added to queue in category '{torrent.Category ?? "Default"}'",
                    torrent.DateAdded);

                if (!string.IsNullOrWhiteSpace(torrent.SavePath))
                {
                    this.torrentLogService.Log(
                        id,
                        "Info",
                        "Storage",
                        $"Storage allocation configured at '{torrent.SavePath}'",
                        torrent.DateAdded.AddSeconds(1));
                }

                if (torrent.DateCompleted.HasValue)
                {
                    this.torrentLogService.Log(
                        id,
                        "Info",
                        "Download",
                        "Torrent download completed (100% verified)",
                        torrent.DateCompleted.Value);
                }

                entries = this.torrentLogService.GetLogs(id, count);
            }
        }

        var resources = (entries ?? Array.Empty<TorrentEventLog>()).Select(e => new TorrentEventLogResource
        {
            Id = e.Id,
            TorrentId = e.TorrentId,
            Level = e.Level,
            Source = e.Source,
            Message = e.Message,
            Timestamp = e.Timestamp,
        }).ToList();

        if (resources.Count == 0)
        {
            var logId = 1;
            resources.Add(new TorrentEventLogResource
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
                resources.Add(new TorrentEventLogResource
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
                resources.Add(new TorrentEventLogResource
                {
                    Id = logId++,
                    TorrentId = id,
                    Level = "Info",
                    Source = "Download",
                    Message = "Torrent download completed (100% verified)",
                    Timestamp = torrent.DateCompleted.Value,
                });
            }
        }

        return this.Ok(resources);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentResource>> AddTorrentJson([FromBody] AddTorrentJsonRequest request)
    {
        if (request == null)
        {
            return this.BadRequest("Request body cannot be null");
        }

        var magnet = !string.IsNullOrWhiteSpace(request.MagnetLink) ? request.MagnetLink : request.MagnetUrl;
        var isPaused = request.Paused || request.StartPaused;

        if (!string.IsNullOrWhiteSpace(magnet))
        {
            var torrent = (request.SequentialDownload.HasValue || request.FirstLastPiecePriority.HasValue)
                ? await this.torrentService.AddFromMagnetAsync(magnet, request.Category, request.SavePath, isPaused, request.SequentialDownload, request.FirstLastPiecePriority)
                : await this.torrentService.AddFromMagnetAsync(magnet, request.Category, request.SavePath, isPaused);
            if (torrent == null)
            {
                return this.BadRequest("Failed to add torrent");
            }

            var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
            return this.Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? 250L * 1024 * 1024;
            var bytes = await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl, maxSizeBytes: maxTorrentBytes);
            var parsed = this.torrentFileParser.Parse(bytes);
            var torrent = (request.SequentialDownload.HasValue || request.FirstLastPiecePriority.HasValue)
                ? await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, isPaused, bytes, request.SequentialDownload, request.FirstLastPiecePriority)
                : await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.SavePath, isPaused, bytes);
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
        [FromForm(Name = "paused")] bool paused = false,
        [FromForm(Name = "isPaused")] bool isPaused = false,
        [FromForm(Name = "startPaused")] bool startPaused = false,
        [FromForm(Name = "sequentialDownload")] bool? sequentialDownload = null,
        [FromForm(Name = "firstLastPiecePriority")] bool? firstLastPiecePriority = null)
    {
        var isPausedFlag = paused || isPaused || startPaused;

        if (file != null && file.Length > 0)
        {
            var maxBytes = this.configService?.MaxTorrentFileSizeBytes > 0 ? this.configService.MaxTorrentFileSizeBytes : 250L * 1024 * 1024;
            if (file.Length > maxBytes)
            {
                return this.StatusCode(StatusCodes.Status413PayloadTooLarge, $"Torrent file size exceeds maximum allowed size of {maxBytes} bytes.");
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = this.torrentFileParser.Parse(bytes);

            var torrent = (sequentialDownload.HasValue || firstLastPiecePriority.HasValue)
                ? await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPausedFlag, bytes, sequentialDownload, firstLastPiecePriority)
                : await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPausedFlag, bytes);
            if (torrent == null)
            {
                return this.BadRequest("Failed to add torrent");
            }

            var meta = this.mediaEnrichmentService.GetMetadata(torrent.Id);
            return this.Ok(TorrentResourceMapper.ToResource(torrent, meta));
        }

        if (!string.IsNullOrWhiteSpace(magnetUrl))
        {
            var torrent = (sequentialDownload.HasValue || firstLastPiecePriority.HasValue)
                ? await this.torrentService.AddFromMagnetAsync(magnetUrl, category, savePath, isPausedFlag, sequentialDownload, firstLastPiecePriority)
                : await this.torrentService.AddFromMagnetAsync(magnetUrl, category, savePath, isPausedFlag);
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
        [FromForm(Name = "downloadPath")] string downloadPath = null,
        [FromForm(Name = "savePath")] string savePath = null,
        [FromForm(Name = "paused")] bool? paused = null,
        [FromForm(Name = "isPaused")] bool? isPaused = null,
        [FromForm(Name = "startPaused")] bool? startPaused = null,
        [FromForm(Name = "sequentialDownload")] bool? sequentialDownload = null,
        [FromForm(Name = "firstLastPiecePriority")] bool? firstLastPiecePriority = null)
    {
        var formFiles = new List<IFormFile>();
        if (files != null && files.Count > 0)
        {
            formFiles.AddRange(files);
        }

        if (this.Request?.HasFormContentType == true && this.Request.Form.Files.Count > 0)
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

        var maxBytes = this.configService?.MaxTorrentFileSizeBytes > 0 ? this.configService.MaxTorrentFileSizeBytes : 250L * 1024 * 1024;
        var oversizedFile = formFiles.FirstOrDefault(f => f != null && f.Length > maxBytes);
        if (oversizedFile != null)
        {
            return this.StatusCode(StatusCodes.Status413PayloadTooLarge, $"File '{oversizedFile.FileName}' exceeds maximum allowed size of {maxBytes} bytes.");
        }

        var destination = !string.IsNullOrWhiteSpace(downloadPath) ? downloadPath : savePath;
        if (string.IsNullOrWhiteSpace(destination) && this.Request?.HasFormContentType == true)
        {
            destination = this.Request.Form["savePath"].FirstOrDefault() ?? this.Request.Form["downloadPath"].FirstOrDefault();
        }

        var pausedFlag = paused ?? isPaused ?? startPaused ?? false;
        if (!pausedFlag && this.Request?.HasFormContentType == true)
        {
            if (bool.TryParse(this.Request.Form["paused"], out var p) && p)
            {
                pausedFlag = true;
            }
            else if (bool.TryParse(this.Request.Form["isPaused"], out var ip) && ip)
            {
                pausedFlag = true;
            }
            else if (bool.TryParse(this.Request.Form["startPaused"], out var sp) && sp)
            {
                pausedFlag = true;
            }
        }

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

                var seqOpt = sequentialDownload;
                if (!seqOpt.HasValue && this.Request?.HasFormContentType == true && bool.TryParse(this.Request.Form["sequentialDownload"], out var sVal))
                {
                    seqOpt = sVal;
                }

                var flpOpt = firstLastPiecePriority;
                if (!flpOpt.HasValue && this.Request?.HasFormContentType == true && bool.TryParse(this.Request.Form["firstLastPiecePriority"], out var flpVal))
                {
                    flpOpt = flpVal;
                }

                var torrent = (seqOpt.HasValue || flpOpt.HasValue)
                    ? await this.torrentService.AddFromParsedTorrentAsync(parsed, category, destination, pausedFlag, bytes, seqOpt, flpOpt)
                    : await this.torrentService.AddFromParsedTorrentAsync(parsed, category, destination, pausedFlag, bytes);
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

    [HttpPost("create")]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentCreationResult>> CreateTorrent([FromBody] TorrentCreationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request?.Path))
        {
            return this.BadRequest(new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = "A valid source file or directory path is required.",
            });
        }

        if (this.torrentCreationService == null)
        {
            return this.StatusCode(StatusCodes.Status503ServiceUnavailable, new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = "Torrent creation service is unavailable.",
            });
        }

        var result = await this.torrentCreationService.CreateTorrentAsync(request);
        return this.Ok(result);
    }

    [HttpPost("{id:int}/pause")]
    public async Task<ActionResult<TorrentResource>> Pause(int id)
    {
        await this.torrentService.PauseAsync(id);
        return this.GetById(id);
    }

    [HttpPost("{id:int}/resume")]
    public async Task<ActionResult<TorrentResource>> Resume(int id)
    {
        await this.torrentService.ResumeAsync(id);
        return this.GetById(id);
    }

    [HttpPost("{id:int}/recheck")]
    public async Task<ActionResult<TorrentResource>> Recheck(int id)
    {
        await this.torrentService.ForceRecheckAsync(id);
        return this.GetById(id);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TorrentResource>> Update(int id, [FromBody] TorrentResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest("Request body cannot be null");
        }

        var existing = this.torrentService.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        if (resource.InitialSeeding == true && (existing.Progress < 1.0 || existing.Status != TorrentStatus.Seeding))
        {
            return this.BadRequest("Super seeding can only be enabled for 100% completed seeding torrents.");
        }

        if (resource.Category != null)
        {
            existing.Category = resource.Category;
        }

        if (resource.TagIds != null && resource.TagIds.Count > 0)
        {
            existing.TagIds = resource.TagIds.Distinct().ToList();
            if (this.tagRepository != null && resource.Label == null)
            {
                var allTags = this.tagRepository.All().ToDictionary(t => t.Id, t => t.Label);
                var labels = existing.TagIds
                    .Where(t => allTags.ContainsKey(t))
                    .Select(t => allTags[t])
                    .Distinct()
                    .ToList();
                existing.Label = string.Join(", ", labels);
            }
        }

        if (resource.Label != null)
        {
            existing.Label = resource.Label;
            if (string.IsNullOrWhiteSpace(resource.Label))
            {
                existing.TagIds = new List<int>();
            }
            else if (this.tagRepository != null && (resource.TagIds == null || resource.TagIds.Count == 0))
            {
                var labelToId = this.tagRepository.All().ToDictionary(t => t.Label, t => t.Id, StringComparer.OrdinalIgnoreCase);
                var parsedLabels = resource.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();
                existing.TagIds = parsedLabels
                    .Where(t => labelToId.ContainsKey(t))
                    .Select(t => labelToId[t])
                    .Distinct()
                    .ToList();
            }
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

        var isSequentialChanged = resource.SequentialDownload.HasValue && resource.SequentialDownload.Value != existing.SequentialDownload;
        if (resource.SequentialDownload.HasValue)
        {
            existing.SequentialDownload = resource.SequentialDownload.Value;
        }

        var isFirstLastChanged = resource.FirstLastPiecePriority.HasValue && resource.FirstLastPiecePriority.Value != existing.FirstLastPiecePriority;
        if (resource.FirstLastPiecePriority.HasValue)
        {
            existing.FirstLastPiecePriority = resource.FirstLastPiecePriority.Value;
        }

        var isInitialSeedingChanged = resource.InitialSeeding.HasValue && resource.InitialSeeding.Value != existing.InitialSeeding;
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
                existing.Status = TorrentStatus.Paused;
            }
            else if (resource.Active.Value && existing.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Queued or TorrentStatus.Error or TorrentStatus.Stalled)
            {
                await this.torrentService.ResumeAsync(id);
                existing.Status = existing.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            }
        }

        var updated = await this.torrentService.UpdateAsync(existing);
        if (isInitialSeedingChanged)
        {
            await this.torrentService.SetSuperSeedingAsync(updated.Id, updated.InitialSeeding);
        }

        if (this.downloadEngine != null && isSequentialChanged)
        {
            await this.downloadEngine.SetSequentialDownloadAsync(updated.Id, updated.SequentialDownload);
        }

        if (this.downloadEngine != null && isFirstLastChanged)
        {
            await this.downloadEngine.SetFirstLastPiecePriorityAsync(updated.Id, updated.FirstLastPiecePriority);
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
                tracker.Status = 0;
                tracker.LastAnnounce = now;
                tracker.NextAnnounce = now.AddSeconds(tracker.AnnounceInterval > 0 ? tracker.AnnounceInterval : 1800);
                tracker.TotalAnnounces++;
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
        if (request == null)
        {
            return this.BadRequest("Request body cannot be null");
        }

        await this.torrentService.MoveQueueAsync(id, request.Position);
        return this.Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, [FromQuery] bool deleteFiles = false)
    {
        await this.torrentService.DeleteAsync(id, deleteFiles);
        return this.NoContent();
    }

    [HttpPost("bulk")]
    public async Task<ActionResult<BulkActionResult>> BulkAction([FromBody] BulkTorrentActionResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Action))
        {
            return this.BadRequest(new { message = "Action is required." });
        }

        var result = new BulkActionResult();
        if (resource.TorrentIds == null || resource.TorrentIds.Count == 0)
        {
            return this.Ok(result);
        }

        var errors = new ConcurrentBag<string>();
        var successCount = 0;
        var failedCount = 0;

        await Parallel.ForEachAsync(resource.TorrentIds, async (id, cancellationToken) =>
        {
            try
            {
                await this.ExecuteActionForTorrentAsync(id, resource);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedCount);
                errors.Add($"Torrent {id}: {ex.Message}");
            }
        });

        result.SuccessCount = successCount;
        result.FailedCount = failedCount;
        result.Errors = errors.ToList();

        return this.Ok(result);
    }

    private async Task ExecuteActionForTorrentAsync(int id, BulkTorrentActionResource resource)
    {
        var action = resource.Action.Trim().ToLowerInvariant();
        switch (action)
        {
            case "start":
            case "resume":
                await this.torrentService.ResumeAsync(id);
                break;

            case "stop":
            case "pause":
                await this.torrentService.PauseAsync(id);
                break;

            case "delete":
            case "remove":
                await this.torrentService.DeleteAsync(id, resource.DeleteFiles);
                break;

            case "recheck":
            case "forcerecheck":
                await this.torrentService.ForceRecheckAsync(id);
                break;

            case "announce":
            case "forceannounce":
                await this.torrentService.ForceAnnounceAsync(id);
                break;

            case "setcategory":
                {
                    var torrent = this.torrentService.Get(id);
                    if (torrent == null)
                    {
                        throw new KeyNotFoundException($"Torrent {id} not found");
                    }

                    string categoryName = null;
                    if (resource.CategoryId.HasValue && resource.CategoryId.Value > 0 && this.categoryService != null)
                    {
                        var cat = this.categoryService.Get(resource.CategoryId.Value);
                        categoryName = cat?.Name;
                    }

                    await this.torrentService.SetCategoryAsync(id, categoryName);
                    break;
                }

            case "addtags":
                {
                    var torrent = this.torrentService.Get(id);
                    if (torrent == null)
                    {
                        throw new KeyNotFoundException($"Torrent {id} not found");
                    }

                    if (resource.TagIds != null && resource.TagIds.Count > 0)
                    {
                        torrent.TagIds ??= new List<int>();
                        torrent.TagIds = torrent.TagIds.Union(resource.TagIds).Distinct().ToList();

                        if (this.tagRepository != null)
                        {
                            var allTags = this.tagRepository.All().ToDictionary(t => t.Id, t => t.Label);
                            var labels = torrent.TagIds
                                .Where(t => allTags.ContainsKey(t))
                                .Select(t => allTags[t])
                                .Distinct()
                                .ToList();
                            torrent.Label = string.Join(", ", labels);
                        }

                        await this.torrentService.UpdateAsync(torrent);
                    }

                    break;
                }

            case "removetags":
                {
                    var torrent = this.torrentService.Get(id);
                    if (torrent == null)
                    {
                        throw new KeyNotFoundException($"Torrent {id} not found");
                    }

                    if (resource.TagIds != null && resource.TagIds.Count > 0 && torrent.TagIds != null)
                    {
                        torrent.TagIds = torrent.TagIds.Except(resource.TagIds).ToList();

                        if (this.tagRepository != null)
                        {
                            var allTags = this.tagRepository.All().ToDictionary(t => t.Id, t => t.Label);
                            var labels = torrent.TagIds
                                .Where(t => allTags.ContainsKey(t))
                                .Select(t => allTags[t])
                                .Distinct()
                                .ToList();
                            torrent.Label = string.Join(", ", labels);
                        }

                        await this.torrentService.UpdateAsync(torrent);
                    }

                    break;
                }

            case "setpriority":
                {
                    var torrent = this.torrentService.Get(id);
                    if (torrent == null)
                    {
                        throw new KeyNotFoundException($"Torrent {id} not found");
                    }

                    if (resource.Priority.HasValue)
                    {
                        torrent.Priority = resource.Priority.Value;
                        await this.torrentService.UpdateAsync(torrent);
                    }

                    break;
                }

            case "setspeedlimits":
                {
                    var torrent = this.torrentService.Get(id);
                    if (torrent == null)
                    {
                        throw new KeyNotFoundException($"Torrent {id} not found");
                    }

                    if (resource.UploadLimit.HasValue)
                    {
                        torrent.UploadLimit = resource.UploadLimit.Value;
                    }

                    if (resource.DownloadLimit.HasValue)
                    {
                        torrent.DownloadLimit = resource.DownloadLimit.Value;
                    }

                    await this.torrentService.UpdateAsync(torrent);

                    break;
                }

            default:
                throw new ArgumentException($"Unknown action: {resource.Action}");
        }
    }

    [HttpPost("preview")]
    [Consumes("application/json")]
    public async Task<ActionResult<TorrentPreviewResource>> PreviewJson([FromBody] TorrentPreviewRequest request)
    {
        if (request == null)
        {
            return this.BadRequest("Request body cannot be null");
        }

        var magnet = !string.IsNullOrWhiteSpace(request.MagnetLink) ? request.MagnetLink : (!string.IsNullOrWhiteSpace(request.MagnetUrl) ? request.MagnetUrl : request.Uri);
        if (!string.IsNullOrWhiteSpace(magnet) && magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var parsedMagnet = MagnetLinkParser.Parse(magnet);
            if (parsedMagnet == null || string.IsNullOrWhiteSpace(parsedMagnet.InfoHash))
            {
                return this.BadRequest("Invalid magnet URI");
            }

            var res = new TorrentPreviewResource
            {
                Name = !string.IsNullOrWhiteSpace(parsedMagnet.DisplayName) ? parsedMagnet.DisplayName : parsedMagnet.InfoHash,
                InfoHash = parsedMagnet.InfoHash.ToLowerInvariant(),
                Trackers = parsedMagnet.Trackers?.Select(TrackerUrlSanitizer.Sanitize).ToList() ?? new List<string>(),
                TotalSize = 0,
                PieceCount = 0,
                PieceLength = 0,
            };

            return this.Ok(res);
        }

        byte[] torrentBytes = null;
        if (!string.IsNullOrWhiteSpace(request.TorrentBase64))
        {
            try
            {
                torrentBytes = Convert.FromBase64String(request.TorrentBase64);
            }
            catch (Exception ex)
            {
                return this.BadRequest($"Invalid base64 payload: {ex.Message}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? 250L * 1024 * 1024;
            torrentBytes = await this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl, maxSizeBytes: maxTorrentBytes);
        }

        if (torrentBytes != null && torrentBytes.Length > 0)
        {
            return this.PreviewFromBytes(torrentBytes);
        }

        return this.BadRequest("No valid magnet URI, download URL, or torrent data provided");
    }

    [HttpPost("preview/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TorrentPreviewResource>> PreviewUpload([FromForm] List<IFormFile> files = null)
    {
        var targetFile = files?.FirstOrDefault() ?? (this.Request?.HasFormContentType == true && this.Request.Form.Files.Count > 0 ? this.Request.Form.Files[0] : null);
        if (targetFile == null || targetFile.Length == 0)
        {
            return this.BadRequest("No torrent file provided");
        }

        var maxBytes = this.configService?.MaxTorrentFileSizeBytes > 0 ? this.configService.MaxTorrentFileSizeBytes : 250L * 1024 * 1024;
        if (targetFile.Length > maxBytes)
        {
            return this.StatusCode(StatusCodes.Status413PayloadTooLarge, $"File '{targetFile.FileName}' exceeds maximum allowed size of {maxBytes} bytes.");
        }

        using var ms = new MemoryStream();
        await targetFile.CopyToAsync(ms);
        return this.PreviewFromBytes(ms.ToArray());
    }

    private ActionResult<TorrentPreviewResource> PreviewFromBytes(byte[] bytes)
    {
        var parsed = this.torrentFileParser.Parse(bytes);
        if (parsed == null)
        {
            return this.BadRequest("Failed to parse torrent data");
        }

        var trackers = new List<string>();
        if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl))
        {
            trackers.Add(parsed.AnnounceUrl);
        }

        if (parsed.AnnounceList != null)
        {
            foreach (var tier in parsed.AnnounceList)
            {
                if (tier != null)
                {
                    trackers.AddRange(tier.Where(u => !string.IsNullOrWhiteSpace(u)));
                }
            }
        }

        var res = new TorrentPreviewResource
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash?.ToLowerInvariant(),
            TotalSize = parsed.TotalSize,
            PieceCount = parsed.PieceCount,
            PieceLength = parsed.PieceLength,
            Comment = parsed.Comment,
            CreatedBy = parsed.CreatedBy,
            CreationDate = parsed.CreationDate,
            Trackers = trackers.Select(TrackerUrlSanitizer.Sanitize).Distinct().ToList(),
            Files = parsed.Files?.Select(f => new TorrentPreviewFileResource
            {
                Path = f.Path,
                Size = f.Size,
                Extension = !string.IsNullOrWhiteSpace(f.Path) ? Path.GetExtension(f.Path) : string.Empty,
            }).ToList() ?? new List<TorrentPreviewFileResource>(),
        };

        return this.Ok(res);
    }

    protected override TorrentResource GetResourceById(Torrent model)
    {
        if (model == null)
        {
            return null;
        }

        var meta = this.mediaEnrichmentService.GetMetadata(model.Id);
        var task = this.downloadEngine?.GetTask(model.Id) ?? this.torrentService?.GetDownloadTask(model.Id);
        var bitfield = task?.PieceBitfield != null && task.PieceBitfield.Length > 0
            ? TorrentResourceMapper.EncodeBitfield(task.PieceBitfield)
            : null;
        var res = TorrentResourceMapper.ToResource(model, meta, bitfield);
        var dbTrackers = this.trackerEntryRepository?.GetByTorrentId(model.Id)?.ToList();
        if (dbTrackers != null && dbTrackers.Count > 0)
        {
            res.Trackers = dbTrackers.Select(x => TrackerUrlSanitizer.Sanitize(x.Url)).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (string.IsNullOrWhiteSpace(res.TrackerUrl) && res.Trackers.Count > 0)
            {
                res.TrackerUrl = res.Trackers[0];
            }
        }
        else if (!string.IsNullOrWhiteSpace(res.TrackerUrl))
        {
            res.Trackers = new List<string> { res.TrackerUrl };
        }

        return res;
    }
}
