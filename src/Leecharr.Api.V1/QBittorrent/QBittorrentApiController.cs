using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.QBittorrent;

[AllowAnonymous]
[ApiController]
[Route("api/v2")]
public class QBittorrentApiController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public QBittorrentApiController(
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

    [HttpPost("auth/login")]
    public ActionResult Login([FromForm] string username = null, [FromForm] string password = null)
    {
        Response.Cookies.Append("SID", Guid.NewGuid().ToString("N"), new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax
        });

        return Content("Ok.", "text/plain");
    }

    [HttpPost("auth/logout")]
    public ActionResult Logout()
    {
        Response.Cookies.Delete("SID");
        return Content("Ok.", "text/plain");
    }

    [HttpGet("app/version")]
    public ActionResult<string> GetVersion()
    {
        return Content("v4.4.2", "text/plain");
    }

    [HttpGet("app/webapiVersion")]
    public ActionResult<string> GetWebApiVersion()
    {
        return Content("2.8.3", "text/plain");
    }

    [HttpGet("torrents/info")]
    public ActionResult<List<Dictionary<string, object>>> GetTorrentsInfo(
        [FromQuery] string filter = null,
        [FromQuery] string category = null,
        [FromQuery] string hashes = null)
    {
        var torrents = _torrentService.GetAll();

        if (!string.IsNullOrEmpty(hashes))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.ToLowerInvariant())
                .ToHashSet();
            torrents = torrents.Where(t => hashList.Contains(t.InfoHash.ToLowerInvariant()));
        }

        if (!string.IsNullOrEmpty(category))
        {
            torrents = torrents.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        var result = torrents.Select(t =>
        {
            var state = MapToQBitState(t.Status);
            return new Dictionary<string, object>
            {
                ["hash"] = t.InfoHash,
                ["name"] = t.Name,
                ["size"] = t.TotalSize,
                ["total_size"] = t.TotalSize,
                ["progress"] = t.Progress,
                ["dlspeed"] = t.DownloadSpeed,
                ["upspeed"] = t.UploadSpeed,
                ["priority"] = t.Priority,
                ["num_seeds"] = t.Seeders,
                ["num_leechs"] = t.Leechers,
                ["num_complete"] = t.Seeders,
                ["num_incomplete"] = t.Leechers,
                ["ratio"] = t.Ratio,
                ["eta"] = t.Eta > 0 ? t.Eta : 8640000,
                ["state"] = state,
                ["seq_dl"] = t.SequentialDownload,
                ["category"] = t.Category ?? string.Empty,
                ["save_path"] = t.SavePath ?? string.Empty,
                ["content_path"] = Path.Combine(t.SavePath ?? string.Empty, t.Name ?? string.Empty),
                ["added_on"] = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds(),
                ["completion_on"] = t.DateCompleted.HasValue ? new DateTimeOffset(t.DateCompleted.Value).ToUnixTimeSeconds() : -1,
                ["amount_left"] = (long)(t.TotalSize * (1.0 - t.Progress)),
                ["downloaded"] = t.Downloaded,
                ["uploaded"] = t.Uploaded,
                ["max_ratio"] = t.TargetRatio,
                ["max_seeding_time"] = t.TargetSeedTimeMinutes * 60
            };
        }).ToList();

        return Ok(result);
    }

    [HttpPost("torrents/add")]
    public async Task<ActionResult> AddTorrents(
        [FromForm] string urls = null,
        [FromForm] List<IFormFile> torrents = null,
        [FromForm] string category = null,
        [FromForm] string savepath = null,
        [FromForm] string paused = null,
        [FromForm] string sequentialDownload = null)
    {
        var isPaused = string.Equals(paused, "true", StringComparison.OrdinalIgnoreCase);

        // 1. URLs (magnets or http torrent links)
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var lines = urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in lines)
            {
                var trimmed = url.Trim();
                if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    await _torrentService.AddFromMagnetAsync(trimmed, category, savepath, isPaused);
                }
            }
        }

        // 2. Uploaded .torrent files
        if (torrents != null)
        {
            foreach (var file in torrents)
            {
                if (file.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var parsed = _torrentFileParser.Parse(bytes);
                    await _torrentService.AddFromParsedTorrentAsync(parsed, category, savepath, isPaused, bytes);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/pause")]
    public async Task<ActionResult> PauseTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrEmpty(hashes))
        {
            return BadRequest();
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hash in hashList)
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                await _torrentService.PauseAsync(torrent.Id);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/resume")]
    public async Task<ActionResult> ResumeTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrEmpty(hashes))
        {
            return BadRequest();
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hash in hashList)
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                await _torrentService.ResumeAsync(torrent.Id);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/delete")]
    public async Task<ActionResult> DeleteTorrents(
        [FromForm] string hashes,
        [FromForm] bool deleteFiles = false)
    {
        if (string.IsNullOrEmpty(hashes))
        {
            return BadRequest();
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hash in hashList)
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                await _torrentService.DeleteAsync(torrent.Id, deleteFiles);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/files")]
    public ActionResult<List<Dictionary<string, object>>> GetFiles([FromQuery] string hash)
    {
        var torrent = _torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return NotFound();
        }

        var files = _torrentFileService.GetFiles(torrent.Id);
        var result = files.Select((f, index) => new Dictionary<string, object>
        {
            ["index"] = index,
            ["name"] = f.Path,
            ["size"] = f.Size,
            ["progress"] = f.Progress,
            ["priority"] = f.Priority,
            ["is_seed"] = f.Progress >= 1.0,
            ["piece_range"] = new[] { f.PieceOffset, f.PieceOffset + f.PieceCount - 1 }
        }).ToList();

        return Ok(result);
    }

    [HttpGet("torrents/categories")]
    public ActionResult<Dictionary<string, object>> GetCategories()
    {
        var categories = _categoryService.GetAll();
        var result = categories.ToDictionary(
            c => c.Name,
            c => (object)new { name = c.Name, savePath = c.SavePath });

        return Ok(result);
    }

    [HttpPost("torrents/createCategory")]
    public ActionResult CreateCategory([FromForm] string category, [FromForm] string savePath)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return BadRequest();
        }

        _categoryService.Add(new Category
        {
            Name = category,
            SavePath = savePath ?? string.Empty
        });

        return Content("Ok.", "text/plain");
    }

    [HttpGet("sync/maindata")]
    public ActionResult<Dictionary<string, object>> GetMainData([FromQuery] int rid = 0)
    {
        var torrents = _torrentService.GetAll();
        var torrentDict = torrents.ToDictionary(
            t => t.InfoHash,
            t => (object)new
            {
                name = t.Name,
                size = t.TotalSize,
                progress = t.Progress,
                dlspeed = t.DownloadSpeed,
                upspeed = t.UploadSpeed,
                state = MapToQBitState(t.Status),
                category = t.Category ?? string.Empty,
                save_path = t.SavePath ?? string.Empty,
                eta = t.Eta > 0 ? t.Eta : 8640000,
                ratio = t.Ratio
            });

        var categories = _categoryService.GetAll().ToDictionary(
            c => c.Name,
            c => (object)new { name = c.Name, savePath = c.SavePath });

        var result = new Dictionary<string, object>
        {
            ["rid"] = rid + 1,
            ["full_update"] = true,
            ["torrents"] = torrentDict,
            ["categories"] = categories,
            ["server_state"] = new
            {
                dl_info_speed = torrents.Sum(t => t.DownloadSpeed),
                up_info_speed = torrents.Sum(t => t.UploadSpeed),
                dl_info_data = torrents.Sum(t => t.Downloaded),
                up_info_data = torrents.Sum(t => t.Uploaded),
                connection_status = "connected"
            }
        };

        return Ok(result);
    }

    [HttpGet("transfer/info")]
    public ActionResult<Dictionary<string, object>> GetTransferInfo()
    {
        var torrents = _torrentService.GetAll().ToList();
        var result = new Dictionary<string, object>
        {
            ["dl_info_speed"] = torrents.Sum(t => t.DownloadSpeed),
            ["up_info_speed"] = torrents.Sum(t => t.UploadSpeed),
            ["dl_info_data"] = torrents.Sum(t => t.Downloaded),
            ["up_info_data"] = torrents.Sum(t => t.Uploaded),
            ["connection_status"] = "connected"
        };

        return Ok(result);
    }

    private static string MapToQBitState(TorrentStatus status)
    {
        return status switch
        {
            TorrentStatus.Queued => "queuedDL",
            TorrentStatus.Checking => "checkingDL",
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "uploading",
            TorrentStatus.Paused => "pausedDL",
            TorrentStatus.Stopped => "stoppedDL",
            TorrentStatus.Error => "error",
            _ => "unknown"
        };
    }
}
