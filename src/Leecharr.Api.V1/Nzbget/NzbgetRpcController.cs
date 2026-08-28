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

namespace Leecharr.Api.V1.Nzbget;

public class NzbgetRequest
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
public class NzbgetRpcController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NzbgetRpcController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _categoryService = categoryService;
        _configService = configService;
    }

    [HttpGet]
    [HttpPost]
    [Route("nzbget/jsonrpc")]
    [Route("nzbget/xmlrpc")]
    [Route("nzbget")]
    [Route("{user}:{pass}/jsonrpc")]
    [Route("{user}:{pass}/xmlrpc")]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NzbgetRequest request = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(new { version = "1.1", result = "24.0", id = (object)1 });
        }

        var id = request.Id ?? 1;

        try
        {
            switch (request.Method.ToLowerInvariant())
            {
                case "version":
                    return Ok(new { version = "1.1", result = "24.0", id });

                case "config":
                case "loadconfig":
                    var configItems = new List<object>
                    {
                        new { Name = "MainDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "DestDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "InterDir", Value = _configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
                        new { Name = "NzbDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "QueueDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "TempDir", Value = _configService.IncompleteDownloadDir ?? "/downloads/incomplete" }
                    };

                    var nzbgetCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tv", "tv-sonarr", "movies", "music", "anime", "default" };
                    foreach (var c in _categoryService.GetAll())
                    {
                        nzbgetCats.Add(c.Name);
                    }

                    var catIndex = 1;
                    foreach (var cat in nzbgetCats)
                    {
                        configItems.Add(new { Name = $"Category{catIndex}.Name", Value = cat });
                        configItems.Add(new { Name = $"Category{catIndex}.DestDir", Value = global::System.IO.Path.Combine(_configService.DownloadDir ?? "/downloads", cat) });
                        catIndex++;
                    }

                    return Ok(new
                    {
                        version = "1.1",
                        result = configItems,
                        id
                    });

                case "status":
                    var all = _torrentService.GetAll().ToList();
                    var freeMb = (int)(GetDriveFreeSpace(_configService.DownloadDir) / (1024 * 1024));
                    return Ok(new
                    {
                        version = "1.1",
                        result = new
                        {
                            RemainingSizeMB = (int)(all.Sum(t => t.TotalSize - t.Downloaded) / (1024 * 1024)),
                            DownloadRate = (int)all.Sum(t => t.DownloadSpeed),
                            DownloadLimit = _configService.MaxDownloadSpeedKbps * 1024,
                            SpeedLimit = _configService.MaxDownloadSpeedKbps * 1024,
                            FreeDiskSpaceMB = freeMb,
                            DownloadPaused = false,
                            ServerPaused = false,
                            ServerStandBy = false,
                            PostJobCount = 0,
                            ParJobCount = 0,
                            DownloadTimeSec = 0,
                            ServerTime = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
                            ResumeTime = 0,
                            FeedActive = false,
                            QueueScriptCount = 0
                        },
                        id
                    });

                case "listgroups":
                    var queueTorrents = _torrentService.GetAll()
                        .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Queued || t.Status == TorrentStatus.Paused)
                        .Select((t, index) =>
                        {
                            var totalMb = (int)(t.TotalSize / (1024 * 1024));
                            var remMb = (int)((t.TotalSize - t.Downloaded) / (1024 * 1024));
                            var totalLo = (int)(t.TotalSize & 0xFFFFFFFF);
                            var totalHi = (int)(t.TotalSize >> 32);
                            var remBytes = Math.Max(0, t.TotalSize - t.Downloaded);
                            var remLo = (int)(remBytes & 0xFFFFFFFF);
                            var remHi = (int)(remBytes >> 32);

                            return new
                            {
                                NZBID = t.Id,
                                NZBName = t.Name ?? string.Empty,
                                NZBNicename = t.Name ?? string.Empty,
                                Kind = "NZB",
                                URL = string.Empty,
                                DestDir = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                                Category = t.Category ?? string.Empty,
                                FileSizeMB = totalMb,
                                RemainingSizeMB = remMb,
                                PausedSizeMB = 0,
                                FileSizeLo = totalLo,
                                FileSizeHi = totalHi,
                                RemainingSizeLo = remLo,
                                RemainingSizeHi = remHi,
                                PausedSizeLo = 0,
                                PausedSizeHi = 0,
                                FileCount = 1,
                                RemainingFileCount = 1,
                                PausedFileCount = 0,
                                Status = t.Status == TorrentStatus.Paused ? "PAUSED" : "DOWNLOADING",
                                ActiveDownloads = 1,
                                Parameters = Array.Empty<object>()
                            };
                        }).ToList();

                    return Ok(new { version = "1.1", result = queueTorrents, id });

                case "history":
                    var finished = _torrentService.GetAll()
                        .Where(t => t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding)
                        .Select(t =>
                        {
                            var totalMb = (int)(t.TotalSize / (1024 * 1024));
                            var totalLo = (int)(t.TotalSize & 0xFFFFFFFF);
                            var totalHi = (int)(t.TotalSize >> 32);

                            return new
                            {
                                NZBID = t.Id,
                                Name = t.Name ?? string.Empty,
                                NZBName = t.Name ?? string.Empty,
                                NZBNicename = t.Name ?? string.Empty,
                                DestDir = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                                Category = t.Category ?? string.Empty,
                                FileSizeMB = totalMb,
                                FileSizeLo = totalLo,
                                FileSizeHi = totalHi,
                                Status = "SUCCESS/ALL",
                                ParStatus = "SUCCESS",
                                UnpackStatus = "SUCCESS",
                                MoveStatus = "SUCCESS",
                                ScriptStatus = "NONE",
                                DeleteStatus = "NONE",
                                MarkStatus = "NONE",
                                UrlStatus = "NONE",
                                Parameters = Array.Empty<object>()
                            };
                        }).ToList();

                    return Ok(new { version = "1.1", result = finished, id });

                case "append":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var nzbName = request.Params[0].GetString();
                        var nzbContent = request.Params[1].GetString();
                        var category = request.Params.GetArrayLength() > 2 ? request.Params[2].GetString() : null;
                        var isPaused = false;
                        if (request.Params.GetArrayLength() > 5)
                        {
                            var pausedElem = request.Params[5];
                            if (pausedElem.ValueKind == JsonValueKind.True)
                            {
                                isPaused = true;
                            }
                            else if (pausedElem.ValueKind == JsonValueKind.Number)
                            {
                                isPaused = pausedElem.GetInt32() != 0;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(nzbContent))
                        {
                            try
                            {
                                var bytes = Convert.FromBase64String(nzbContent);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, bytes);
                                return Ok(new { version = "1.1", result = added?.Id ?? 1, id });
                            }
                            catch
                            {
                                return Ok(new { version = "1.1", result = 1, id });
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(nzbName) && (nzbName.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) || nzbName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || nzbName.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                        {
                            try
                            {
                                var added = await _torrentService.AddFromMagnetAsync(nzbName, category, null, isPaused);
                                return Ok(new { version = "1.1", result = added?.Id ?? 1, id });
                            }
                            catch
                            {
                                return Ok(new { version = "1.1", result = 1, id });
                            }
                        }
                    }

                    return Ok(new { version = "1.1", result = 1, id });

                case "pause":
                case "pauseall":
                    foreach (var t in _torrentService.GetAll())
                    {
                        await _torrentService.PauseAsync(t.Id);
                    }

                    return Ok(new { version = "1.1", result = true, id });

                case "resume":
                case "resumeall":
                    foreach (var t in _torrentService.GetAll())
                    {
                        await _torrentService.ResumeAsync(t.Id);
                    }

                    return Ok(new { version = "1.1", result = true, id });

                case "rate":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0)
                    {
                        var rate = request.Params[0].GetInt32();
                        _configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxDownloadSpeedKbps"] = rate });
                    }

                    return Ok(new { version = "1.1", result = true, id });

                case "editqueue":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var command = request.Params[0].GetString()?.ToLowerInvariant();
                        var offset = 0;
                        var editText = string.Empty;
                        var targetIds = new List<int>();

                        if (request.Params.GetArrayLength() >= 4)
                        {
                            if (request.Params[1].ValueKind == JsonValueKind.Number)
                            {
                                offset = request.Params[1].GetInt32();
                            }

                            if (request.Params[2].ValueKind == JsonValueKind.String)
                            {
                                editText = request.Params[2].GetString() ?? string.Empty;
                            }

                            var idElem = request.Params[3];
                            if (idElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var elem in idElem.EnumerateArray())
                                {
                                    if (elem.TryGetInt32(out var parsedId))
                                    {
                                        targetIds.Add(parsedId);
                                    }
                                }
                            }
                            else if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var parsedId))
                            {
                                targetIds.Add(parsedId);
                            }
                        }
                        else
                        {
                            var idElem = request.Params[1];
                            if (idElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var elem in idElem.EnumerateArray())
                                {
                                    if (elem.TryGetInt32(out var parsedId))
                                    {
                                        targetIds.Add(parsedId);
                                    }
                                }
                            }
                            else if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var parsedId))
                            {
                                targetIds.Add(parsedId);
                            }
                        }

                        foreach (var targetId in targetIds)
                        {
                            if (command == "grouppause")
                            {
                                await _torrentService.PauseAsync(targetId);
                            }
                            else if (command == "groupresume")
                            {
                                await _torrentService.ResumeAsync(targetId);
                            }
                            else if (command == "groupdelete" || command == "historydelete")
                            {
                                await _torrentService.DeleteAsync(targetId, false);
                            }
                            else if (command == "groupfinaldelete")
                            {
                                await _torrentService.DeleteAsync(targetId, true);
                            }
                            else if (command == "groupmovetop")
                            {
                                await _torrentService.MoveQueueAsync(targetId, "top");
                            }
                            else if (command == "groupmoveup")
                            {
                                await _torrentService.MoveQueueAsync(targetId, "up");
                            }
                            else if (command == "groupmovedown")
                            {
                                await _torrentService.MoveQueueAsync(targetId, "down");
                            }
                            else if (command == "groupmovebottom")
                            {
                                await _torrentService.MoveQueueAsync(targetId, "bottom");
                            }
                            else if (command == "groupmoveoffset")
                            {
                                await _torrentService.MoveQueueAsync(targetId, offset > 0 ? "down" : "up");
                            }
                            else if (command == "groupsetcategory")
                            {
                                var t = _torrentService.Get(targetId);
                                if (t != null && !string.IsNullOrWhiteSpace(editText))
                                {
                                    t.Category = editText;
                                    await _torrentService.UpdateAsync(t);
                                }
                            }
                        }
                    }

                    return Ok(new { version = "1.1", result = true, id });

                default:
                    _logger.Debug("Unhandled NZBGet method: {0}", request.Method);
                    return Ok(new { version = "1.1", result = true, id });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in NZBGet RPC: {0}", request.Method);
            return Ok(new { version = "1.1", error = new { code = 1, message = ex.Message }, id });
        }
    }

    private static long GetDriveFreeSpace(string path)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(path) ? "/downloads" : path;
            var fullPath = global::System.IO.Path.GetFullPath(target);
            var root = global::System.IO.Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = "/";
            }

            var driveInfo = new global::System.IO.DriveInfo(root);
            return driveInfo.AvailableFreeSpace;
        }
        catch
        {
            return 1099511627776L;
        }
    }
}
