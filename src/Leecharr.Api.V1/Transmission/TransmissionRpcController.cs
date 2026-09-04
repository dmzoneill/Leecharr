// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Http;
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

[ApiController]
[Route("transmission/rpc")]
public class TransmissionRpcController : ControllerBase
{
    private const string SessionHeaderName = "X-Transmission-Session-Id";
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly IConfigService configService;
    private readonly IDiskSpaceService diskSpaceService;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public TransmissionRpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService,
        IDiskSpaceService diskSpaceService = null,
        ISafeHttpClientService safeHttpClientService = null,
        IConfigFileProvider configFileProvider = null,
        IDiskProvider diskProvider = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.configService = configService;
        this.diskSpaceService = diskSpaceService;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.configFileProvider = configFileProvider;
        this.diskProvider = diskProvider;
    }

    [HttpGet]
    public IActionResult HandleGet()
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Transmission\"";
            return this.Unauthorized();
        }

        if (!this.Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) || string.IsNullOrEmpty(sessionVal))
        {
            var newSessionId = Guid.NewGuid().ToString("N");
            this.Response.Headers[SessionHeaderName] = newSessionId;
            return this.StatusCode(409, "Conflict: Session ID generated.");
        }

        return this.Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                { "version", "3.00 (Leecharr)" },
                { "rpc-version", 17 },
                { "rpc-version-minimum", 1 }
            },
        });
    }

    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] TransmissionRpcRequest request = null)
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Transmission\"";
            return this.Unauthorized();
        }

        // 1. Transmission CSRF token check
        if (!this.Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) || string.IsNullOrEmpty(sessionVal))
        {
            var newSessionId = Guid.NewGuid().ToString("N");
            this.Response.Headers[SessionHeaderName] = newSessionId;
            return this.StatusCode(409, "Conflict: Session ID generated.");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = null });
        }

        var tag = request.Tag.ValueKind != JsonValueKind.Undefined ? (object)request.Tag : 1;

        try
        {
            switch (request.Method.ToLowerInvariant())
            {
                case "session-get":
                    return this.Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "version", "3.00 (Leecharr)" },
                            { "rpc-version", 17 },
                            { "rpc-version-minimum", 1 },
                            { "download-dir", this.configService.DownloadDir ?? "/downloads" },
                            { "incomplete-dir", this.configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
                            { "incomplete-dir-enabled", !string.IsNullOrWhiteSpace(this.configService.IncompleteDownloadDir) },
                            { "speed-limit-down", this.configService.MaxDownloadSpeedKbps },
                            { "speed-limit-up", this.configService.MaxUploadSpeedKbps },
                            { "speed-limit-down-enabled", this.configService.MaxDownloadSpeedKbps > 0 },
                            { "speed-limit-up-enabled", this.configService.MaxUploadSpeedKbps > 0 },
                            { "peer-port", this.configService.ListeningPort },
                        },
                        Tag = tag,
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
                            this.configService.SaveConfigDictionary(updates);
                        }
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "session-stats":
                    var allTorrents = this.torrentService.GetAll().ToList();
                    return this.Ok(new TransmissionRpcResponse
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
                            },
                        },
                        Tag = tag,
                    });

                case "torrent-get":
                    var torrents = this.torrentService.GetAll();
                    var targetIds = this.ExtractIds(request.Arguments);
                    if (targetIds.Count > 0)
                    {
                        var targetIdSet = targetIds.ToHashSet();
                        torrents = torrents.Where(t => targetIdSet.Contains(t.Id));
                    }

                    HashSet<string> requestedFields = null;
                    if (request.Arguments != null && request.Arguments.TryGetValue("fields", out var fieldsVal) && fieldsVal.ValueKind == JsonValueKind.Array)
                    {
                        requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in fieldsVal.EnumerateArray())
                        {
                            if (f.ValueKind == JsonValueKind.String)
                            {
                                requestedFields.Add(f.GetString());
                            }
                        }
                    }

                    var mappedTorrents = torrents.Select(t => this.MapTorrentToTransmission(t, requestedFields)).ToList();
                    return this.Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "torrents", mappedTorrents },
                        },
                        Tag = tag,
                    });

                case "torrent-add":
                    return await this.HandleTorrentAddAsync(request, tag);

                case "torrent-set":
                    var setIds = this.ExtractIds(request.Arguments);
                    foreach (var id in setIds)
                    {
                        var t = this.torrentService.Get(id);
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

                            if (request.Arguments.TryGetValue("seedIdleLimit", out var idleVal) && idleVal.ValueKind == JsonValueKind.Number)
                            {
                                t.TargetSeedTimeMinutes = idleVal.GetInt32();
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
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in unwantedVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, 0);
                                        }
                                    }
                                }
                            }

                            if (request.Arguments.TryGetValue("files-wanted", out var wantedVal) && wantedVal.ValueKind == JsonValueKind.Array)
                            {
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in wantedVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, 1);
                                        }
                                    }
                                }
                            }

                            if (request.Arguments.TryGetValue("priority-high", out var prioHighVal) && prioHighVal.ValueKind == JsonValueKind.Array)
                            {
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in prioHighVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, 2);
                                        }
                                    }
                                }
                            }

                            if (request.Arguments.TryGetValue("priority-low", out var prioLowVal) && prioLowVal.ValueKind == JsonValueKind.Array)
                            {
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in prioLowVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, 0);
                                        }
                                    }
                                }
                            }

                            if (request.Arguments.TryGetValue("priority-normal", out var prioNormVal) && prioNormVal.ValueKind == JsonValueKind.Array)
                            {
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                foreach (var item in prioNormVal.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        var idx = item.GetInt32();
                                        if (idx >= 0 && idx < files.Count)
                                        {
                                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, 1);
                                        }
                                    }
                                }
                            }

                            await this.torrentService.UpdateAsync(t);
                        }
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-set-location":
                    var locIds = this.ExtractIds(request.Arguments, false);
                    var newLocation = request.Arguments != null && request.Arguments.TryGetValue("location", out var locElem)
                        ? locElem.GetString()
                        : null;
                    var shouldMove = true;
                    if (request.Arguments != null && request.Arguments.TryGetValue("move", out var moveElem))
                    {
                        if (moveElem.ValueKind == JsonValueKind.True || moveElem.ValueKind == JsonValueKind.False)
                        {
                            shouldMove = moveElem.GetBoolean();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(newLocation))
                    {
                        foreach (var id in locIds)
                        {
                            await this.torrentService.SetLocationAsync(id, newLocation, shouldMove);
                        }
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "free-space":
                    var freePath = request.Arguments != null && request.Arguments.TryGetValue("path", out var pElem)
                        ? pElem.GetString()
                        : (this.configService.DownloadDir ?? "/downloads");

                    long? freeBytes = null;
                    long? totalBytes = null;

                    if (!string.IsNullOrWhiteSpace(freePath) && this.diskProvider != null)
                    {
                        try
                        {
                            freeBytes = this.diskProvider.GetAvailableSpace(freePath);
                            totalBytes = this.diskProvider.GetTotalSize(freePath);
                        }
                        catch
                        {
                            // Fall through to fallback
                        }
                    }

                    freeBytes ??= this.diskSpaceService?.GetDiskSpace()?.FirstOrDefault()?.FreeSpace ?? (100L * 1024 * 1024 * 1024);
                    totalBytes ??= this.diskSpaceService?.GetDiskSpace()?.FirstOrDefault()?.TotalSpace ?? (500L * 1024 * 1024 * 1024);

                    return this.Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object>
                        {
                            { "path", freePath },
                            { "size-bytes", freeBytes },
                            { "total_size", totalBytes },
                        },
                        Tag = tag,
                    });

                case "queue-move-top":
                    var qTopIds = this.ExtractIds(request.Arguments);
                    foreach (var id in qTopIds)
                    {
                        await this.torrentService.MoveQueueAsync(id, "top");
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "queue-move-up":
                    var qUpIds = this.ExtractIds(request.Arguments);
                    foreach (var id in qUpIds)
                    {
                        await this.torrentService.MoveQueueAsync(id, "up");
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "queue-move-down":
                    var qDownIds = this.ExtractIds(request.Arguments);
                    foreach (var id in qDownIds)
                    {
                        await this.torrentService.MoveQueueAsync(id, "down");
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "queue-move-bottom":
                    var qBottomIds = this.ExtractIds(request.Arguments);
                    foreach (var id in qBottomIds)
                    {
                        await this.torrentService.MoveQueueAsync(id, "bottom");
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-start":
                case "torrent-start-now":
                    var startIds = this.ExtractIds(request.Arguments, true);
                    foreach (var id in startIds)
                    {
                        await this.torrentService.ResumeAsync(id);
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-stop":
                    var stopIds = this.ExtractIds(request.Arguments, true);
                    foreach (var id in stopIds)
                    {
                        await this.torrentService.PauseAsync(id);
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-verify":
                    var verifyIds = this.ExtractIds(request.Arguments, true);
                    foreach (var id in verifyIds)
                    {
                        await this.torrentService.ForceRecheckAsync(id);
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-reannounce":
                    var reannounceIds = this.ExtractIds(request.Arguments, true);
                    foreach (var id in reannounceIds)
                    {
                        await this.torrentService.ForceAnnounceAsync(id);
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-remove":
                    var removeIds = this.ExtractIds(request.Arguments, false);
                    var deleteLocalData = request.Arguments != null && request.Arguments.TryGetValue("delete-local-data", out var delVal) && delVal.GetBoolean();
                    foreach (var id in removeIds)
                    {
                        await this.torrentService.DeleteAsync(id, deleteLocalData);
                    }

                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "port-test":
                    return this.Ok(new TransmissionRpcResponse
                    {
                        Result = "success",
                        Arguments = new Dictionary<string, object> { { "port-is-open", true } },
                        Tag = tag,
                    });

                default:
                    this.logger.Debug("Unhandled Transmission RPC method: {0}", request.Method);
                    return this.Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling Transmission RPC method: {0}", request.Method);
            return this.Ok(new TransmissionRpcResponse { Result = ex.Message, Tag = tag });
        }
    }

    private async Task<IActionResult> HandleTorrentAddAsync(TransmissionRpcRequest request, object tag)
    {
        Torrent addedTorrent = null;
        var isDuplicate = false;
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
                    var parsed = this.torrentFileParser.Parse(bytes);
                    var existing = !string.IsNullOrWhiteSpace(parsed?.InfoHash)
                        ? this.torrentService.GetByInfoHash(parsed.InfoHash)
                        : null;

                    if (existing != null)
                    {
                        isDuplicate = true;
                        addedTorrent = existing;
                    }
                    else
                    {
                        addedTorrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, downloadDir, isPaused, bytes);
                    }
                }
            }
            else if (request.Arguments.TryGetValue("filename", out var fnVal) && fnVal.ValueKind == JsonValueKind.String)
            {
                var fn = fnVal.GetString();
                if (!string.IsNullOrWhiteSpace(fn))
                {
                    if (fn.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var parsedMagnet = MagnetLinkParser.Parse(fn);
                            if (!string.IsNullOrWhiteSpace(parsedMagnet?.InfoHash))
                            {
                                var existing = this.torrentService.GetByInfoHash(parsedMagnet.InfoHash);
                                if (existing != null)
                                {
                                    isDuplicate = true;
                                    addedTorrent = existing;
                                }
                            }
                        }
                        catch
                        {
                            // Ignore magnet parsing error, defer to AddFromMagnetAsync
                        }

                        if (addedTorrent == null)
                        {
                            addedTorrent = await this.torrentService.AddFromMagnetAsync(fn, category, downloadDir, isPaused);
                        }
                    }
                    else if (fn.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || fn.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = await this.safeHttpClientService.DownloadBytesAsync(fn);
                        var parsed = this.torrentFileParser.Parse(bytes);
                        var existing = !string.IsNullOrWhiteSpace(parsed?.InfoHash)
                            ? this.torrentService.GetByInfoHash(parsed.InfoHash)
                            : null;

                        if (existing != null)
                        {
                            isDuplicate = true;
                            addedTorrent = existing;
                        }
                        else
                        {
                            addedTorrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, downloadDir, isPaused, bytes);
                        }
                    }
                    else if (global::System.IO.File.Exists(fn))
                    {
                        var bytes = await global::System.IO.File.ReadAllBytesAsync(fn);
                        var parsed = this.torrentFileParser.Parse(bytes);
                        var existing = !string.IsNullOrWhiteSpace(parsed?.InfoHash)
                            ? this.torrentService.GetByInfoHash(parsed.InfoHash)
                            : null;

                        if (existing != null)
                        {
                            isDuplicate = true;
                            addedTorrent = existing;
                        }
                        else
                        {
                            addedTorrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, downloadDir, isPaused, bytes);
                        }
                    }
                }
            }
        }

        if (addedTorrent != null)
        {
            var responseKey = isDuplicate ? "torrent-duplicate" : "torrent-added";
            return this.Ok(new TransmissionRpcResponse
            {
                Result = "success",
                Arguments = new Dictionary<string, object>
                {
                    { responseKey, new { id = addedTorrent.Id, name = addedTorrent.Name, hashString = addedTorrent.InfoHash } },
                },
                Tag = tag,
            });
        }

        return this.Ok(new TransmissionRpcResponse { Result = "failed to add torrent", Tag = tag });
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
                        var torrent = this.torrentService.GetByInfoHash(str);
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
                var torrent = this.torrentService.GetByInfoHash(str);
                if (torrent != null)
                {
                    ids.Add(torrent.Id);
                }
            }
        }

        if (ids.Count == 0 && applyAllIfEmpty)
        {
            return this.torrentService.GetAll().Select(t => t.Id).ToList();
        }

        return ids;
    }

    private Dictionary<string, object> MapTorrentToTransmission(Torrent t, ISet<string> requestedFields = null)
    {
        var statusNum = t.Status switch
        {
            TorrentStatus.Stopped => 0,
            TorrentStatus.Checking => 2,
            TorrentStatus.Downloading => 4,
            TorrentStatus.Seeding => 6,
            TorrentStatus.Paused => 0,
            _ => 0,
        };

        var needsFiles = requestedFields == null || requestedFields.Count == 0 ||
            requestedFields.Contains("files") || requestedFields.Contains("priorities") || requestedFields.Contains("fileStats") || requestedFields.Contains("fileCount") || requestedFields.Contains("file-count");

        List<Dictionary<string, object>> filesList;
        List<Dictionary<string, object>> fileStats;
        List<int> priorities;
        int fileCount;

        if (needsFiles)
        {
            var files = this.torrentFileService.GetFiles(t.Id).ToList();
            fileCount = files.Count;
            filesList = files.Select(f => new Dictionary<string, object>
            {
                { "name", f.Path },
                { "bytesCompleted", (long)(f.Size * f.Progress) },
                { "length", f.Size },
            }).ToList();

            fileStats = files.Select(f => new Dictionary<string, object>
            {
                { "bytesCompleted", (long)(f.Size * f.Progress) },
                { "wanted", f.Priority > 0 },
                { "priority", f.Priority },
            }).ToList();

            priorities = files.Select(f => f.Priority).ToList();
        }
        else
        {
            filesList = new List<Dictionary<string, object>>();
            fileStats = new List<Dictionary<string, object>>();
            priorities = new List<int>();
            fileCount = 0;
        }

        var labels = string.IsNullOrWhiteSpace(t.Category)
            ? (string.IsNullOrWhiteSpace(t.Label) ? Array.Empty<string>() : new[] { t.Label })
            : new[] { t.Category };

        var secondsDownloading = (long)(DateTime.UtcNow - t.DateAdded).TotalSeconds;
        var secondsSeeding = t.DateCompleted.HasValue ? (long)(DateTime.UtcNow - t.DateCompleted.Value).TotalSeconds : 0;
        var isError = t.Status == TorrentStatus.Error;

        var dict = new Dictionary<string, object>
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
            { "seedIdleLimit", t.TargetSeedTimeMinutes },
            { "seedIdleMode", t.TargetSeedTimeMinutes > 0 ? 1 : 0 },
            { "fileCount", fileCount },
            { "file-count", fileCount },
            { "isPrivate", t.IsPrivate },
            { "files", filesList },
            { "fileStats", fileStats },
            { "priorities", priorities },
        };

        if (requestedFields != null && requestedFields.Count > 0)
        {
            var filtered = new Dictionary<string, object>();
            foreach (var field in requestedFields)
            {
                if (dict.TryGetValue(field, out var val))
                {
                    filtered[field] = val;
                }
            }

            return filtered;
        }

        return dict;
    }
}
