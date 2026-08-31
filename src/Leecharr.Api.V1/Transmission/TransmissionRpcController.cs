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
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public TransmissionRpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _configService = configService;
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

                        if (request.Arguments.TryGetValue("metainfo", out var metaVal) && metaVal.ValueKind == JsonValueKind.String)
                        {
                            var b64 = metaVal.GetString();
                            if (!string.IsNullOrWhiteSpace(b64))
                            {
                                var bytes = Convert.FromBase64String(b64);
                                var parsed = _torrentFileParser.Parse(bytes);
                                addedTorrent = await _torrentService.AddFromParsedTorrentAsync(parsed, null, downloadDir, isPaused, bytes);
                            }
                        }
                        else if (request.Arguments.TryGetValue("filename", out var fnVal) && fnVal.ValueKind == JsonValueKind.String)
                        {
                            var fn = fnVal.GetString();
                            if (!string.IsNullOrWhiteSpace(fn))
                            {
                                if (fn.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                                {
                                    addedTorrent = await _torrentService.AddFromMagnetAsync(fn, null, downloadDir, isPaused);
                                }
                                else if (fn.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || fn.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                {
                                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                    var bytes = await httpClient.GetByteArrayAsync(fn);
                                    var parsed = _torrentFileParser.Parse(bytes);
                                    addedTorrent = await _torrentService.AddFromParsedTorrentAsync(parsed, null, downloadDir, isPaused, bytes);
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

                case "torrent-start":
                case "torrent-start-now":
                    var startIds = ExtractIds(request.Arguments);
                    foreach (var id in startIds)
                    {
                        await _torrentService.ResumeAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-stop":
                    var stopIds = ExtractIds(request.Arguments);
                    foreach (var id in stopIds)
                    {
                        await _torrentService.PauseAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-verify":
                    var verifyIds = ExtractIds(request.Arguments);
                    foreach (var id in verifyIds)
                    {
                        await _torrentService.ForceRecheckAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-reannounce":
                    var reannounceIds = ExtractIds(request.Arguments);
                    foreach (var id in reannounceIds)
                    {
                        await _torrentService.ForceAnnounceAsync(id);
                    }

                    return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });

                case "torrent-remove":
                    var removeIds = ExtractIds(request.Arguments);
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

    private List<int> ExtractIds(Dictionary<string, JsonElement> arguments)
    {
        var ids = new List<int>();
        if (arguments != null && arguments.TryGetValue("ids", out var idsElem))
        {
            if (idsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var id))
                    {
                        ids.Add(id);
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
            else if (idsElem.ValueKind == JsonValueKind.Number && idsElem.TryGetInt32(out var singleId))
            {
                ids.Add(singleId);
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

        return new Dictionary<string, object>
        {
            { "id", t.Id },
            { "name", t.Name },
            { "hashString", t.InfoHash },
            { "status", statusNum },
            { "percentDone", t.Progress },
            { "totalSize", t.TotalSize },
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
            { "files", filesList },
            { "fileStats", fileStats }
        };
    }
}
