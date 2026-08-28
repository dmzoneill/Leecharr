using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Sabnzbd;

[AllowAnonymous]
[ApiController]
public class SabnzbdApiController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public SabnzbdApiController(
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
    [Route("api")]
    [Route("sabnzbd/api")]
    public async Task<IActionResult> HandleApi(
        [FromQuery] string mode,
        [FromQuery] string name,
        [FromQuery] string value,
        [FromQuery] string cat,
        [FromQuery] string priority,
        [FromQuery] string output)
    {
        var formMode = Request.HasFormContentType ? Request.Form["mode"].ToString() : string.Empty;
        var formName = Request.HasFormContentType ? Request.Form["name"].ToString() : string.Empty;
        var formValue = Request.HasFormContentType ? Request.Form["value"].ToString() : string.Empty;
        var formValue2 = Request.HasFormContentType ? Request.Form["value2"].ToString() : string.Empty;
        var formCat = Request.HasFormContentType ? Request.Form["cat"].ToString() : string.Empty;

        var effectiveMode = (!string.IsNullOrWhiteSpace(mode) ? mode : formMode).ToLowerInvariant();

        switch (effectiveMode)
        {
            case "version":
                return Ok(new { version = "4.3.2" });

            case "fullstatus":
            case "status":
                return Ok(new
                {
                    status = new
                    {
                        version = "4.3.2",
                        paused = false,
                        restart_req = false,
                        power_options = true,
                        speedlimit = _configService.MaxDownloadSpeedKbps.ToString(),
                        color_scheme = "gold"
                    },
                    version = "4.3.2"
                });

            case "auth":
            case "get_config":
            case "set_config":
            case "config":
                var paramName = Request.Query["name"].ToString();
                var paramVal = Request.Query["value"].ToString();
                if (string.IsNullOrEmpty(paramName) && Request.HasFormContentType)
                {
                    paramName = Request.Form["name"].ToString();
                    paramVal = Request.Form["value"].ToString();
                }

                if (!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramVal))
                {
                    var cfgDict = new Dictionary<string, object>();
                    if (string.Equals(paramName, "speedlimit", StringComparison.OrdinalIgnoreCase) && int.TryParse(paramVal, out var speedKb))
                    {
                        cfgDict["MaxDownloadSpeedKbps"] = speedKb;
                    }
                    else if (string.Equals(paramName, "complete_dir", StringComparison.OrdinalIgnoreCase) || string.Equals(paramName, "dir_completed_download", StringComparison.OrdinalIgnoreCase))
                    {
                        cfgDict["DownloadDir"] = paramVal;
                    }
                    else if (string.Equals(paramName, "download_dir", StringComparison.OrdinalIgnoreCase) || string.Equals(paramName, "dir_inprogress_download", StringComparison.OrdinalIgnoreCase))
                    {
                        cfgDict["IncompleteDownloadDir"] = paramVal;
                    }

                    if (cfgDict.Count > 0)
                    {
                        _configService.SaveConfigDictionary(cfgDict);
                    }
                }

                var configuredCats = _categoryService.GetAll().ToList();
                var catNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*", "tv", "tv-sonarr", "movies", "music", "anime", "default" };
                foreach (var c in configuredCats)
                {
                    catNames.Add(c.Name);
                }

                return Ok(new
                {
                    config = new
                    {
                        version = "4.3.2",
                        misc = new
                        {
                            complete_dir = _configService.DownloadDir ?? "/downloads",
                            download_dir = _configService.IncompleteDownloadDir ?? "/downloads/incomplete"
                        },
                        categories = catNames.Select(name => new { name, dir = _configService.DownloadDir ?? "/downloads", order = 0 }).ToList()
                    }
                });

            case "get_cats":
                var allCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*", "tv", "tv-sonarr", "movies", "music", "anime", "default" };
                foreach (var c in _categoryService.GetAll())
                {
                    allCats.Add(c.Name);
                }

                return Ok(new { categories = allCats.ToList() });

            case "queue":
                var queueSubAction = (!string.IsNullOrWhiteSpace(name) ? name : formName).ToLowerInvariant();
                var queueVal = !string.IsNullOrWhiteSpace(value) ? value : formValue;
                var queueVal2 = !string.IsNullOrWhiteSpace(Request.Query["value2"].ToString())
                    ? Request.Query["value2"].ToString()
                    : (!string.IsNullOrWhiteSpace(formValue2) ? formValue2 : (!string.IsNullOrWhiteSpace(cat) ? cat : formCat));

                var delFiles = Request.Query["del_files"] == "1" ||
                    (Request.HasFormContentType && Request.Form["del_files"] == "1") ||
                    string.Equals(value, "del_files", StringComparison.OrdinalIgnoreCase);

                if (queueSubAction == "delete")
                {
                    var target = _torrentService.GetByInfoHash(queueVal);
                    if (target != null)
                    {
                        await _torrentService.DeleteAsync(target.Id, delFiles);
                    }

                    return Ok(new { status = true });
                }
                else if (queueSubAction == "pause")
                {
                    if (string.IsNullOrWhiteSpace(queueVal) || queueVal.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var t in _torrentService.GetAll())
                        {
                            await _torrentService.PauseAsync(t.Id);
                        }
                    }
                    else
                    {
                        var target = _torrentService.GetByInfoHash(queueVal);
                        if (target != null)
                        {
                            await _torrentService.PauseAsync(target.Id);
                        }
                    }

                    return Ok(new { status = true });
                }
                else if (queueSubAction == "resume")
                {
                    if (string.IsNullOrWhiteSpace(queueVal) || queueVal.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var t in _torrentService.GetAll())
                        {
                            await _torrentService.ResumeAsync(t.Id);
                        }
                    }
                    else
                    {
                        var target = _torrentService.GetByInfoHash(queueVal);
                        if (target != null)
                        {
                            await _torrentService.ResumeAsync(target.Id);
                        }
                    }

                    return Ok(new { status = true });
                }
                else if (queueSubAction == "change_cat")
                {
                    var target = _torrentService.GetByInfoHash(queueVal);
                    if (target != null && !string.IsNullOrWhiteSpace(queueVal2))
                    {
                        target.Category = queueVal2;
                        await _torrentService.UpdateAsync(target);
                    }

                    return Ok(new { status = true });
                }
                else if (queueSubAction == "priority" || queueSubAction.StartsWith("move_"))
                {
                    var target = _torrentService.GetByInfoHash(queueVal);
                    if (target != null)
                    {
                        if (queueSubAction == "priority" && int.TryParse(queueVal2, out var prio))
                        {
                            target.Priority = prio;
                            await _torrentService.UpdateAsync(target);
                        }
                        else if (queueSubAction.Contains("top") || queueVal2 == "0")
                        {
                            await _torrentService.MoveQueueAsync(target.Id, "top");
                        }
                        else if (queueSubAction.Contains("bottom") || queueSubAction.Contains("end"))
                        {
                            await _torrentService.MoveQueueAsync(target.Id, "bottom");
                        }
                        else if (queueSubAction.Contains("up"))
                        {
                            await _torrentService.MoveQueueAsync(target.Id, "up");
                        }
                        else if (queueSubAction.Contains("down"))
                        {
                            await _torrentService.MoveQueueAsync(target.Id, "down");
                        }
                    }

                    return Ok(new { status = true });
                }

                var allTorrents = _torrentService.GetAll().ToList();
                var queueSlots = allTorrents
                    .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Queued || t.Status == TorrentStatus.Paused)
                    .Select(t =>
                    {
                        var secondsLeft = t.DownloadSpeed > 0 ? (t.TotalSize - t.Downloaded) / t.DownloadSpeed : 0;
                        var ts = TimeSpan.FromSeconds(secondsLeft);
                        var timeleftStr = $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";

                        return new
                        {
                            nzo_id = t.InfoHash,
                            filename = t.Name ?? string.Empty,
                            size = (t.TotalSize / (1024.0 * 1024.0)).ToString("F2") + " MB",
                            sizeleft = ((t.TotalSize - t.Downloaded) / (1024.0 * 1024.0)).ToString("F2") + " MB",
                            mb = (t.TotalSize / (1024.0 * 1024.0)).ToString("F2"),
                            mbleft = ((t.TotalSize - t.Downloaded) / (1024.0 * 1024.0)).ToString("F2"),
                            status = t.Status == TorrentStatus.Paused ? "Paused" : "Downloading",
                            cat = t.Category ?? "default",
                            priority = "Normal",
                            timeleft = timeleftStr,
                            percentage = ((int)(t.Progress * 100)).ToString()
                        };
                    }).ToList();

                var freeSpaceGb = (GetDriveFreeSpace(_configService.DownloadDir) / (1024.0 * 1024.0 * 1024.0)).ToString("F2");
                var incFreeSpaceGb = (GetDriveFreeSpace(_configService.IncompleteDownloadDir) / (1024.0 * 1024.0 * 1024.0)).ToString("F2");

                return Ok(new
                {
                    queue = new
                    {
                        status = "Downloading",
                        speed = (allTorrents.Sum(t => t.DownloadSpeed) / 1024.0).ToString("F1") + " KB/s",
                        speedlimit = _configService.MaxDownloadSpeedKbps.ToString(),
                        paused = false,
                        noofslots_total = queueSlots.Count,
                        diskspace1 = freeSpaceGb,
                        diskspace2 = incFreeSpaceGb,
                        diskspacetotal1 = "1000.00",
                        diskspacetotal2 = "1000.00",
                        slots = queueSlots
                    }
                });

            case "history":
                var historySubAction = (!string.IsNullOrWhiteSpace(name) ? name : formName).ToLowerInvariant();
                var historyVal = !string.IsNullOrWhiteSpace(value) ? value : formValue;
                var histDelFiles = Request.Query["del_files"] == "1" ||
                    (Request.HasFormContentType && Request.Form["del_files"] == "1") ||
                    string.Equals(value, "del_files", StringComparison.OrdinalIgnoreCase);

                if (historySubAction == "delete")
                {
                    var target = _torrentService.GetByInfoHash(historyVal);
                    if (target != null)
                    {
                        await _torrentService.DeleteAsync(target.Id, histDelFiles);
                    }

                    return Ok(new { status = true });
                }

                var finishedTorrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding)
                    .Select(t => new
                    {
                        nzo_id = t.InfoHash,
                        name = t.Name ?? string.Empty,
                        nzb_name = t.Name ?? string.Empty,
                        size = (t.TotalSize / (1024.0 * 1024.0)).ToString("F2") + " MB",
                        bytes = t.TotalSize,
                        category = t.Category ?? "default",
                        status = "Completed",
                        storage = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                        path = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                        download_time = 60,
                        completename = t.Name ?? string.Empty
                    }).ToList();

                return Ok(new
                {
                    history = new
                    {
                        total_size = "0 MB",
                        month_size = "0 MB",
                        week_size = "0 MB",
                        noofslots = finishedTorrents.Count,
                        slots = finishedTorrents
                    }
                });

            case "addurl":
                var targetUrl = !string.IsNullOrWhiteSpace(name) ? name : formName;
                var targetCat = !string.IsNullOrWhiteSpace(cat) ? cat : formCat;
                var addedId = Guid.NewGuid().ToString("N");

                if (!string.IsNullOrWhiteSpace(targetUrl))
                {
                    if (targetUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        var added = await _torrentService.AddFromMagnetAsync(targetUrl, targetCat, null, false);
                        if (added != null)
                        {
                            var prioStr = Request.Query["priority"].ToString();
                            if (int.TryParse(prioStr, out var pVal))
                            {
                                added.Priority = pVal;
                                await _torrentService.UpdateAsync(added);
                            }

                            addedId = added.InfoHash;
                        }
                    }
                    else
                    {
                        using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                        var bytes = await client.GetByteArrayAsync(targetUrl);
                        var parsed = _torrentFileParser.Parse(bytes);
                        var added = await _torrentService.AddFromParsedTorrentAsync(parsed, targetCat, null, false, bytes);
                        if (added != null)
                        {
                            var prioStr = Request.Query["priority"].ToString();
                            if (int.TryParse(prioStr, out var pVal))
                            {
                                added.Priority = pVal;
                                await _torrentService.UpdateAsync(added);
                            }

                            addedId = added.InfoHash;
                        }
                    }
                }

                return Ok(new { status = true, nzo_ids = new[] { addedId } });

            case "addfile":
            case "addlocalfile":
                if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                {
                    var file = Request.Form.Files[0];
                    var fileCat = !string.IsNullOrWhiteSpace(cat) ? cat : formCat;
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var parsed = _torrentFileParser.Parse(bytes);
                    var added = await _torrentService.AddFromParsedTorrentAsync(parsed, fileCat, null, false, bytes);
                    if (added != null)
                    {
                        var prioStr = Request.Query["priority"].ToString();
                        if (int.TryParse(prioStr, out var pVal))
                        {
                            added.Priority = pVal;
                            await _torrentService.UpdateAsync(added);
                        }
                    }

                    return Ok(new { status = true, nzo_ids = new[] { added?.InfoHash ?? Guid.NewGuid().ToString("N") } });
                }

                return Ok(new { status = true, nzo_ids = new[] { Guid.NewGuid().ToString("N") } });

            case "pause":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var t = _torrentService.GetByInfoHash(value);
                    if (t != null)
                    {
                        await _torrentService.PauseAsync(t.Id);
                    }
                }
                else
                {
                    foreach (var t in _torrentService.GetAll())
                    {
                        await _torrentService.PauseAsync(t.Id);
                    }
                }

                return Ok(new { status = true });

            case "resume":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var t = _torrentService.GetByInfoHash(value);
                    if (t != null)
                    {
                        await _torrentService.ResumeAsync(t.Id);
                    }
                }
                else
                {
                    foreach (var t in _torrentService.GetAll())
                    {
                        await _torrentService.ResumeAsync(t.Id);
                    }
                }

                return Ok(new { status = true });

            case "delete":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var directDelFiles = Request.Query["del_files"] == "1" || (Request.HasFormContentType && Request.Form["del_files"] == "1");
                    var t = _torrentService.GetByInfoHash(value);
                    if (t != null)
                    {
                        await _torrentService.DeleteAsync(t.Id, directDelFiles);
                    }
                }

                return Ok(new { status = true });

            case "change_cat":
            case "set_category":
                if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(cat))
                {
                    var t = _torrentService.GetByInfoHash(value);
                    if (t != null)
                    {
                        t.Category = cat;
                        await _torrentService.UpdateAsync(t);
                    }
                }

                return Ok(new { status = true });

            default:
                return Ok(new { status = true, version = "4.3.2" });
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
