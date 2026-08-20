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
        var effectiveMode = (mode ?? Request.Form["mode"].ToString() ?? string.Empty).ToLowerInvariant();

        switch (effectiveMode)
        {
            case "version":
                return Ok(new { version = "4.3.2" });

            case "auth":
            case "get_config":
            case "config":
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
                        categories = _categoryService.GetAll().Select(c => new { name = c.Name, dir = c.SavePath, order = 0 }).ToList()
                    }
                });

            case "get_cats":
                var cats = new List<string> { "*" };
                cats.AddRange(_categoryService.GetAll().Select(c => c.Name));
                return Ok(new { categories = cats });

            case "queue":
                var allTorrents = _torrentService.GetAll().ToList();
                var queueSlots = allTorrents
                    .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Queued || t.Status == TorrentStatus.Paused)
                    .Select(t => new
                    {
                        nzo_id = t.InfoHash,
                        filename = t.Name ?? string.Empty,
                        size = (t.TotalSize / (1024.0 * 1024.0)).ToString("F2") + " MB",
                        sizeleft = ((t.TotalSize - t.Downloaded) / (1024.0 * 1024.0)).ToString("F2") + " MB",
                        mb = (t.TotalSize / (1024.0 * 1024.0)).ToString("F2"),
                        mbleft = ((t.TotalSize - t.Downloaded) / (1024.0 * 1024.0)).ToString("F2"),
                        status = t.Status == TorrentStatus.Paused ? "Paused" : "Downloading",
                        cat = t.Category ?? "default",
                        timeleft = "0:00:00",
                        percentage = ((int)(t.Progress * 100)).ToString()
                    }).ToList();

                return Ok(new
                {
                    queue = new
                    {
                        status = "Downloading",
                        speed = (allTorrents.Sum(t => t.DownloadSpeed) / 1024.0).ToString("F1") + " KB/s",
                        speedlimit = _configService.MaxDownloadSpeedKbps.ToString(),
                        paused = false,
                        noofslots_total = queueSlots.Count,
                        slots = queueSlots
                    }
                });

            case "history":
                var finishedTorrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding)
                    .Select(t => new
                    {
                        nzo_id = t.InfoHash,
                        name = t.Name ?? string.Empty,
                        size = (t.TotalSize / (1024.0 * 1024.0)).ToString("F2") + " MB",
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
                var url = name ?? Request.Form["name"].ToString();
                var targetCat = cat ?? Request.Form["cat"].ToString();
                var addedId = Guid.NewGuid().ToString("N");

                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        var added = await _torrentService.AddFromMagnetAsync(url, targetCat, null, false);
                        addedId = added?.InfoHash ?? addedId;
                    }
                    else
                    {
                        using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                        var bytes = await client.GetByteArrayAsync(url);
                        var parsed = _torrentFileParser.Parse(bytes);
                        var added = await _torrentService.AddFromParsedTorrentAsync(parsed, targetCat, null, false, bytes);
                        addedId = added?.InfoHash ?? addedId;
                    }
                }

                return Ok(new { status = true, nzo_ids = new[] { addedId } });

            case "addfile":
            case "addlocalfile":
                if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                {
                    var file = Request.Form.Files[0];
                    var fileCat = cat ?? Request.Form["cat"].ToString();
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var parsed = _torrentFileParser.Parse(bytes);
                    var added = await _torrentService.AddFromParsedTorrentAsync(parsed, fileCat, null, false, bytes);
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

                return Ok(new { status = true });

            case "delete":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var t = _torrentService.GetByInfoHash(value);
                    if (t != null)
                    {
                        await _torrentService.DeleteAsync(t.Id, false);
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
}
