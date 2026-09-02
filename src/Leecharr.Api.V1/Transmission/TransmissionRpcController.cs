using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Transmission;

public class TransmissionRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();

    [JsonPropertyName("tag")]
    public JsonElement Tag { get; set; }
}

public class TransmissionRpcResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = "success";

    [JsonPropertyName("arguments")]
    public object Arguments { get; set; }

    [JsonPropertyName("tag")]
    public object Tag { get; set; }
}

[AllowAnonymous]
[ApiController]
[Route("transmission/rpc")]
public class TransmissionRpcController : ControllerBase
{
    private const string SessionHeaderName = "X-Transmission-Session-Id";
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IConfigService _configService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public TransmissionRpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService,
        IDiskSpaceService diskSpaceService = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
    }

    [HttpGet]
    public IActionResult HandleGet()
    {
        if (!Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) || string.IsNullOrEmpty(sessionVal))
        {
            var newSessionId = Guid.NewGuid().ToString("N");
            Response.Headers[SessionHeaderName] = newSessionId;
            return StatusCode(409, "Conflict: Session ID generated.");
        }

        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                { "version", "3.00 (Leecharr)" },
                { "rpc-version", 17 },
                { "rpc-version-minimum", 1 }
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] TransmissionRpcRequest request = null)
    {
        // 1. Transmission CSRF token check
        if (!Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) || string.IsNullOrEmpty(sessionVal))
        {
            var newSessionId = Guid.NewGuid().ToString("N");
            Response.Headers[SessionHeaderName] = newSessionId;
            return StatusCode(409, "Conflict: Session ID generated.");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = null });
        }

        var tag = request.Tag.ValueKind != JsonValueKind.Undefined ? (object)request.Tag : 1;

        try
        {
            switch (request.Method.ToLowerInvariant())
            {
                case "session-get":
                    return Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "version", "3.00 (Leecharr)" },
                            { "rpc-version", 17 },
                            { "rpc-version-minimum", 1 },
                            { "download-dir", _configService.DownloadDir ?? "/downloads" },
                            { "incomplete-dir", _configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
                            { "incomplete-dir-enabled", !string.IsNullOrWhiteSpace(_configService.IncompleteDownloadDir) },
                            { "speed-limit-down", _configService.MaxDownloadSpeedKbps },
                            { "speed-limit-up", _configService.MaxUploadSpeedKbps },
                            { "speed-limit-down-enabled", _configService.MaxDownloadSpeedKbps > 0 },
                            { "speed-limit-up-enabled", _configService.MaxUploadSpeedKbps > 0 },
                            { "peer-port", _configService.ListeningPort }
                        },
                        Tag = tag
                    });

                case "session-set":
                    if (request.Arguments != null)
                    {
                        var updates = new Dictionary<string, object>();

                        if (request.Arguments.TryGetValue("download-dir", out var dlDir) && dlDir.ValueKind == JsonValueKind.String)
                        {
                            updates["DownloadDir"] = dlDir.GetString();
                        }

                        if (request.Arguments.TryGetValue("incomplete-dir", out var incDir) && incDir.ValueKind == JsonValueKind.String)
                        {
                            updates["IncompleteDownloadDir"] = incDir.GetString();
                        }

                        if (request.Arguments.TryGetValue("speed-limit-down", out var dlLimit) && dlLimit.ValueKind == JsonValueKind.Number)
                        {
                            updates["MaxDownloadSpeedKbps"] = dlLimit.GetInt32();
                        }

                        if (request.Arguments.TryGetValue("speed-limit-up", out var upLimit) && upLimit.ValueKind == JsonValueKind.Number)
                        {
                            updates["MaxUploadSpeedKbps"] = upLimit.GetInt32();
                        }

                        if (request.Arguments.TryGetValue("alt-speed-down", out var altDl) && altDl.ValueKind == JsonValueKind.Number)
                        {
                            updates["AltDownloadSpeedKbps"] = altDl.GetInt32();
                        }

                        if (request.Arguments.TryGetValue("alt-speed-up", out var altUp) && altUp.ValueKind == JsonValueKind.Number)
                        {
                            updates["AltUploadSpeedKbps"] = altUp.GetInt32();
                        }

                        if (request.Arguments.TryGetValue("alt-speed-enabled", out var altEn) && (altEn.ValueKind == JsonValueKind.True || altEn.ValueKind == JsonValueKind.False))
                        {
                            updates["SchedulerEnabled"] = altEn.GetBoolean();
                        }

                        if (request.Arguments.TryGetValue("peer-port", out var peerPort) && peerPort.ValueKind == JsonValueKind.Number)
                        {
                            updates["ListeningPort"] = peerPort.GetInt32();
                        }

                        if (updates.Count > 0)
                        {
                            _configService.SaveConfigDictionary(updates);
                        }
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "session-stats":
                    var allTorrents = _torrentService.GetAll().ToList();
                    return Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "activeTorrentCount", allTorrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding) },
                            { "downloadSpeed", allTorrents.Sum(t => t.DownloadSpeed) },
                            { "uploadSpeed", allTorrents.Sum(t => t.UploadSpeed) },
                            { "torrentCount", allTorrents.Count },
                            {
                                "cumulative-stats", new Dictionary<string, object>
                                {
                                    { "downloadedBytes", allTorrents.Sum(t => t.Downloaded) },
                                    { "uploadedBytes", allTorrents.Sum(t => t.Uploaded) }
                                }
                            }
                        },
                        Tag = tag
                    });

                case "torrent-get":
                    var torrents = _torrentService.GetAll();
                    var targetIds = ExtractIds(request.Arguments);
                    if (targetIds.Count > 0)
                    {
                        var targetIdSet = targetIds.ToHashSet();
                        torrents = torrents.Where(t => targetIdSet.Contains(t.Id));
                    }

                    var mappedTorrents = torrents.Select(MapTorrentToTransmission).ToList();
                    return Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "torrents", mappedTorrents }
                        },
                        Tag = tag
                    });

                case "torrent-add":
                    Torrent addedTorrent = null;
                    var isPaused = false;
                    string downloadDir = null;
                    string category = null;
                    if (request.Arguments != null)
                    {
                        if (request.Arguments.TryGetValue("paused", out var pVal))
                        {
                            isPaused = pVal.GetBoolean();
                        }

                        if (request.Arguments.TryGetValue("download-dir", out var ddVal))
                        {
                            downloadDir = ddVal.GetString();
                        }

                        if (request.Arguments.TryGetValue("labels", out var lblsVal) && lblsVal.ValueKind == JsonValueKind.Array && lblsVal.GetArrayLength() > 0)
                        {
                            category = lblsVal[0].GetString();
                        }

                        if (request.Arguments.TryGetValue("metainfo", out var metaVal) && metaVal.ValueKind == JsonValueKind.String)
                        {
                            var b64 = metaVal.GetString();
                            if (!string.IsNullOrWhiteSpace(b64))
                            {
                                var bytes = Convert.FromBase64String(b64);
                                var parsed = _torrentFileParser.Parse(bytes);
                                addedTorrent = await _torrentService.AddFromParsedTorrentAsync(parsed, category, downloadDir, isPaused, bytes);
                            }
                        }
                        else if (request.Arguments.TryGetValue("filename", out var fnVal) && fnVal.ValueKind == JsonValueKind.String)
                        {
                            var fn = fnVal.GetString();
                            if (!string.IsNullOrWhiteSpace(fn))
                            {
                                if (fn.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                                {
                                    addedTorrent = await _torrentService.AddFromMagnetAsync(fn, category, downloadDir, isPaused);
                                }
                                else if (fn.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || fn.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                {
                                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                    var bytes = await httpClient.GetByteArrayAsync(fn);
                                    var parsed = _torrentFileParser.Parse(bytes);
                                    addedTorrent = await _torrentService.AddFromParsedTorrentAsync(parsed, category, downloadDir, isPaused, bytes);
                                }
                            }
                        }
                    }

                    if (addedTorrent != null)
                    {
                        return Ok(new TransmissionRpcResponse
                        {
                            Result = "success",
                            Arguments = new Dictionary<string, object>
                            {
                                { "torrent-added", new { id = addedTorrent.Id, name = addedTorrent.Name, hashString = addedTorrent.InfoHash } }
                            },
                            Tag = tag
                        });
                    }

                    return Ok(new TransmissionRpcResponse { Result = "failed to add torrent", Tag = tag });

                case "torrent-set":
                    var setIds = ExtractIds(request.Arguments);
                    foreach (var id in setIds)
                    {
                        var t = _torrentService.Get(id);
                        if (t != null)
                        {
                            if (request.Arguments.TryGetValue("labels", out var lblVal) && lblVal.ValueKind == JsonValueKind.Array)
                            {
                                var lbls = lblVal.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                                if (lbls.Count > 0)
                                {
                                    t.Category = lbls[0];
                                    t.Label = string.Join(",", lbls);
                                }
                            }

                            if (request.Arguments.TryGetValue("seedRatioLimit", out var ratioVal))
                            {
                                t.TargetRatio = ratioVal.GetDouble();
                            }

                            if (request.Arguments.TryGetValue("downloadLimit", out var dlLimitVal))
                            {
                                t.DownloadLimit = dlLimitVal.GetInt32();
                            }

                            if (request.Arguments.TryGetValue("uploadLimit", out var ulLimitVal))
                            {
                                t.UploadLimit = ulLimitVal.GetInt32();
                            }

                            if (request.Arguments.TryGetValue("location", out var locVal) && locVal.ValueKind == JsonValueKind.String)
                            {
                                t.SavePath = locVal.GetString();
                            }

                            if (request.Arguments.TryGetValue("files-unwanted", out var unwantedVal) && unwantedVal.ValueKind == JsonValueKind.Array)
                            {
                                var files = _torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in unwantedVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await _torrentFileService.SetPriorityAsync(files[idx].Id, 0);
                                        }
                                    }
                                }
                            }

                            if (request.Arguments.TryGetValue("files-wanted", out var wantedVal) && wantedVal.ValueKind == JsonValueKind.Array)
                            {
                                var files = _torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in wantedVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await _torrentFileService.SetPriorityAsync(files[idx].Id, 1);
                                        }
                                    }
                                }
                            }

                            await _torrentService.UpdateAsync(t);
                        }
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-set-location":
                    var locIds = ExtractIds(request.Arguments, false);
                    var newLocation = request.Arguments != null && request.Arguments.TryGetValue("location", out var locElem)
                        ? locElem.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(newLocation))
                    {
                        foreach (var id in locIds)
                        {
                            var t = _torrentService.Get(id);
                            if (t != null)
                            {
                                t.SavePath = newLocation;
                                await _torrentService.UpdateAsync(t);
                            }
                        }
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "free-space":
                    var freePath = request.Arguments != null && request.Arguments.TryGetValue("path", out var pElem)
                        ? pElem.GetString()
                        : (_configService.DownloadDir ?? "/downloads");
                    var freeBytes = _diskSpaceService?.GetDiskSpace()?.FirstOrDefault()?.FreeSpace ?? (100L * 1024 * 1024 * 1024);
                    var totalBytes = _diskSpaceService?.GetDiskSpace()?.FirstOrDefault()?.TotalSpace ?? (500L * 1024 * 1024 * 1024);

                    return Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "path", freePath },
                            { "size-bytes", freeBytes },
                            { "total_size", totalBytes }
                        },
                        Tag = tag
                    });

                case "queue-move-top":
                    var qTopIds = ExtractIds(request.Arguments);
                    foreach (var id in qTopIds)
                    {
                        await _torrentService.MoveQueueAsync(id, "top");
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "queue-move-up":
                    var qUpIds = ExtractIds(request.Arguments);
                    foreach (var id in qUpIds)
                    {
                        await _torrentService.MoveQueueAsync(id, "up");
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "queue-move-down":
                    var qDownIds = ExtractIds(request.Arguments);
                    foreach (var id in qDownIds)
                    {
                        await _torrentService.MoveQueueAsync(id, "down");
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "queue-move-bottom":
                    var qBottomIds = ExtractIds(request.Arguments);
                    foreach (var id in qBottomIds)
                    {
                        await _torrentService.MoveQueueAsync(id, "bottom");
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-start":
                case "torrent-start-now":
                    var startIds = ExtractIds(request.Arguments, true);
                    foreach (var id in startIds)
                    {
                        await _torrentService.ResumeAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-stop":
                    var stopIds = ExtractIds(request.Arguments, true);
                    foreach (var id in stopIds)
                    {
                        await _torrentService.PauseAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-verify":
                    var verifyIds = ExtractIds(request.Arguments, true);
                    foreach (var id in verifyIds)
                    {
                        await _torrentService.ForceRecheckAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-reannounce":
                    var reannounceIds = ExtractIds(request.Arguments, true);
                    foreach (var id in reannounceIds)
                    {
                        await _torrentService.ForceAnnounceAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-remove":
                    var removeIds = ExtractIds(request.Arguments, false);
                    var deleteLocalData = request.Arguments != null && request.Arguments.TryGetValue("delete-local-data", out var delVal) && delVal.GetBoolean();
                    foreach (var id in removeIds)
                    {
                        await _torrentService.DeleteAsync(id, deleteLocalData);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "port-test":
                    return Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object> { { "port-is-open", true } },
                        Tag = tag
                    });

                default:
                    _logger.Debug("Unhandled Transmission RPC method: {0}", request.Method);
                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Transmission RPC method: {0}", request.Method);
            return Ok(new TransmissionRpcResponse { Result = ex.Message, Tag = tag });
        }
    }

    private List<int> ExtractIds(Dictionary<string, JsonElement> arguments, bool applyAllIfEmpty = false)
    {
        var ids = new List<int>();
        if (arguments != null && arguments.TryGetValue("ids", out var idsElem))
        {
            if (idsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number)
                    {
                        ids.Add(item.GetInt32());
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        var str = item.GetString();
                        var torrent = _torrentService.GetByInfoHash(str);
                        if (torrent != null)
                        {
                            ids.Add(torrent.Id);
                        }
                    }
                }
            }
            else if (idsElem.ValueKind == JsonValueKind.Number)
            {
                ids.Add(idsElem.GetInt32());
            }
            else if (idsElem.ValueKind == JsonValueKind.String)
            {
                var str = idsElem.GetString();
                var torrent = _torrentService.GetByInfoHash(str);
                if (torrent != null)
                {
                    ids.Add(torrent.Id);
                }
            }
        }

        if (ids.Count == 0 && applyAllIfEmpty)
        {
            return _torrentService.GetAll().Select(t => t.Id).ToList();
        }

        return ids;
    }

    private Dictionary<string, object> MapTorrentToTransmission(Torrent t)
    {
        var statusNum = t.Status switch
        {
            TorrentStatus.Stopped => 0,
            TorrentStatus.Checking => 2,
            TorrentStatus.Downloading => 4,
            TorrentStatus.Seeding => 6,
            TorrentStatus.Paused => 0,
            _ => 0
        };

        var files = _torrentFileService.GetFiles(t.Id);
        var filesList = files.Select(f => new Dictionary<string, object>
        {
            { "name", f.Path },
            { "bytesCompleted", (long)(f.Size * f.Progress) },
            { "length", f.Size }
        }).ToList();

        var fileStats = files.Select(f => new Dictionary<string, object>
        {
            { "bytesCompleted", (long)(f.Size * f.Progress) },
            { "wanted", f.Priority > 0 },
            { "priority", f.Priority }
        }).ToList();

        var labels = string.IsNullOrWhiteSpace(t.Category)
            ? (string.IsNullOrWhiteSpace(t.Label) ? Array.Empty<string>() : new[] { t.Label })
            : new[] { t.Category };

        var secondsDownloading = (long)(DateTime.UtcNow - t.DateAdded).TotalSeconds;
        var secondsSeeding = t.DateCompleted.HasValue ? (long)(DateTime.UtcNow - t.DateCompleted.Value).TotalSeconds : 0;
        var isError = t.Status == TorrentStatus.Error;

        return new Dictionary<string, object>
        {
            { "id", t.Id },
            { "name", t.Name },
            { "hashString", t.InfoHash },
            { "status", statusNum },
            { "percentDone", t.Progress },
            { "totalSize", t.TotalSize },
            { "leftUntilDone", Math.Max(0, t.TotalSize - t.Downloaded) },
            { "downloadedEver", t.Downloaded },
            { "uploadedEver", t.Uploaded },
            { "rateDownload", t.DownloadSpeed },
            { "rateUpload", t.UploadSpeed },
            { "eta", t.Eta },
            { "uploadRatio", t.Ratio },
            { "peersConnected", t.Leechers },
            { "peersSendingToUs", t.Seeders },
            { "isFinished", t.Progress >= 1.0 },
            { "downloadDir", t.SavePath ?? string.Empty },
            { "labels", labels },
            { "errorString", isError ? "Error" : string.Empty },
            { "error", isError ? 3 : 0 },
            { "secondsDownloading", secondsDownloading },
            { "secondsSeeding", secondsSeeding },
            { "seedRatioLimit", t.TargetRatio },
            { "seedRatioMode", t.TargetRatio > 0 ? 1 : 0 },
            { "seedIdleLimit", 0 },
            { "seedIdleMode", 0 },
            { "fileCount", filesList.Count },
            { "files", filesList },
            { "fileStats", fileStats }
        };
    }
}
