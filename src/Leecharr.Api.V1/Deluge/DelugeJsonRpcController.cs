using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Deluge;

[AllowAnonymous]
[ApiController]
[Route("json")]
public class DelugeJsonRpcController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public DelugeJsonRpcController(
        ITorrentService torrentService,
        ICategoryService categoryService,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _categoryService = categoryService;
        _configService = configService;
    }

    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody] JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var methodProp) || methodProp.ValueKind != JsonValueKind.String)
        {
            return Ok(new { result = (object)null, error = "Invalid RPC request", id = (object)null });
        }

        var method = methodProp.GetString() ?? string.Empty;
        object id = 1;
        if (root.TryGetProperty("id", out var idElem))
        {
            if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt64(out var numId))
            {
                id = numId;
            }
            else if (idElem.ValueKind == JsonValueKind.String)
            {
                id = idElem.GetString();
            }
        }

        var paramsElem = root.TryGetProperty("params", out var p) ? p : default;

        try
        {
            switch (method.ToLowerInvariant())
            {
                case "auth.login":
                case "auth.check_session":
                case "auth.delete_session":
                case "web.connected":
                    return Ok(new { result = true, error = (object)null, id });

                case "core.get_config":
                case "web.get_config":
                    return Ok(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "download_location", _configService.DownloadDir },
                            { "max_connections_global", _configService.MaxGlobalConnections },
                            { "max_download_speed", _configService.MaxDownloadSpeedKbps },
                            { "max_upload_speed", _configService.MaxUploadSpeedKbps }
                        },
                        error = (object)null,
                        id
                    });

                case "core.get_torrents_status":
                case "web.get_torrents_status":
                    var torrents = _torrentService.GetAll();
                    var resultDict = new Dictionary<string, Dictionary<string, object>>();

                    foreach (var torrent in torrents)
                    {
                        resultDict[torrent.InfoHash.ToLowerInvariant()] = MapTorrentToDelugeStatus(torrent);
                    }

                    return Ok(new { result = resultDict, error = (object)null, id });

                case "core.get_torrent_status":
                    var targetHash = GetFirstStringParam(paramsElem);
                    var found = _torrentService.GetByInfoHash(targetHash);
                    if (found == null)
                    {
                        return Ok(new { result = (object)null, error = "Torrent not found", id });
                    }

                    return Ok(new { result = MapTorrentToDelugeStatus(found), error = (object)null, id });

                case "core.pause_torrent":
                    var pauseHashes = ExtractHashes(paramsElem);
                    foreach (var hash in pauseHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.PauseAsync(t.Id);
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.resume_torrent":
                    var resumeHashes = ExtractHashes(paramsElem);
                    foreach (var hash in resumeHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.ResumeAsync(t.Id);
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.remove_torrent":
                    var removeHash = GetFirstStringParam(paramsElem);
                    var deleteData = GetSecondBoolParam(paramsElem);
                    var toRemove = _torrentService.GetByInfoHash(removeHash);
                    if (toRemove != null)
                    {
                        await _torrentService.DeleteAsync(toRemove.Id, deleteData);
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.get_filter_tree":
                    var allTorrents = _torrentService.GetAll().ToList();
                    var stateCounts = new Dictionary<string, int>
                    {
                        { "All", allTorrents.Count },
                        { "Active", allTorrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding) },
                        { "Downloading", allTorrents.Count(t => t.Status == TorrentStatus.Downloading) },
                        { "Seeding", allTorrents.Count(t => t.Status == TorrentStatus.Seeding) },
                        { "Paused", allTorrents.Count(t => t.Status == TorrentStatus.Paused) }
                    };

                    var filterTree = new Dictionary<string, object>
                    {
                        { "state", stateCounts.Select(kvp => new object[] { kvp.Key, kvp.Value }).ToList() },
                        { "label", _categoryService.GetAll().Select(c => new object[] { c.Name, allTorrents.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase)) }).ToList() }
                    };

                    return Ok(new { result = filterTree, error = (object)null, id });

                default:
                    _logger.Debug("Unhandled Deluge RPC method: {0}", method);
                    return Ok(new { result = true, error = (object)null, id });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Deluge RPC method: {0}", method);
            return Ok(new { result = (object)null, error = ex.Message, id });
        }
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

    private static bool GetSecondBoolParam(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 1)
        {
            var second = parameters[1];
            if (second.ValueKind == JsonValueKind.True || second.ValueKind == JsonValueKind.False)
            {
                return second.GetBoolean();
            }
        }

        return false;
    }

    private static List<string> ExtractHashes(JsonElement parameters)
    {
        var hashes = new List<string>();
        if (parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in parameters.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    hashes.Add(item.GetString());
                }
                else if (item.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sub in item.EnumerateArray())
                    {
                        if (sub.ValueKind == JsonValueKind.String)
                        {
                            hashes.Add(sub.GetString());
                        }
                    }
                }
            }
        }
        else if (parameters.ValueKind == JsonValueKind.String)
        {
            hashes.Add(parameters.GetString());
        }

        return hashes;
    }

    private static Dictionary<string, object> MapTorrentToDelugeStatus(Torrent t)
    {
        var stateStr = t.Status switch
        {
            TorrentStatus.Downloading => "Downloading",
            TorrentStatus.Seeding => "Seeding",
            TorrentStatus.Paused => "Paused",
            TorrentStatus.Queued => "Queued",
            TorrentStatus.Checking => "Checking",
            TorrentStatus.Error => "Error",
            _ => "Paused"
        };

        return new Dictionary<string, object>
        {
            { "name", t.Name },
            { "hash", t.InfoHash },
            { "state", stateStr },
            { "progress", t.Progress * 100.0 },
            { "total_size", t.TotalSize },
            { "total_done", t.Downloaded },
            { "total_uploaded", t.Uploaded },
            { "total_payload_download", t.Downloaded },
            { "total_payload_upload", t.Uploaded },
            { "download_payload_rate", t.DownloadSpeed },
            { "upload_payload_rate", t.UploadSpeed },
            { "eta", t.Eta },
            { "ratio", t.Ratio },
            { "num_seeds", t.Seeders },
            { "total_seeds", t.Seeders },
            { "num_peers", t.Leechers },
            { "total_peers", t.Leechers },
            { "save_path", t.SavePath ?? string.Empty },
            { "label", t.Category ?? string.Empty },
            { "is_finished", t.Progress >= 1.0 },
            { "is_seed", t.Status == TorrentStatus.Seeding },
            { "paused", t.Status == TorrentStatus.Paused },
            { "time_added", new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() },
            { "all_time_download", t.Downloaded },
            { "active_time", (long)(DateTime.UtcNow - t.DateAdded).TotalSeconds },
            { "seeding_time", t.DateCompleted.HasValue ? (long)(DateTime.UtcNow - t.DateCompleted.Value).TotalSeconds : 0 }
        };
    }
}
