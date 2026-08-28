using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Hadouken;

public class HadoukenRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;
}

[AllowAnonymous]
[ApiController]
public class HadoukenRpcController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public HadoukenRpcController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService,
        ITorrentFileService torrentFileService = null)
    {
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _configService = configService;
        _torrentFileService = torrentFileService;
    }

    [HttpPost]
    [Route("api")]
    [Route("api/hadouken")]
    [Route("api/rpc")]
    [Route("hadouken/api")]
    [Route("hadouken/rpc")]
    [Route("hadouken")]
    public async Task<IActionResult> HandleRpc([FromBody] HadoukenRpcRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(new { result = (object)null, error = "Invalid request", id = (object)1 });
        }

        var id = request.Id ?? 1;

        try
        {
            switch (request.Method.ToLowerInvariant())
            {
                case "core.getsysteminfo":
                case "core.get_system_info":
                    return Ok(new
                    {
                        result = new
                        {
                            committish = "5.3.0",
                            branch = "master",
                            versions = new Dictionary<string, string>
                            {
                                { "hadouken", "5.3.0" },
                                { "libtorrent", "1.2.14" }
                            }
                        },
                        error = (object)null,
                        id
                    });

                case "webui.getsettings":
                case "webui.get_settings":
                    return Ok(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "bittorrent.default_save_path", _configService.DownloadDir ?? "/downloads" }
                        },
                        error = (object)null,
                        id
                    });

                case "webui.list":
                    var allTorrents = _torrentService.GetAll().ToList();
                    var torrentRows = new List<object[]>();
                    foreach (var t in allTorrents)
                    {
                        var isFinished = t.Progress >= 1.0;
                        int statusFlag;
                        if (t.Status == TorrentStatus.Downloading)
                        {
                            statusFlag = 1 | 2;
                        }
                        else if (t.Status == TorrentStatus.Seeding)
                        {
                            statusFlag = 1 | 8 | 16 | (isFinished ? 128 : 0);
                        }
                        else if (t.Status == TorrentStatus.Paused)
                        {
                            statusFlag = 1 | 4 | 16 | (isFinished ? 128 : 0);
                        }
                        else if (t.Status == TorrentStatus.Stopped)
                        {
                            statusFlag = 1 | 16 | (isFinished ? 128 : 0);
                        }
                        else if (t.Status == TorrentStatus.Error)
                        {
                            statusFlag = 1 | 8 | 16;
                        }
                        else
                        {
                            statusFlag = 1 | 2 | 16;
                        }

                        var addedUnix = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds();
                        var completedUnix = t.DateCompleted.HasValue ? new DateTimeOffset(t.DateCompleted.Value).ToUnixTimeSeconds() : 0;
                        var modifiedUnix = t.LastActive.HasValue ? new DateTimeOffset(t.LastActive.Value).ToUnixTimeSeconds() : addedUnix;

                        torrentRows.Add(new object[]
                        {
                            t.InfoHash.ToUpperInvariant(),
                            statusFlag,
                            t.Name ?? string.Empty,
                            t.TotalSize,
                            (int)(t.Progress * 1000),
                            t.Downloaded,
                            t.Uploaded,
                            (int)(t.Ratio * 1000),
                            t.UploadSpeed,
                            t.DownloadSpeed,
                            t.Eta,
                            t.Category ?? string.Empty,
                            t.Leechers,
                            t.Leechers,
                            t.Seeders,
                            t.Seeders,
                            65536,
                            0,
                            Math.Max(0, t.TotalSize - t.Downloaded),
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            addedUnix,
                            completedUnix,
                            string.Empty,
                            t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                            string.Empty,
                            modifiedUnix
                        });
                    }

                    return Ok(new
                    {
                        result = new
                        {
                            torrents = torrentRows,
                            torrentc = "1"
                        },
                        error = (object)null,
                        id
                    });

                case "webui.addtorrent":
                case "webui.add_torrent":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var type = request.Params[0].GetString();
                        var data = request.Params[1].GetString();
                        string savePath = null;
                        string category = null;
                        var isPaused = false;

                        if (request.Params.GetArrayLength() >= 3 && request.Params[2].ValueKind == JsonValueKind.Object)
                        {
                            var opts = request.Params[2];
                            if (opts.TryGetProperty("save_path", out var spProp))
                            {
                                savePath = spProp.GetString();
                            }

                            if (opts.TryGetProperty("label", out var lblProp))
                            {
                                category = lblProp.GetString();
                            }

                            if (opts.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array && tagsProp.GetArrayLength() > 0)
                            {
                                category = tagsProp[0].GetString();
                            }

                            if (opts.TryGetProperty("paused", out var pProp))
                            {
                                isPaused = pProp.GetBoolean();
                            }
                        }

                        if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(data))
                        {
                            var bytes = Convert.FromBase64String(data);
                            var parsed = _torrentFileParser.Parse(bytes);
                            var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                            return Ok(new { result = added?.InfoHash, error = (object)null, id });
                        }
                        else if (string.Equals(type, "url", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(data))
                        {
                            if (data.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await _torrentService.AddFromMagnetAsync(data, category, savePath, isPaused);
                                return Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                            else
                            {
                                using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                var bytes = await client.GetByteArrayAsync(data);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                                return Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "webui.perform":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var action = request.Params[0].GetString()?.ToLowerInvariant();
                        var hashesElem = request.Params[1];
                        var targetHashes = new List<string>();
                        if (hashesElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var h in hashesElem.EnumerateArray())
                            {
                                if (h.ValueKind == JsonValueKind.String)
                                {
                                    targetHashes.Add(h.GetString());
                                }
                            }
                        }
                        else if (hashesElem.ValueKind == JsonValueKind.String)
                        {
                            targetHashes.Add(hashesElem.GetString());
                        }

                        foreach (var targetHash in targetHashes)
                        {
                            var t = _torrentService.GetByInfoHash(targetHash);
                            if (t != null)
                            {
                                switch (action)
                                {
                                    case "pause":
                                        await _torrentService.PauseAsync(t.Id);
                                        break;
                                    case "resume":
                                    case "start":
                                        await _torrentService.ResumeAsync(t.Id);
                                        break;
                                    case "recheck":
                                        await _torrentService.ForceRecheckAsync(t.Id);
                                        break;
                                    case "remove":
                                        await _torrentService.DeleteAsync(t.Id, false);
                                        break;
                                    case "removedata":
                                        await _torrentService.DeleteAsync(t.Id, true);
                                        break;
                                }
                            }
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "torrents.get_files":
                case "webui.getfiles":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0 && _torrentFileService != null)
                    {
                        var reqHash = request.Params[0].GetString();
                        if (!string.IsNullOrWhiteSpace(reqHash))
                        {
                            var t = _torrentService.GetByInfoHash(reqHash);
                            if (t != null)
                            {
                                var files = _torrentFileService.GetFiles(t.Id).ToList();
                                var fileRes = files.Select((f, idx) => new
                                {
                                    index = idx,
                                    path = f.Path,
                                    size = f.Size,
                                    progress = f.Progress,
                                    priority = f.Priority
                                });

                                return Ok(new { result = fileRes, error = (object)null, id });
                            }
                        }
                    }

                    return Ok(new { result = new object[] { }, error = (object)null, id });

                case "core.getversion":
                case "hadouken.getversion":
                    return Ok(new { result = "5.3.0", error = (object)null, id });

                case "torrents.adduri":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0)
                    {
                        var uri = request.Params[0].GetString();
                        string savePath = null;
                        string category = null;
                        var isPaused = false;

                        if (request.Params.GetArrayLength() > 1 && request.Params[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = request.Params[1];
                            if (opts.TryGetProperty("save_path", out var spProp))
                            {
                                savePath = spProp.GetString();
                            }

                            if (opts.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array && tagsProp.GetArrayLength() > 0)
                            {
                                category = tagsProp[0].GetString();
                            }

                            if (opts.TryGetProperty("paused", out var pProp))
                            {
                                isPaused = pProp.GetBoolean();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            if (uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await _torrentService.AddFromMagnetAsync(uri, category, savePath, isPaused);
                                return Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                            else
                            {
                                using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                var bytes = await client.GetByteArrayAsync(uri);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                                return Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "torrents.pause":
                    var hashToPause = GetFirstStringParam(request.Params);
                    var tPause = _torrentService.GetByInfoHash(hashToPause);
                    if (tPause != null)
                    {
                        await _torrentService.PauseAsync(tPause.Id);
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "torrents.resume":
                    var hashToResume = GetFirstStringParam(request.Params);
                    var tResume = _torrentService.GetByInfoHash(hashToResume);
                    if (tResume != null)
                    {
                        await _torrentService.ResumeAsync(tResume.Id);
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "torrents.delete":
                    var hashToDelete = GetFirstStringParam(request.Params);
                    var tDelete = _torrentService.GetByInfoHash(hashToDelete);
                    if (tDelete != null)
                    {
                        var deleteData = false;
                        if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 1)
                        {
                            var p1 = request.Params[1];
                            if (p1.ValueKind == JsonValueKind.True)
                            {
                                deleteData = true;
                            }
                            else if (p1.ValueKind == JsonValueKind.Object && p1.TryGetProperty("delete_data", out var ddProp) && ddProp.ValueKind == JsonValueKind.True)
                            {
                                deleteData = true;
                            }
                        }

                        await _torrentService.DeleteAsync(tDelete.Id, deleteData);
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "torrents.set_props":
                case "torrents.setprops":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var targetHash = request.Params[0].GetString();
                        var tProps = _torrentService.GetByInfoHash(targetHash);
                        if (tProps != null && request.Params[1].ValueKind == JsonValueKind.Object)
                        {
                            var propsObj = request.Params[1];
                            if (propsObj.TryGetProperty("tags", out var tProp) && tProp.ValueKind == JsonValueKind.Array && tProp.GetArrayLength() > 0)
                            {
                                tProps.Category = tProp[0].GetString();
                            }

                            if (propsObj.TryGetProperty("download_limit", out var dlProp))
                            {
                                tProps.DownloadLimit = dlProp.GetInt32();
                            }

                            if (propsObj.TryGetProperty("upload_limit", out var ulProp))
                            {
                                tProps.UploadLimit = ulProp.GetInt32();
                            }

                            await _torrentService.UpdateAsync(tProps);
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                default:
                    _logger.Debug("Unhandled Hadouken RPC method: {0}", request.Method);
                    return Ok(new { result = true, error = (object)null, id });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in Hadouken RPC: {0}", request.Method);
            return Ok(new { result = (object)null, error = ex.Message, id });
        }
    }

    private static string GetFirstStringParam(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
        {
            return parameters[0].GetString() ?? string.Empty;
        }

        if (parameters.ValueKind == JsonValueKind.String)
        {
            return parameters.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
