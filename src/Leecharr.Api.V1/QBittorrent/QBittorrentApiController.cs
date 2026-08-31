using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Api.V1.QBittorrent;

[AllowAnonymous]
[ApiController]
[Route("api/v2")]
public class QBittorrentApiController : ControllerBase, IActionFilter
{
    private static readonly ConcurrentDictionary<string, DateTime> _authenticatedSessions = new();
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly ITrackerEntryRepository _trackerEntryRepository;
    private readonly NzbDrone.Core.Tags.ITagRepository _tagRepository;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IUserService _userService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public QBittorrentApiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        ITrackerEntryRepository trackerEntryRepository,
        NzbDrone.Core.Tags.ITagRepository tagRepository = null,
        IConfigFileProvider configFileProvider = null,
        IUserService userService = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _categoryService = categoryService;
        _configService = configService;
        _trackerEntryRepository = trackerEntryRepository;
        _tagRepository = tagRepository;
        _configFileProvider = configFileProvider;
        _userService = userService;
    }

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var actionName = context.ActionDescriptor.RouteValues["action"];
        if (string.Equals(actionName, nameof(Login), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsAuthenticated())
        {
            context.Result = StatusCode(StatusCodes.Status403Forbidden, "Forbidden");
        }
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private bool IsAuthenticated()
    {
        if (_configFileProvider == null || !_configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        if (Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
        {
            if (_authenticatedSessions.TryGetValue(sid, out var expiry) && expiry > DateTime.UtcNow)
            {
                return true;
            }
        }

        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey) && Request.Query.TryGetValue("apikey", out var qKey))
        {
            apiKey = qKey.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(_configFileProvider.ApiKey))
        {
            if (string.Equals(apiKey, _configFileProvider.ApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [HttpPost("auth/login")]
    public ActionResult Login([FromForm] string username = null, [FromForm] string password = null)
    {
        if (_configFileProvider != null && _configFileProvider.AuthenticationEnabled)
        {
            var authenticated = false;
            var masterApiKey = _configFileProvider.ApiKey;

            if (!string.IsNullOrWhiteSpace(masterApiKey) &&
                ((!string.IsNullOrWhiteSpace(password) && string.Equals(password, masterApiKey, StringComparison.Ordinal)) ||
                 (!string.IsNullOrWhiteSpace(username) && string.Equals(username, masterApiKey, StringComparison.Ordinal))))
            {
                authenticated = true;
            }
            else if (_userService != null && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                var user = _userService.Authenticate(username, password);
                if (user != null)
                {
                    authenticated = true;
                }
            }

            if (!authenticated)
            {
                return Content("Fails.", "text/plain");
            }
        }

        var sid = Guid.NewGuid().ToString("N");
        _authenticatedSessions[sid] = DateTime.UtcNow.AddDays(7);

        Response.Cookies.Append("SID", sid, new CookieOptions
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
        if (Request.Cookies.TryGetValue("SID", out var sid))
        {
            _authenticatedSessions.TryRemove(sid, out _);
        }

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
                ["max_seeding_time"] = t.TargetSeedTimeMinutes * 60,
                ["ratio_limit"] = t.TargetRatio > 0 ? t.TargetRatio : -2.0,
                ["seeding_time_limit"] = t.TargetSeedTimeMinutes > 0 ? t.TargetSeedTimeMinutes : -2,
                ["seeding_time"] = t.DateCompleted.HasValue ? (long)(DateTime.UtcNow - t.DateCompleted.Value).TotalSeconds : 0,
                ["last_activity"] = new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds()
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
        [FromForm] string stopped = null,
        [FromForm] string tags = null,
        [FromForm] string sequentialDownload = null,
        [FromForm] string firstLastPiecePrio = null,
        [FromForm] double? ratioLimit = null,
        [FromForm] int? seedingTimeLimit = null,
        [FromForm] string contentLayout = null)
    {
        var isPaused = string.Equals(paused, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stopped, "true", StringComparison.OrdinalIgnoreCase);
        var isSequential = string.Equals(sequentialDownload, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(firstLastPiecePrio, "true", StringComparison.OrdinalIgnoreCase);

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
                    if (added != null)
                    {
                        var needsUpdate = false;
                        if (!string.IsNullOrWhiteSpace(tags))
                        {
                            added.Label = tags;
                            needsUpdate = true;
                        }

                        if (isSequential)
                        {
                            added.SequentialDownload = true;
                            needsUpdate = true;
                        }

                        if (ratioLimit.HasValue && ratioLimit.Value > 0)
                        {
                            added.TargetRatio = ratioLimit.Value;
                            needsUpdate = true;
                        }

                        if (seedingTimeLimit.HasValue && seedingTimeLimit.Value > 0)
                        {
                            added.TargetSeedTimeMinutes = seedingTimeLimit.Value;
                            needsUpdate = true;
                        }

                        if (needsUpdate)
                        {
                            await _torrentService.UpdateAsync(added);
                        }
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
                        if (added != null)
                        {
                            var needsUpdate = false;
                            if (!string.IsNullOrWhiteSpace(tags))
                            {
                                added.Label = tags;
                                needsUpdate = true;
                            }

                            if (isSequential)
                            {
                                added.SequentialDownload = true;
                                needsUpdate = true;
                            }

                            if (ratioLimit.HasValue && ratioLimit.Value > 0)
                            {
                                added.TargetRatio = ratioLimit.Value;
                                needsUpdate = true;
                            }

                            if (seedingTimeLimit.HasValue && seedingTimeLimit.Value > 0)
                            {
                                added.TargetSeedTimeMinutes = seedingTimeLimit.Value;
                                needsUpdate = true;
                            }

                            if (needsUpdate)
                            {
                                await _torrentService.UpdateAsync(added);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to download torrent file from URL: {0}", trimmed);
                    }
                }
            }
        }

        // 2. Uploaded files
        if (torrents != null && torrents.Count > 0)
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
                    if (added != null)
                    {
                        var needsUpdate = false;
                        if (!string.IsNullOrWhiteSpace(tags))
                        {
                            added.Label = tags;
                            needsUpdate = true;
                        }

                        if (isSequential)
                        {
                            added.SequentialDownload = true;
                            needsUpdate = true;
                        }

                        if (ratioLimit.HasValue && ratioLimit.Value > 0)
                        {
                            added.TargetRatio = ratioLimit.Value;
                            needsUpdate = true;
                        }

                        if (seedingTimeLimit.HasValue && seedingTimeLimit.Value > 0)
                        {
                            added.TargetSeedTimeMinutes = seedingTimeLimit.Value;
                            needsUpdate = true;
                        }

                        if (needsUpdate)
                        {
                            await _torrentService.UpdateAsync(added);
                        }
                    }
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setShareLimits")]
    public async Task<ActionResult> SetShareLimits(
        [FromForm] string hashes,
        [FromForm] double? ratioLimit = null,
        [FromForm] int? seedingTimeLimit = null,
        [FromForm] int? inactiveSeedingTimeLimit = null)
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
                var updated = false;
                if (ratioLimit.HasValue)
                {
                    torrent.TargetRatio = ratioLimit.Value >= 0 ? ratioLimit.Value : 0;
                    updated = true;
                }

                if (seedingTimeLimit.HasValue)
                {
                    torrent.TargetSeedTimeMinutes = seedingTimeLimit.Value >= 0 ? seedingTimeLimit.Value : 0;
                    updated = true;
                }

                if (updated)
                {
                    await _torrentService.UpdateAsync(torrent);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setForceStart")]
    public async Task<ActionResult> SetForceStart(
        [FromForm] string hashes,
        [FromForm] string value = null,
        [FromForm] bool? enable = null)
    {
        if (string.IsNullOrEmpty(hashes))
        {
            return BadRequest();
        }

        var force = enable ?? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hash in hashList)
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                torrent.ForceStart = force;
                await _torrentService.UpdateAsync(torrent);
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
        if (!string.IsNullOrWhiteSpace(tags) && _tagRepository != null)
        {
            var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var existingTags = _tagRepository.All().Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tagList)
            {
                var trimmed = t.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !existingTags.Contains(trimmed))
                {
                    _tagRepository.Insert(new Tag { Label = trimmed });
                    existingTags.Add(trimmed);
                }
            }
        }

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
    public async Task<ActionResult> RemoveTags([FromForm] string hashes, [FromForm] string tags)
    {
        if (!string.IsNullOrEmpty(hashes))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var tagsToRemove = (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var hash in hashList)
            {
                var torrent = _torrentService.GetByInfoHash(hash);
                if (torrent != null && !string.IsNullOrEmpty(torrent.Label))
                {
                    if (tagsToRemove.Count == 0)
                    {
                        torrent.Label = string.Empty;
                    }
                    else
                    {
                        var remaining = torrent.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim())
                            .Where(t => !tagsToRemove.Contains(t));
                        torrent.Label = string.Join(", ", remaining);
                    }

                    await _torrentService.UpdateAsync(torrent);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/deleteTags")]
    public async Task<ActionResult> DeleteTags([FromForm] string tags)
    {
        if (!string.IsNullOrEmpty(tags))
        {
            var tagsToDelete = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var all = _torrentService.GetAll();
            foreach (var torrent in all)
            {
                if (!string.IsNullOrEmpty(torrent.Label))
                {
                    var remaining = torrent.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Where(t => !tagsToDelete.Contains(t));
                    torrent.Label = string.Join(", ", remaining);
                    await _torrentService.UpdateAsync(torrent);
                }
            }
        }

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

        var dbTrackers = _trackerEntryRepository.GetByTorrentId(torrent.Id).ToList();
        var trackers = new List<Dictionary<string, object>>();

        if (dbTrackers.Count > 0)
        {
            foreach (var t in dbTrackers)
            {
                trackers.Add(new Dictionary<string, object>
                {
                    ["url"] = t.Url ?? string.Empty,
                    ["status"] = t.Status,
                    ["num_peers"] = t.Seeders + t.Leechers,
                    ["num_seeds"] = t.Seeders,
                    ["num_leeches"] = t.Leechers,
                    ["num_downloaded"] = t.Downloaded,
                    ["msg"] = t.ErrorMessage ?? string.Empty
                });
            }
        }
        else
        {
            trackers.Add(new Dictionary<string, object>
            {
                ["url"] = torrent.TrackerUrl ?? string.Empty,
                ["status"] = 2,
                ["num_peers"] = torrent.Seeders + torrent.Leechers,
                ["num_seeds"] = torrent.Seeders,
                ["num_leeches"] = torrent.Leechers,
                ["num_downloaded"] = 0,
                ["msg"] = string.Empty
            });
        }

        return Ok(trackers);
    }

    [HttpPost("torrents/addTrackers")]
    public ActionResult AddTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                var urlList = urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var url in urlList)
                {
                    var trimmed = url.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        _trackerEntryRepository.Insert(new TrackerEntry
                        {
                            TorrentId = torrent.Id,
                            Url = trimmed,
                            Status = 1,
                            LastAnnounce = DateTime.UtcNow
                        });
                    }
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTrackers")]
    public ActionResult RemoveTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = _torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                var urlSet = urls.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var existing = _trackerEntryRepository.GetByTorrentId(torrent.Id);
                foreach (var t in existing.Where(t => urlSet.Contains(t.Url)))
                {
                    _trackerEntryRepository.Delete(t.Id);
                }
            }
        }

        return Content("Ok.", "text/plain");
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

    [HttpPost("torrents/setLocation")]
    [HttpPost("torrents/setSavePath")]
    public async Task<ActionResult> SetLocation([FromForm] string hashes, [FromForm] string location)
    {
        if (!string.IsNullOrWhiteSpace(hashes) && !string.IsNullOrWhiteSpace(location))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var h in hashList)
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    t.SavePath = location;
                    await _torrentService.UpdateAsync(t);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/topPrio")]
    public async Task<ActionResult> TopPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var h in hashes.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    await _torrentService.MoveQueueAsync(t.Id, "top");
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/bottomPrio")]
    public async Task<ActionResult> BottomPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var h in hashes.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    await _torrentService.MoveQueueAsync(t.Id, "bottom");
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/increasePrio")]
    public async Task<ActionResult> IncreasePrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var h in hashes.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    await _torrentService.MoveQueueAsync(t.Id, "up");
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/decreasePrio")]
    public async Task<ActionResult> DecreasePrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var h in hashes.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    await _torrentService.MoveQueueAsync(t.Id, "down");
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setDownloadLimit")]
    public ActionResult SetTransferDownloadLimit([FromForm] long limit)
    {
        _configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxDownloadSpeedKbps"] = (int)(limit / 1024) });
        return Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setUploadLimit")]
    public ActionResult SetTransferUploadLimit([FromForm] long limit)
    {
        _configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxUploadSpeedKbps"] = (int)(limit / 1024) });
        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setDownloadLimit")]
    public async Task<ActionResult> SetTorrentDownloadLimit([FromForm] string hashes, [FromForm] long limit)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var h in hashList)
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    t.DownloadLimit = (int)(limit / 1024);
                    await _torrentService.UpdateAsync(t);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setUploadLimit")]
    public async Task<ActionResult> SetTorrentUploadLimit([FromForm] string hashes, [FromForm] long limit)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var h in hashList)
            {
                var t = _torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    t.UploadLimit = (int)(limit / 1024);
                    await _torrentService.UpdateAsync(t);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/filePrio")]
    public async Task<ActionResult> FilePrio([FromForm] string hash, [FromForm] string id, [FromForm] int priority)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(id))
        {
            var t = _torrentService.GetByInfoHash(hash);
            if (t != null)
            {
                var files = _torrentFileService.GetFiles(t.Id).ToList();
                var idStrings = id.Split('|', StringSplitOptions.RemoveEmptyEntries);
                foreach (var idStr in idStrings)
                {
                    if (int.TryParse(idStr, out var fileIndex) && fileIndex >= 0 && fileIndex < files.Count)
                    {
                        await _torrentFileService.SetPriorityAsync(files[fileIndex].Id, priority);
                    }
                }
            }
        }

        return Content("Ok.", "text/plain");
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
