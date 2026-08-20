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
                            savePath = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                            isPaused = t.Status == TorrentStatus.Paused,
                            isFinished = t.Progress >= 1.0 || t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding
                        };
                    }

                    return Ok(new { result = dict, error = (object)null, id });

                case "torrents.adduri":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0)
                    {
                        var uri = request.Params[0].GetString();
                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            var added = await _torrentService.AddFromMagnetAsync(uri, null, null, false);
                            return Ok(new { result = added?.InfoHash, error = (object)null, id });
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
                        await _torrentService.DeleteAsync(tDelete.Id, false);
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
