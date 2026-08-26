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
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public HadoukenRpcController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _configService = configService;
    }

    [HttpPost]
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
                case "core.getversion":
                case "hadouken.getversion":
                    return Ok(new { result = "5.3.0", error = (object)null, id });

                case "torrents.list":
                    var all = _torrentService.GetAll().ToList();
                    var dict = new Dictionary<string, object>();
                    foreach (var t in all)
                    {
                        dict[t.InfoHash.ToLowerInvariant()] = new
                        {
                            infoHash = t.InfoHash.ToLowerInvariant(),
                            name = t.Name ?? string.Empty,
                            totalSize = t.TotalSize,
                            progress = t.Progress,
                            downloadRate = t.DownloadSpeed,
                            uploadRate = t.UploadSpeed,
                            eta = t.Eta,
                            ratio = t.Ratio,
                            tags = string.IsNullOrWhiteSpace(t.Category) ? Array.Empty<string>() : new[] { t.Category },
                            savePath = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                            isPaused = t.Status == TorrentStatus.Paused,
                            isFinished = t.Progress >= 1.0 || t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding
                        };
                    }

                    return Ok(new { result = dict, error = (object)null, id });

                case "torrents.add":
                case "torrents.addfile":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0)
                    {
                        var b64 = request.Params[0].GetString();
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

                        if (!string.IsNullOrWhiteSpace(b64))
                        {
                            var bytes = Convert.FromBase64String(b64);
                            var parsed = _torrentFileParser.Parse(bytes);
                            var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                            return Ok(new { result = added?.InfoHash, error = (object)null, id });
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

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
