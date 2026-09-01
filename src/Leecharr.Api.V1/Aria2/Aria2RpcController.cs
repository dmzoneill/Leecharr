using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Aria2;

public class Aria2RpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;
}

[AllowAnonymous]
[ApiController]
[Route("jsonrpc")]
[Route("rpc")]
[Route("aria2/jsonrpc")]
[Route("aria2/rpc")]
public class Aria2RpcController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public Aria2RpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _categoryService = categoryService;
        _configService = configService;
    }

    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] Aria2RpcRequest request = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(new
            {
                jsonrpc = "2.0",
                id = (object)1,
                result = new
                {
                    version = "1.36.0",
                    enabledFeatures = new[] { "BitTorrent", "GZip", "HTTPS", "MessageDigest", "Async DNS" }
                }
            });
        }

        var id = request.Id ?? 1;

        try
        {
            var res = await ExecuteMethodAsync(request.Method, request.Params);
            return Ok(new
            {
                jsonrpc = "2.0",
                id,
                result = res
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Aria2 RPC method: {0}", request.Method);
            return Ok(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = 1, message = ex.Message }
            });
        }
    }

    private async Task<object> ExecuteMethodAsync(string method, JsonElement parameters)
    {
        switch (method.ToLowerInvariant())
        {
            case "aria2.getversion":
                return new
                {
                    version = "1.36.0",
                    enabledFeatures = new[] { "BitTorrent", "GZip", "HTTPS", "MessageDigest", "Async DNS" }
                };

            case "aria2.getglobalstat":
                var allT = _torrentService.GetAll().ToList();
                return new
                {
                    downloadSpeed = allT.Sum(t => t.DownloadSpeed).ToString(),
                    uploadSpeed = allT.Sum(t => t.UploadSpeed).ToString(),
                    numActive = allT.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding).ToString(),
                    numWaiting = allT.Count(t => t.Status == TorrentStatus.Queued).ToString(),
                    numStopped = allT.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped).ToString()
                };

            case "aria2.tellactive":
                var activeTorrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding)
                    .Select(MapTorrentToAria2)
                    .ToList();
                return activeTorrents;

            case "aria2.tellwaiting":
                var waitingTorrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Queued)
                    .Select(MapTorrentToAria2)
                    .ToList();
                return waitingTorrents;

            case "aria2.tellstopped":
                var stoppedTorrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped)
                    .Select(MapTorrentToAria2)
                    .ToList();
                return stoppedTorrents;

            case "aria2.tellstatus":
                var gid = GetFirstStringParam(parameters);
                var torrent = FindByGid(gid);
                return torrent != null ? MapTorrentToAria2(torrent) : new object();

            case "aria2.addtorrent":
                if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
                {
                    var b64 = parameters[0].GetString();
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        var bytes = Convert.FromBase64String(b64);
                        var parsed = _torrentFileParser.Parse(bytes);
                        var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, bytes);
                        return added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16];
                    }
                }

                return Guid.NewGuid().ToString("N")[..16];

            case "aria2.adduri":
                if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
                {
                    var urisArray = parameters[0];
                    if (urisArray.ValueKind == JsonValueKind.Array && urisArray.GetArrayLength() > 0)
                    {
                        var uri = urisArray[0].GetString();
                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            if (uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await _torrentService.AddFromMagnetAsync(uri, null, null, false);
                                return added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16];
                            }
                            else
                            {
                                using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                var bytes = await client.GetByteArrayAsync(uri);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, bytes);
                                return added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16];
                            }
                        }
                    }
                }

                return Guid.NewGuid().ToString("N")[..16];

            case "aria2.remove":
            case "aria2.forceremove":
                var removeGid = GetFirstStringParam(parameters);
                var toRemove = FindByGid(removeGid);
                if (toRemove != null)
                {
                    await _torrentService.DeleteAsync(toRemove.Id, false);
                }

                return removeGid;

            case "aria2.pause":
            case "aria2.forcepause":
                var pauseGid = GetFirstStringParam(parameters);
                var toPause = FindByGid(pauseGid);
                if (toPause != null)
                {
                    await _torrentService.PauseAsync(toPause.Id);
                }

                return pauseGid;

            case "aria2.unpause":
            case "aria2.forceunpause":
                var unpauseGid = GetFirstStringParam(parameters);
                var toUnpause = FindByGid(unpauseGid);
                if (toUnpause != null)
                {
                    await _torrentService.ResumeAsync(toUnpause.Id);
                }

                return unpauseGid;

            case "aria2.getfiles":
                var filesGid = GetFirstStringParam(parameters);
                var tForFiles = FindByGid(filesGid);
                if (tForFiles != null)
                {
                    var files = _torrentFileService.GetFiles(tForFiles.Id);
                    return files.Select((f, idx) => new
                    {
                        index = (idx + 1).ToString(),
                        path = f.Path,
                        length = f.Size.ToString(),
                        completedLength = (f.Size * tForFiles.Progress).ToString("0"),
                        selected = f.Priority > 0 ? "true" : "false"
                    }).ToList();
                }

                return new List<object>();

            case "system.multicall":
                if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
                {
                    var calls = parameters[0];
                    var results = new List<object>();
                    if (calls.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var call in calls.EnumerateArray())
                        {
                            if (call.TryGetProperty("methodName", out var mn) && call.TryGetProperty("params", out var p))
                            {
                                var subRes = await ExecuteMethodAsync(mn.GetString() ?? string.Empty, p);
                                results.Add(new object[] { subRes });
                            }
                        }
                    }

                    return results;
                }

                return new List<object>();

            case "system.listmethods":
                return new[]
                {
                    "aria2.addUri",
                    "aria2.addTorrent",
                    "aria2.remove",
                    "aria2.forceRemove",
                    "aria2.pause",
                    "aria2.forcePause",
                    "aria2.unpause",
                    "aria2.forceUnpause",
                    "aria2.tellStatus",
                    "aria2.getUris",
                    "aria2.getFiles",
                    "aria2.getPeers",
                    "aria2.getServers",
                    "aria2.tellActive",
                    "aria2.tellWaiting",
                    "aria2.tellStopped",
                    "aria2.getVersion",
                    "aria2.getGlobalStat",
                    "system.multicall",
                    "system.listMethods"
                };

            default:
                _logger.Debug("Unhandled Aria2 RPC method: {0}", method);
                return "OK";
        }
    }

    private Torrent FindByGid(string gid)
    {
        if (string.IsNullOrWhiteSpace(gid))
        {
            return null;
        }

        var clean = gid.Trim();
        var all = _torrentService.GetAll().ToList();
        return all.FirstOrDefault(t => t.InfoHash.StartsWith(clean, StringComparison.OrdinalIgnoreCase) || t.InfoHash.Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, object> MapTorrentToAria2(Torrent t)
    {
        var gid = t.InfoHash.Length >= 16 ? t.InfoHash[..16] : t.InfoHash;
        var status = t.Status switch
        {
            TorrentStatus.Downloading => "active",
            TorrentStatus.Seeding => "active",
            TorrentStatus.Paused => "paused",
            TorrentStatus.Stopped => "complete",
            TorrentStatus.Error => "error",
            _ => "waiting"
        };

        return new Dictionary<string, object>
        {
            { "gid", gid },
            { "status", status },
            { "totalLength", t.TotalSize.ToString() },
            { "completedLength", t.Downloaded.ToString() },
            { "uploadLength", t.Uploaded.ToString() },
            { "downloadSpeed", t.DownloadSpeed.ToString() },
            { "uploadSpeed", t.UploadSpeed.ToString() },
            { "infoHash", t.InfoHash.ToLowerInvariant() },
            { "numSeeders", t.Seeders.ToString() },
            { "connections", (t.Leechers + t.Seeders).ToString() },
            { "dir", t.SavePath ?? (_configService.DownloadDir ?? "/downloads") },
            {
                "bittorrent", new Dictionary<string, object>
                {
                    { "info", new Dictionary<string, string> { { "name", t.Name ?? string.Empty } } },
                    { "mode", "multi" }
                }
            },
            {
                "files", new object[]
                {
                    new
                    {
                        index = "1",
                        path = global::System.IO.Path.Combine(t.SavePath ?? "/downloads", t.Name ?? string.Empty),
                        length = t.TotalSize.ToString(),
                        completedLength = t.Downloaded.ToString(),
                        selected = "true"
                    }
                }
            }
        };
    }

    private static string GetFirstStringParam(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
        {
            var first = parameters[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return first.GetString() ?? string.Empty;
            }
        }
        else if (parameters.ValueKind == JsonValueKind.String)
        {
            return parameters.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
