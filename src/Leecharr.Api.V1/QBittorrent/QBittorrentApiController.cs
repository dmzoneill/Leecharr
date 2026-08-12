using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

    [HttpGet("app/preferences")]
    public ActionResult<Dictionary<string, object>> GetPreferences()
    {
        var savePath = _configService.DownloadDir ?? "/downloads";
        var tempPath = _configService.IncompleteDownloadDir ?? "/downloads/incomplete";

        return Ok(new Dictionary<string, object>
        {
            ["save_path"] = savePath,
            ["temp_path_enabled"] = !string.IsNullOrWhiteSpace(tempPath),
            ["temp_path"] = tempPath,
            ["listen_port"] = _configService.ListeningPort,
            ["up_limit"] = _configService.MaxUploadSpeedKbps * 1024,
            ["dl_limit"] = _configService.MaxDownloadSpeedKbps * 1024,
            ["max_connec"] = _configService.MaxGlobalConnections,
            ["max_connec_per_torrent"] = _configService.MaxPerTorrentConnections,
            ["dht"] = _configService.EnableDht,
            ["pex"] = _configService.EnablePex,
            ["lsd"] = _configService.EnableLpd,
            ["encryption"] = 1,
            ["anonymous_mode"] = false,
            ["queueing_enabled"] = false,
            ["alt_dl_limit"] = _configService.AltDownloadSpeedKbps * 1024,
            ["alt_up_limit"] = _configService.AltUploadSpeedKbps * 1024
        });
    }

    [HttpGet("app/defaultSavePath")]
    public ActionResult<string> GetDefaultSavePath()
    {
        return Content(_configService.DownloadDir ?? "/downloads", "text/plain");
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

        if (!string.IsNullOrEmpty(filter))
        {
            switch (filter.ToLowerInvariant())
            {
                case "downloading":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Downloading);
                    break;
                case "seeding":
                case "completed":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Seeding || t.Progress >= 1.0);
                    break;
                case "paused":
                case "stopped":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped);
                    break;
                case "active":
                    torrents = torrents.Where(t => t.DownloadSpeed > 0 || t.UploadSpeed > 0);
                    break;
                case "inactive":
                    torrents = torrents.Where(t => t.DownloadSpeed == 0 && t.UploadSpeed == 0);
                    break;
            }
        }

        var result = torrents.Select(t =>
        {
            var state = MapToQBitState(t.Status, t.Progress);
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
                ["tags"] = t.Label ?? string.Empty,
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
        [FromForm] string tags = null,
        [FromForm] string sequentialDownload = null)
    {
        var isPaused = string.Equals(paused, "true", StringComparison.OrdinalIgnoreCase);

        // 1. URLs (magnets or http/https torrent links)
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var lines = urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in lines)
            {
                var trimmed = url.Trim();
                if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await _torrentService.AddFromMagnetAsync(trimmed, category, savepath, isPaused);
                    if (added != null && !string.IsNullOrWhiteSpace(tags))
                    {
                        added.Label = tags;
                        await _torrentService.UpdateAsync(added);
                    }
                }
                else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                        var bytes = await httpClient.GetByteArrayAsync(trimmed);
                        var parsed = _torrentFileParser.Parse(bytes);
                        var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savepath, isPaused, bytes);
                        if (added != null && !string.IsNullOrWhiteSpace(tags))
                        {
                            added.Label = tags;
                            await _torrentService.UpdateAsync(added);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to download and parse .torrent file from {0}", trimmed);
                    }
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
                    var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, savepath, isPaused, bytes);
                    if (added != null && !string.IsNullOrWhiteSpace(tags))
                    {
                        added.Label = tags;
                        await _torrentService.UpdateAsync(added);
                    }
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

    [HttpGet("torrents/tags")]
    public ActionResult<List<string>> GetTags()
    {
        var tags = _torrentService.GetAll()
            .Select(t => t.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();
        return Ok(tags);
    }

    [HttpPost("torrents/createTags")]
    public ActionResult CreateTags([FromForm] string tags)
    {
        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/addTags")]
    public async Task<ActionResult> AddTags([FromForm] string hashes, [FromForm] string tags)
    {
        if (string.IsNullOrEmpty(hashes) || string.IsNullOrEmpty(tags))
        {
            return Content("Ok.", "text/plain");
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hash in hashList)
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                torrent.Label = tags;
                await _torrentService.UpdateAsync(torrent);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTags")]
    [HttpPost("torrents/deleteTags")]
    public ActionResult DeleteTags([FromForm] string tags)
    {
        return Content("Ok.", "text/plain");
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

    [HttpPost("torrents/setCategory")]
    public async Task<ActionResult> SetCategory([FromForm] string hashes, [FromForm] string category)
    {
        if (string.IsNullOrEmpty(hashes))
        {
            return Content("Ok.", "text/plain");
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hash in hashList)
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                torrent.Category = category ?? string.Empty;
                await _torrentService.UpdateAsync(torrent);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/editCategory")]
    public ActionResult EditCategory([FromForm] string category, [FromForm] string savePath)
    {
        var existing = _categoryService.GetByName(category);
        if (existing != null)
        {
            existing.SavePath = savePath ?? string.Empty;
            _categoryService.Update(existing);
        }
        else
        {
            _categoryService.Add(new Category { Name = category, SavePath = savePath ?? string.Empty });
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeCategories")]
    public ActionResult RemoveCategories([FromForm] string categories)
    {
        if (!string.IsNullOrEmpty(categories))
        {
            var cats = categories.Split(new[] { '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var c in cats)
            {
                var existing = _categoryService.GetByName(c);
                if (existing != null)
                {
                    _categoryService.Delete(existing.Id);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/properties")]
    public ActionResult<Dictionary<string, object>> GetProperties([FromQuery] string hash)
    {
        var torrent = _torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return NotFound();
        }

        return Ok(new Dictionary<string, object>
        {
            ["save_path"] = torrent.SavePath ?? string.Empty,
            ["creation_date"] = new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds(),
            ["piece_size"] = torrent.PieceLength,
            ["pieces_num"] = torrent.PieceCount,
            ["pieces_have"] = (int)(torrent.PieceCount * torrent.Progress),
            ["total_downloaded"] = torrent.Downloaded,
            ["total_uploaded"] = torrent.Uploaded,
            ["up_limit"] = torrent.UploadLimit,
            ["dl_limit"] = torrent.DownloadLimit,
            ["time_elapsed"] = (int)(DateTime.UtcNow - torrent.DateAdded).TotalSeconds,
            ["seeding_time"] = torrent.DateCompleted.HasValue ? (int)(DateTime.UtcNow - torrent.DateCompleted.Value).TotalSeconds : 0,
            ["nb_connections"] = torrent.Seeders + torrent.Leechers,
            ["share_ratio"] = torrent.Ratio
        });
    }

    [HttpGet("torrents/trackers")]
    public ActionResult<List<Dictionary<string, object>>> GetTrackers([FromQuery] string hash)
    {
        var torrent = _torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return NotFound();
        }

        var trackers = new List<Dictionary<string, object>>
        {
            new()
            {
                ["url"] = torrent.TrackerUrl ?? string.Empty,
                ["status"] = 2,
                ["num_peers"] = torrent.Seeders + torrent.Leechers,
                ["num_seeds"] = torrent.Seeders,
                ["num_leeches"] = torrent.Leechers,
                ["num_downloaded"] = 0,
                ["msg"] = string.Empty
            }
        };

        return Ok(trackers);
    }

    [HttpPost("torrents/recheck")]
    public async Task<ActionResult> RecheckTorrents([FromForm] string hashes)
    {
        if (!string.IsNullOrEmpty(hashes))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var hash in hashList)
            {
                var torrent = _torrentService.GetByInfoHash(hash);
                if (torrent != null)
                {
                    await _torrentService.ForceRecheckAsync(torrent.Id);
                }
            }
        }

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
                state = MapToQBitState(t.Status, t.Progress),
                category = t.Category ?? string.Empty,
                tags = t.Label ?? string.Empty,
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

    private static string MapToQBitState(TorrentStatus status, double progress)
    {
        return status switch
        {
            TorrentStatus.Queued => progress >= 1.0 ? "queuedUP" : "queuedDL",
            TorrentStatus.Checking => progress >= 1.0 ? "checkingUP" : "checkingDL",
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "uploading",
            TorrentStatus.Paused => progress >= 1.0 ? "pausedUP" : "pausedDL",
            TorrentStatus.Stopped => progress >= 1.0 ? "stoppedUP" : "stoppedDL",
            TorrentStatus.Error => "error",
            _ => "unknown"
        };
    }
}
