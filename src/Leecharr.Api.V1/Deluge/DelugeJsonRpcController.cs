using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public DelugeJsonRpcController(
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
                case "web.connect":
                    return Ok(new { result = true, error = (object)null, id });

                case "system.listmethods":
                case "system.list_methods":
                case "daemon.get_method_list":
                case "system.get_methods":
                    return Ok(new
                    {
                        result = new[]
                        {
                            "auth.login",
                            "auth.check_session",
                            "auth.delete_session",
                            "web.connected",
                            "web.connect",
                            "web.get_hosts",
                            "web.get_host_status",
                            "web.update_ui",
                            "web.get_plugins",
                            "web.get_installed_plugins",
                            "web.get_config",
                            "web.get_torrents_status",
                            "core.get_version",
                            "daemon.get_version",
                            "daemon.info",
                            "system.listMethods",
                            "system.list_methods",
                            "system.get_methods",
                            "core.get_config",
                            "core.get_session_status",
                            "core.get_free_space",
                            "core.get_path_free_space",
                            "core.get_torrents_status",
                            "core.get_torrent_status",
                            "core.add_torrent_file",
                            "core.add_torrent_magnet",
                            "core.add_torrent_url",
                            "core.pause_torrent",
                            "core.pause_torrents",
                            "core.resume_torrent",
                            "core.resume_torrents",
                            "core.remove_torrent",
                            "core.remove_torrents",
                            "core.force_recheck",
                            "core.set_torrent_options",
                            "core.get_filter_tree",
                            "core.get_enabled_plugins",
                            "core.get_available_plugins",
                            "label.get_labels",
                            "label.set_torrent"
                        },
                        error = (object)null,
                        id
                    });

                case "daemon.get_version":
                case "daemon.info":
                case "core.get_version":
                case "web.get_version":
                    return Ok(new { result = "2.1.1", error = (object)null, id });

                case "label.get_labels":
                    var labels = _categoryService.GetAll().Select(c => c.Name).ToArray();
                    return Ok(new { result = labels, error = (object)null, id });

                case "label.add":
                case "label.add_label":
                    var newLabel = GetFirstStringParam(paramsElem);
                    if (!string.IsNullOrWhiteSpace(newLabel))
                    {
                        var existing = _categoryService.GetByName(newLabel);
                        if (existing == null)
                        {
                            _categoryService.Add(new Category
                            {
                                Name = newLabel,
                                SavePath = global::System.IO.Path.Combine(_configService.DownloadDir ?? "/downloads", newLabel)
                            });
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "label.remove":
                    var labelToRemove = GetFirstStringParam(paramsElem);
                    if (!string.IsNullOrEmpty(labelToRemove))
                    {
                        var cat = _categoryService.GetByName(labelToRemove);
                        if (cat != null)
                        {
                            _categoryService.Delete(cat.Id);
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "label.get_options":
                    {
                        var labelOptName = GetFirstStringParam(paramsElem);
                        var targetCat = !string.IsNullOrWhiteSpace(labelOptName) ? _categoryService.GetByName(labelOptName) : null;
                        var labelOpts = new Dictionary<string, object>
                        {
                            ["apply_max"] = false,
                            ["max_download_speed"] = targetCat?.DefaultDownloadLimit ?? -1,
                            ["max_upload_speed"] = targetCat?.DefaultUploadLimit ?? -1,
                            ["apply_queue"] = false,
                            ["stop_at_ratio"] = (targetCat?.TargetRatio ?? 0) > 0,
                            ["stop_ratio"] = targetCat?.TargetRatio ?? 2.0,
                            ["remove_at_ratio"] = false,
                            ["apply_move_completed"] = !string.IsNullOrWhiteSpace(targetCat?.SavePath),
                            ["move_completed_path"] = targetCat?.SavePath ?? string.Empty
                        };
                        return Ok(new { result = labelOpts, error = (object)null, id });
                    }

                case "label.set_options":
                    {
                        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                        {
                            var lName = paramsElem[0].GetString();
                            var lOptions = paramsElem[1];
                            if (!string.IsNullOrWhiteSpace(lName) && lOptions.ValueKind == JsonValueKind.Object)
                            {
                                var cat = _categoryService.GetByName(lName) ?? new Category { Name = lName };
                                if (lOptions.TryGetProperty("move_completed_path", out var mcpProp) && mcpProp.ValueKind == JsonValueKind.String)
                                {
                                    cat.SavePath = mcpProp.GetString();
                                }

                                if (lOptions.TryGetProperty("stop_ratio", out var srProp) && srProp.ValueKind == JsonValueKind.Number)
                                {
                                    cat.TargetRatio = srProp.GetDouble();
                                }

                                if (cat.Id > 0)
                                {
                                    _categoryService.Update(cat);
                                }
                                else
                                {
                                    _categoryService.Add(cat);
                                }
                            }
                        }

                        return Ok(new { result = true, error = (object)null, id });
                    }

                case "label.set_torrent":
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var torrentHash = paramsElem[0].GetString();
                        var labelName = paramsElem[1].GetString();
                        if (!string.IsNullOrEmpty(torrentHash))
                        {
                            var torrent = _torrentService.GetByInfoHash(torrentHash);
                            if (torrent != null)
                            {
                                torrent.Category = labelName;
                                await _torrentService.UpdateAsync(torrent);
                            }
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.get_enabled_plugins":
                case "web.get_plugins":
                case "web.get_installed_plugins":
                case "core.get_available_plugins":
                    return Ok(new { result = new[] { "Label", "Extractor", "Execute", "AutoAdd", "Blocklist", "Scheduler", "Stats" }, error = (object)null, id });

                case "web.get_hosts":
                    return Ok(new { result = new object[] { new object[] { "1", "127.0.0.1", 58846, "Connected" } }, error = (object)null, id });

                case "web.get_host_status":
                    return Ok(new { result = new object[] { "1", "Connected", "2.1.1" }, error = (object)null, id });

                case "web.update_ui":
                    var allTorrentsForUi = _torrentService.GetAll().ToList();
                    var filteredTorrents = allTorrentsForUi;

                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 1)
                    {
                        var filterElem = paramsElem[1];
                        if (filterElem.ValueKind == JsonValueKind.Object)
                        {
                            if (filterElem.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                            {
                                var targetLabel = labelProp.GetString();
                                if (!string.IsNullOrWhiteSpace(targetLabel) && !string.Equals(targetLabel, "All", StringComparison.OrdinalIgnoreCase))
                                {
                                    filteredTorrents = filteredTorrents.Where(t => string.Equals(t.Category, targetLabel, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase)).ToList();
                                }
                            }
                        }
                    }

                    var torrentDict = new Dictionary<string, Dictionary<string, object>>();
                    foreach (var t in filteredTorrents)
                    {
                        torrentDict[t.InfoHash.ToLowerInvariant()] = MapTorrentToDelugeStatus(t);
                    }

                    return Ok(new
                    {
                        result = new
                        {
                            connected = true,
                            torrents = torrentDict,
                            filters = new
                            {
                                state = new object[]
                                {
                                    new object[] { "All", allTorrentsForUi.Count },
                                    new object[] { "Downloading", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Downloading) },
                                    new object[] { "Seeding", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Seeding) },
                                    new object[] { "Active", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding) },
                                    new object[] { "Paused", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Paused) }
                                },
                                label = _categoryService.GetAll().Select(c => new object[] { c.Name, allTorrentsForUi.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase)) }).ToArray()
                            },
                            stats = new
                            {
                                max_download = _configService.MaxDownloadSpeedKbps,
                                max_upload = _configService.MaxUploadSpeedKbps,
                                num_connections = allTorrentsForUi.Sum(t => t.Leechers + t.Seeders),
                                upload_rate = allTorrentsForUi.Sum(t => t.UploadSpeed),
                                download_rate = allTorrentsForUi.Sum(t => t.DownloadSpeed),
                                free_space = GetDriveFreeSpace(_configService.DownloadDir)
                            }
                        },
                        error = (object)null,
                        id
                    });

                case "core.get_config":
                case "web.get_config":
                    return Ok(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "download_location", _configService.DownloadDir ?? "/downloads" },
                            { "max_connections_global", _configService.MaxGlobalConnections },
                            { "max_download_speed", _configService.MaxDownloadSpeedKbps },
                            { "max_upload_speed", _configService.MaxUploadSpeedKbps }
                        },
                        error = (object)null,
                        id
                    });

                case "core.get_session_status":
                    var allT = _torrentService.GetAll().ToList();
                    return Ok(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "download_rate", allT.Sum(t => t.DownloadSpeed) },
                            { "upload_rate", allT.Sum(t => t.UploadSpeed) },
                            { "num_peers", allT.Sum(t => t.Leechers) },
                            { "payload_download_rate", allT.Sum(t => t.DownloadSpeed) },
                            { "payload_upload_rate", allT.Sum(t => t.UploadSpeed) },
                            { "total_download", allT.Sum(t => t.Downloaded) },
                            { "total_upload", allT.Sum(t => t.Uploaded) }
                        },
                        error = (object)null,
                        id
                    });

                case "core.get_free_space":
                case "core.get_path_free_space":
                    var targetPath = GetFirstStringParam(paramsElem) ?? _configService.DownloadDir ?? "/downloads";
                    return Ok(new { result = GetDriveFreeSpace(targetPath), error = (object)null, id });

                case "core.get_torrents_status":
                case "web.get_torrents_status":
                    var torrents = _torrentService.GetAll().ToList();

                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Object)
                    {
                        var filterObj = paramsElem[0];
                        if (filterObj.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                        {
                            var targetLabel = labelProp.GetString();
                            if (!string.IsNullOrWhiteSpace(targetLabel))
                            {
                                torrents = torrents.Where(t => string.Equals(t.Category, targetLabel, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase)).ToList();
                            }
                        }

                        if (filterObj.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
                        {
                            var stateStr = stateProp.GetString()?.ToLowerInvariant();
                            if (!string.IsNullOrWhiteSpace(stateStr) && stateStr != "all")
                            {
                                torrents = torrents.Where(t => t.Status.ToString().ToLowerInvariant() == stateStr).ToList();
                            }
                        }
                    }

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

                case "core.add_torrent_file":
                    string addedHash = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var b64 = paramsElem[1].GetString();
                        if (!string.IsNullOrWhiteSpace(b64))
                        {
                            var bytes = Convert.FromBase64String(b64);
                            var parsed = _torrentFileParser.Parse(bytes);
                            var isPaused = false;
                            string savePath = null;
                            string category = null;
                            double? targetRatio = null;

                            if (paramsElem.GetArrayLength() >= 3 && paramsElem[2].ValueKind == JsonValueKind.Object)
                            {
                                var opts = paramsElem[2];
                                if (opts.TryGetProperty("add_paused", out var ap))
                                {
                                    isPaused = ap.GetBoolean();
                                }

                                if (opts.TryGetProperty("download_location", out var dl))
                                {
                                    savePath = dl.GetString();
                                }
                                else if (opts.TryGetProperty("move_completed_path", out var mcp))
                                {
                                    savePath = mcp.GetString();
                                }

                                if (opts.TryGetProperty("label", out var lbl))
                                {
                                    category = lbl.GetString();
                                }

                                if (opts.TryGetProperty("stop_ratio", out var sr) && sr.ValueKind == JsonValueKind.Number)
                                {
                                    targetRatio = sr.GetDouble();
                                }
                            }

                            var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                            if (added != null && targetRatio.HasValue && targetRatio.Value > 0)
                            {
                                added.TargetRatio = targetRatio.Value;
                                await _torrentService.UpdateAsync(added);
                            }

                            addedHash = added?.InfoHash;
                        }
                    }

                    return Ok(new { result = addedHash, error = (object)null, id });

                case "core.add_torrent_magnet":
                    string magnetHash = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
                    {
                        var magnetUri = paramsElem[0].GetString();
                        var isPaused = false;
                        string savePath = null;
                        string category = null;
                        double? targetRatio = null;

                        if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = paramsElem[1];
                            if (opts.TryGetProperty("add_paused", out var ap))
                            {
                                isPaused = ap.GetBoolean();
                            }

                            if (opts.TryGetProperty("download_location", out var dl))
                            {
                                savePath = dl.GetString();
                            }
                            else if (opts.TryGetProperty("move_completed_path", out var mcp))
                            {
                                savePath = mcp.GetString();
                            }

                            if (opts.TryGetProperty("label", out var lbl))
                            {
                                category = lbl.GetString();
                            }

                            if (opts.TryGetProperty("stop_ratio", out var sr) && sr.ValueKind == JsonValueKind.Number)
                            {
                                targetRatio = sr.GetDouble();
                            }
                        }

                        var added = await _torrentService.AddFromMagnetAsync(magnetUri, category, savePath, isPaused);
                        if (added != null && targetRatio.HasValue && targetRatio.Value > 0)
                        {
                            added.TargetRatio = targetRatio.Value;
                            await _torrentService.UpdateAsync(added);
                        }

                        magnetHash = added?.InfoHash;
                    }

                    return Ok(new { result = magnetHash, error = (object)null, id });

                case "core.add_torrent_url":
                    string urlHash = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
                    {
                        var url = paramsElem[0].GetString();
                        var isPaused = false;
                        string savePath = null;
                        string category = null;

                        if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = paramsElem[1];
                            if (opts.TryGetProperty("add_paused", out var ap))
                            {
                                isPaused = ap.GetBoolean();
                            }

                            if (opts.TryGetProperty("download_location", out var dl))
                            {
                                savePath = dl.GetString();
                            }

                            if (opts.TryGetProperty("label", out var lbl))
                            {
                                category = lbl.GetString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await _torrentService.AddFromMagnetAsync(url, category, savePath, isPaused);
                                urlHash = added?.InfoHash;
                            }
                            else
                            {
                                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                var bytes = await httpClient.GetByteArrayAsync(url);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                                urlHash = added?.InfoHash;
                            }
                        }
                    }

                    return Ok(new { result = urlHash, error = (object)null, id });

                case "core.pause_torrent":
                case "core.pause_torrents":
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
                case "core.resume_torrents":
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
                case "core.remove_torrents":
                    var removeHashes = ExtractHashes(paramsElem);
                    var deleteData = GetSecondBoolParam(paramsElem);
                    foreach (var hash in removeHashes)
                    {
                        var toRemove = _torrentService.GetByInfoHash(hash);
                        if (toRemove != null)
                        {
                            await _torrentService.DeleteAsync(toRemove.Id, deleteData);
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.force_recheck":
                    var recheckHashes = ExtractHashes(paramsElem);
                    foreach (var hash in recheckHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.ForceRecheckAsync(t.Id);
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.set_torrent_options":
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var optHashes = ExtractHashes(paramsElem[0]);
                        var opts = paramsElem[1];
                        foreach (var hash in optHashes)
                        {
                            var t = _torrentService.GetByInfoHash(hash);
                            if (t != null)
                            {
                                if (opts.TryGetProperty("download_location", out var dl))
                                {
                                    t.SavePath = dl.GetString();
                                }
                                else if (opts.TryGetProperty("move_completed_path", out var mcp))
                                {
                                    t.SavePath = mcp.GetString();
                                }

                                if (opts.TryGetProperty("max_download_speed", out var mds))
                                {
                                    t.DownloadLimit = (int)(mds.GetInt64() * 1024);
                                }

                                if (opts.TryGetProperty("max_upload_speed", out var mus))
                                {
                                    t.UploadLimit = (int)(mus.GetInt64() * 1024);
                                }

                                if (opts.TryGetProperty("file_priorities", out var fp) && fp.ValueKind == JsonValueKind.Array)
                                {
                                    var files = _torrentFileService.GetFiles(t.Id).ToList();
                                    var fIdx = 0;
                                    foreach (var prioElem in fp.EnumerateArray())
                                    {
                                        if (fIdx < files.Count && prioElem.TryGetInt32(out var prio))
                                        {
                                            await _torrentFileService.SetPriorityAsync(files[fIdx].Id, prio);
                                        }

                                        fIdx++;
                                    }
                                }

                                await _torrentService.UpdateAsync(t);
                            }
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.set_torrent_file_priorities":
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var hash = paramsElem[0].GetString();
                        var priosElem = paramsElem[1];
                        if (!string.IsNullOrWhiteSpace(hash) && priosElem.ValueKind == JsonValueKind.Array)
                        {
                            var t = _torrentService.GetByInfoHash(hash);
                            if (t != null)
                            {
                                var files = _torrentFileService.GetFiles(t.Id).ToList();
                                var fIdx = 0;
                                foreach (var prioElem in priosElem.EnumerateArray())
                                {
                                    if (fIdx < files.Count && prioElem.TryGetInt32(out var prio))
                                    {
                                        await _torrentFileService.SetPriorityAsync(files[fIdx].Id, prio);
                                    }

                                    fIdx++;
                                }
                            }
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "web.disconnect":
                    return Ok(new { result = true, error = (object)null, id });

                case "core.queue_top":
                    var topHashes = ExtractHashes(paramsElem);
                    foreach (var hash in topHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "top");
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.queue_up":
                    var upHashes = ExtractHashes(paramsElem);
                    foreach (var hash in upHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "up");
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.queue_down":
                    var downHashes = ExtractHashes(paramsElem);
                    foreach (var hash in downHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "down");
                        }
                    }

                    return Ok(new { result = true, error = (object)null, id });

                case "core.queue_bottom":
                    var bottomHashes = ExtractHashes(paramsElem);
                    foreach (var hash in bottomHashes)
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "bottom");
                        }
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

    private Dictionary<string, object> MapTorrentToDelugeStatus(Torrent t)
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

        var files = _torrentFileService.GetFiles(t.Id);
        var filesList = files.Select((f, idx) => new Dictionary<string, object>
        {
            { "index", idx },
            { "path", f.Path },
            { "size", f.Size },
            { "offset", f.PieceOffset }
        }).ToList();

        var filePriorities = files.Select(f => f.Priority).ToList();
        var fileProgress = files.Select(f => f.Progress).ToList();

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
            { "num_files", files.Count() },
            { "files", filesList },
            { "file_priorities", filePriorities },
            { "file_progress", fileProgress },
            { "save_path", t.SavePath ?? string.Empty },
            { "label", t.Category ?? string.Empty },
            { "is_finished", t.Progress >= 1.0 },
            { "is_seed", t.Status == TorrentStatus.Seeding },
            { "paused", t.Status == TorrentStatus.Paused },
            { "time_added", new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() },
            { "all_time_download", t.Downloaded },
            { "active_time", (long)(DateTime.UtcNow - t.DateAdded).TotalSeconds },
            { "seeding_time", t.DateCompleted.HasValue ? (long)(DateTime.UtcNow - t.DateCompleted.Value).TotalSeconds : 0 },
            { "message", t.Status == TorrentStatus.Error ? "Error" : "OK" },
            { "is_auto_managed", true },
            { "stop_at_ratio", t.TargetRatio > 0 },
            { "remove_at_ratio", false },
            { "stop_ratio", t.TargetRatio }
        };
    }

    private static long GetDriveFreeSpace(string path)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(path) ? "/downloads" : path;
            var fullPath = global::System.IO.Path.GetFullPath(target);
            var root = global::System.IO.Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new global::System.IO.DriveInfo(root);
                return drive.AvailableFreeSpace;
            }
        }
        catch
        {
        }

        return 1099511627776L;
    }
}
