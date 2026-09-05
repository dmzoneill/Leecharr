// Copyright (c) PlaceholderCompany. All rights reserved.

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
using NzbDrone.Core.BitTorrent.Creation;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers.Search;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Api.V1.QBittorrent;

[AllowAnonymous]
[ApiController]
[Route("api/v2")]
public class QBittorrentApiController : ControllerBase, IActionFilter
{
    private static readonly ConcurrentDictionary<string, DateTime> authenticatedSessions = new();
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly NzbDrone.Core.Tags.ITagRepository tagRepository;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IUserService userService;
    private readonly ITorrentCreationService torrentCreationService;
    private readonly IQBittorrentSearchService qbittorrentSearchService;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public QBittorrentApiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        ITrackerEntryRepository trackerEntryRepository,
        NzbDrone.Core.Tags.ITagRepository tagRepository = null,
        IConfigFileProvider configFileProvider = null,
        IUserService userService = null,
        ITorrentCreationService torrentCreationService = null,
        IQBittorrentSearchService qbittorrentSearchService = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.trackerEntryRepository = trackerEntryRepository;
        this.tagRepository = tagRepository;
        this.configFileProvider = configFileProvider;
        this.userService = userService;
        this.torrentCreationService = torrentCreationService ?? new TorrentCreationService();
        this.qbittorrentSearchService = qbittorrentSearchService ?? new QBittorrentSearchService();
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var actionName = context.ActionDescriptor.RouteValues["action"];
        if (string.Equals(actionName, nameof(this.Login), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!this.IsAuthenticated())
        {
            context.Result = this.StatusCode(StatusCodes.Status403Forbidden, "Forbidden");
        }
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private bool IsAuthenticated()
    {
        if (this.configFileProvider == null || !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (this.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        if (this.Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
        {
            if (authenticatedSessions.TryGetValue(sid, out var expiry) && expiry > DateTime.UtcNow)
            {
                return true;
            }
        }

        var apiKey = this.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey) && this.Request.Query.TryGetValue("apikey", out var qKey))
        {
            apiKey = qKey.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey))
        {
            if (string.Equals(apiKey, this.configFileProvider.ApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [HttpPost("auth/login")]
    public ActionResult Login([FromForm] string username = null, [FromForm] string password = null)
    {
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var authenticated = false;
            var masterApiKey = this.configFileProvider.ApiKey;

            if (!string.IsNullOrWhiteSpace(masterApiKey) &&
                ((!string.IsNullOrWhiteSpace(password) && string.Equals(password, masterApiKey, StringComparison.Ordinal)) ||
                 (!string.IsNullOrWhiteSpace(username) && string.Equals(username, masterApiKey, StringComparison.Ordinal))))
            {
                authenticated = true;
            }
            else if (this.userService != null && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                var user = this.userService.Authenticate(username, password);
                if (user != null)
                {
                    authenticated = true;
                }
            }

            if (!authenticated)
            {
                return this.Content("Fails.", "text/plain");
            }
        }

        var sid = Guid.NewGuid().ToString("N");
        authenticatedSessions[sid] = DateTime.UtcNow.AddDays(7);

        this.Response.Cookies.Append("SID", sid, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("auth/logout")]
    public ActionResult Logout()
    {
        if (this.Request.Cookies.TryGetValue("SID", out var sid))
        {
            authenticatedSessions.TryRemove(sid, out _);
        }

        this.Response.Cookies.Delete("SID");
        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("app/version")]
    public ActionResult<string> GetVersion()
    {
        return this.Content("v4.4.2", "text/plain");
    }

    [HttpGet("app/webapiVersion")]
    public ActionResult<string> GetWebApiVersion()
    {
        return this.Content("2.8.3", "text/plain");
    }

    [HttpGet("app/preferences")]
    public ActionResult<Dictionary<string, object>> GetPreferences()
    {
        var savePath = this.configService.DownloadDir ?? "/downloads";
        var tempPath = this.configService.IncompleteDownloadDir ?? "/downloads/incomplete";

        return this.Ok(new Dictionary<string, object>
        {
            ["save_path"] = savePath,
            ["temp_path_enabled"] = !string.IsNullOrWhiteSpace(tempPath),
            ["temp_path"] = tempPath,
            ["listen_port"] = this.configService.ListeningPort,
            ["up_limit"] = this.configService.MaxUploadSpeedKbps * 1024,
            ["dl_limit"] = this.configService.MaxDownloadSpeedKbps * 1024,
            ["max_connec"] = this.configService.MaxGlobalConnections,
            ["max_connec_per_torrent"] = this.configService.MaxPerTorrentConnections,
            ["dht"] = this.configService.EnableDht,
            ["pex"] = this.configService.EnablePex,
            ["lsd"] = this.configService.EnableLpd,
            ["encryption"] = 1,
            ["anonymous_mode"] = false,
            ["queueing_enabled"] = true,
            ["max_active_downloads"] = this.configService.MaxActiveDownloads,
            ["max_active_uploads"] = this.configService.MaxActiveUploads,
            ["max_active_torrents"] = this.configService.MaxActiveTorrents,
            ["dont_count_slow_torrents"] = this.configService.IgnoreSlowTorrents,
            ["slow_torrent_dl_rate_threshold"] = this.configService.SlowTorrentDownloadRateThreshold,
            ["slow_torrent_ul_rate_threshold"] = this.configService.SlowTorrentUploadRateThreshold,
            ["incomplete_files_ext"] = this.configService.AppendIncompleteExtension,
            ["alt_dl_limit"] = this.configService.AltDownloadSpeedKbps * 1024,
            ["alt_up_limit"] = this.configService.AltUploadSpeedKbps * 1024,
            ["enable_embedded_tracker"] = this.configService.TrackerServerEnabled,
            ["embedded_tracker_port"] = this.configService.TrackerHttpPort,
            ["auto_shutdown_on_downloads_finished"] = !string.Equals(this.configService.AutoShutdownAction, "None", StringComparison.OrdinalIgnoreCase),
        });
    }

    [HttpGet("app/defaultSavePath")]
    public ActionResult<string> GetDefaultSavePath()
    {
        return this.Content(this.configService.DownloadDir ?? "/downloads", "text/plain");
    }

    [HttpGet("torrents/info")]
    public ActionResult<List<Dictionary<string, object>>> GetTorrentsInfo(
        [FromQuery] string filter = null,
        [FromQuery] string category = null,
        [FromQuery] string hashes = null)
    {
        var torrents = this.torrentService.GetAll();

        if (!string.IsNullOrEmpty(hashes) && !string.Equals(hashes.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim().ToLowerInvariant())
                .ToHashSet();
            torrents = torrents.Where(t => t.InfoHash != null && hashList.Contains(t.InfoHash.ToLowerInvariant()));
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
                ["last_activity"] = new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds(),
                ["is_private"] = t.IsPrivate,
                ["private"] = t.IsPrivate,
                ["super_seeding"] = t.InitialSeeding,
            };
        }).ToList();

        return this.Ok(result);
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
                    var added = await this.torrentService.AddFromMagnetAsync(trimmed, category, savepath, isPaused);
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
                            await this.torrentService.UpdateAsync(added);
                        }
                    }
                }
                else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var bytes = await this.safeHttpClientService.DownloadBytesAsync(trimmed);
                        var parsed = this.torrentFileParser.Parse(bytes);
                        var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savepath, isPaused, bytes);
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
                                await this.torrentService.UpdateAsync(added);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Failed to download torrent file from URL: {0}", trimmed);
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
                    var parsed = this.torrentFileParser.Parse(bytes);
                    var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savepath, isPaused, bytes);
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
                            await this.torrentService.UpdateAsync(added);
                        }
                    }
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setShareLimits")]
    public async Task<ActionResult> SetShareLimits(
        [FromForm] string hashes,
        [FromForm] double? ratioLimit = null,
        [FromForm] int? seedingTimeLimit = null,
        [FromForm] int? inactiveSeedingTimeLimit = null,
        [FromForm] int? maxRatioAction = null)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
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

            if (maxRatioAction.HasValue)
            {
                torrent.ShareLimitAction = maxRatioAction.Value switch
                {
                    1 => "Remove",
                    2 => "SuperSeeding",
                    3 => "RemoveWithData",
                    _ => "Pause",
                };
                updated = true;
            }

            if (updated)
            {
                await this.torrentService.UpdateAsync(torrent);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/renameFile")]
    public async Task<ActionResult> RenameFile(
        [FromForm] string hash,
        [FromForm] string oldPath,
        [FromForm] string newPath)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return this.BadRequest();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var success = await this.torrentService.RenameFileAsync(torrent.Id, oldPath, newPath);
        return success ? this.Content("Ok.", "text/plain") : this.StatusCode(StatusCodes.Status409Conflict, "Failed to rename file.");
    }

    [HttpPost("torrents/renameFolder")]
    public async Task<ActionResult> RenameFolder(
        [FromForm] string hash,
        [FromForm] string oldPath,
        [FromForm] string newPath)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return this.BadRequest();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var success = await this.torrentService.RenameFolderAsync(torrent.Id, oldPath, newPath);
        return success ? this.Content("Ok.", "text/plain") : this.StatusCode(StatusCodes.Status409Conflict, "Failed to rename folder.");
    }

    [HttpPost("torrents/setSuperSeeding")]
    public async Task<ActionResult> SetSuperSeeding(
        [FromForm] string hashes,
        [FromForm] bool value)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            await this.torrentService.SetSuperSeedingAsync(torrent.Id, value);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/create")]
    public async Task<ActionResult> CreateTorrent(
        [FromForm] string path,
        [FromForm] string name = null,
        [FromForm] string comment = null,
        [FromForm] string created_by = null,
        [FromForm] bool is_private = false,
        [FromForm] int piece_size = 0,
        [FromForm] string trackers = null,
        [FromForm] string webseeds = null,
        [FromForm] string output_path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.BadRequest("Path is required.");
        }

        var trackerList = !string.IsNullOrWhiteSpace(trackers)
            ? trackers.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();

        var webSeedList = !string.IsNullOrWhiteSpace(webseeds)
            ? webseeds.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();

        var request = new TorrentCreationRequest
        {
            Path = path,
            Name = name,
            Comment = comment,
            CreatedBy = created_by,
            IsPrivate = is_private,
            PieceLength = piece_size,
            Trackers = trackerList,
            WebSeeds = webSeedList,
            OutputPath = output_path,
        };

        var result = await this.torrentCreationService.CreateTorrentAsync(request);
        if (!result.Success)
        {
            return this.StatusCode(StatusCodes.Status500InternalServerError, result.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(output_path) && result.TorrentFileBytes != null)
        {
            return this.File(result.TorrentFileBytes, "application/x-bittorrent", $"{name ?? Path.GetFileName(path)}.torrent");
        }

        return this.Ok(new
        {
            success = true,
            infoHash = result.InfoHash,
            totalSize = result.TotalSize,
            pieceCount = result.PieceCount,
            pieceLength = result.PieceLength,
            outputPath = result.OutputPath,
        });
    }

    [HttpPost("torrents/setForceStart")]
    public async Task<ActionResult> SetForceStart(
        [FromForm] string hashes,
        [FromForm] string value = null,
        [FromForm] bool? enable = null)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        var force = enable ?? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.ForceStart = force;
            await this.torrentService.UpdateAsync(torrent);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/pause")]
    [HttpPost("torrents/stop")]
    public async Task<ActionResult> PauseTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            await this.torrentService.PauseAsync(torrent.Id);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/resume")]
    [HttpPost("torrents/start")]
    public async Task<ActionResult> ResumeTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            await this.torrentService.ResumeAsync(torrent.Id);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/delete")]
    public async Task<ActionResult> DeleteTorrents(
        [FromForm] string hashes,
        [FromForm] bool deleteFiles = false)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            await this.torrentService.DeleteAsync(torrent.Id, deleteFiles);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/files")]
    public ActionResult<List<Dictionary<string, object>>> GetFiles([FromQuery] string hash)
    {
        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var files = this.torrentFileService.GetFiles(torrent.Id).ToList();
        var downloadTask = this.torrentService.GetDownloadTask(torrent.Id);
        TorrentFileProgressEnricher.Enrich(torrent, files, downloadTask);
        var result = files.Select((f, index) => new Dictionary<string, object>
        {
            ["index"] = index,
            ["name"] = f.Path,
            ["size"] = f.Size,
            ["progress"] = f.Progress,
            ["priority"] = f.Priority,
            ["is_seed"] = f.Progress >= 1.0,
            ["piece_range"] = new[] { f.PieceOffset, f.PieceOffset + f.PieceCount - 1 },
        }).ToList();

        return this.Ok(result);
    }

    [HttpGet("torrents/tags")]
    public ActionResult<List<string>> GetTags()
    {
        var tags = this.torrentService.GetAll()
            .Select(t => t.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();
        return this.Ok(tags);
    }

    [HttpPost("torrents/createTags")]
    public ActionResult CreateTags([FromForm] string tags)
    {
        if (!string.IsNullOrWhiteSpace(tags) && this.tagRepository != null)
        {
            var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var existingTags = this.tagRepository.All().Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tagList)
            {
                var trimmed = t.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !existingTags.Contains(trimmed))
                {
                    this.tagRepository.Insert(new Tag { Label = trimmed });
                    existingTags.Add(trimmed);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/addTags")]
    public async Task<ActionResult> AddTags([FromForm] string hashes, [FromForm] string tags)
    {
        if (string.IsNullOrWhiteSpace(hashes) || string.IsNullOrEmpty(tags))
        {
            return this.Content("Ok.", "text/plain");
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.Label = tags;
            await this.torrentService.UpdateAsync(torrent);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTags")]
    public async Task<ActionResult> RemoveTags([FromForm] string hashes, [FromForm] string tags)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var tagsToRemove = (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var torrent in this.ResolveTorrents(hashes))
            {
                if (!string.IsNullOrEmpty(torrent.Label))
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

                    await this.torrentService.UpdateAsync(torrent);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/deleteTags")]
    public async Task<ActionResult> DeleteTags([FromForm] string tags)
    {
        if (!string.IsNullOrEmpty(tags))
        {
            var tagsToDelete = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var all = this.torrentService.GetAll();
            foreach (var torrent in all)
            {
                if (!string.IsNullOrEmpty(torrent.Label))
                {
                    var remaining = torrent.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Where(t => !tagsToDelete.Contains(t));
                    torrent.Label = string.Join(", ", remaining);
                    await this.torrentService.UpdateAsync(torrent);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/categories")]
    public ActionResult<Dictionary<string, object>> GetCategories()
    {
        var categories = this.categoryService.GetAll();
        var result = categories.ToDictionary(
            c => c.Name,
            c => (object)new { name = c.Name, savePath = c.SavePath });

        return this.Ok(result);
    }

    [HttpPost("torrents/createCategory")]
    public ActionResult CreateCategory([FromForm] string category, [FromForm] string savePath)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return this.BadRequest();
        }

        this.categoryService.Add(new Category
        {
            Name = category,
            SavePath = savePath ?? string.Empty,
        });

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setCategory")]
    public async Task<ActionResult> SetCategory([FromForm] string hashes, [FromForm] string category)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.Content("Ok.", "text/plain");
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.Category = category ?? string.Empty;
            await this.torrentService.UpdateAsync(torrent);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/editCategory")]
    public ActionResult EditCategory([FromForm] string category, [FromForm] string savePath)
    {
        var existing = this.categoryService.GetByName(category);
        if (existing != null)
        {
            existing.SavePath = savePath ?? string.Empty;
            this.categoryService.Update(existing);
        }
        else
        {
            this.categoryService.Add(new Category { Name = category, SavePath = savePath ?? string.Empty });
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeCategories")]
    public ActionResult RemoveCategories([FromForm] string categories)
    {
        if (!string.IsNullOrEmpty(categories))
        {
            var cats = categories.Split(new[] { '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var c in cats)
            {
                var existing = this.categoryService.GetByName(c);
                if (existing != null)
                {
                    this.categoryService.Delete(existing.Id);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/properties")]
    public ActionResult<Dictionary<string, object>> GetProperties([FromQuery] string hash)
    {
        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        return this.Ok(new Dictionary<string, object>
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
            ["share_ratio"] = torrent.Ratio,
            ["is_private"] = torrent.IsPrivate,
            ["private"] = torrent.IsPrivate,
            ["super_seeding"] = torrent.InitialSeeding,
        });
    }

    [HttpGet("torrents/trackers")]
    public ActionResult<List<Dictionary<string, object>>> GetTrackers([FromQuery] string hash)
    {
        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var dbTrackers = this.trackerEntryRepository.GetByTorrentId(torrent.Id).ToList();
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
                    ["msg"] = t.ErrorMessage ?? string.Empty,
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
                ["msg"] = string.Empty,
            });
        }

        return this.Ok(trackers);
    }

    [HttpPost("torrents/addTrackers")]
    public ActionResult AddTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = this.torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                var urlList = urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var url in urlList)
                {
                    var trimmed = url.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        this.trackerEntryRepository.Insert(new TrackerEntry
                        {
                            TorrentId = torrent.Id,
                            Url = trimmed,
                            Status = 1,
                            LastAnnounce = DateTime.UtcNow,
                        });
                    }
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTrackers")]
    public ActionResult RemoveTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = this.torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                var urlSet = urls.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var existing = this.trackerEntryRepository.GetByTorrentId(torrent.Id);
                foreach (var t in existing.Where(t => urlSet.Contains(t.Url)))
                {
                    this.trackerEntryRepository.Delete(t.Id);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/recheck")]
    public async Task<ActionResult> RecheckTorrents([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var torrent in this.ResolveTorrents(hashes))
            {
                await this.torrentService.ForceRecheckAsync(torrent.Id);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    private static readonly object SyncLock = new();
    private static int currentRid = 0;
    private static Dictionary<string, (QBitTorrentSnapshot Snapshot, int LastModifiedRid)> cachedTorrents = new(StringComparer.OrdinalIgnoreCase);
    private static List<(string Hash, int RemovedAtRid)> removedTorrents = new();

    public static void ResetSyncState()
    {
        lock (SyncLock)
        {
            currentRid = 0;
            cachedTorrents.Clear();
            removedTorrents.Clear();
        }
    }

    private record QBitTorrentSnapshot(
        string Name,
        long Size,
        double Progress,
        long DlSpeed,
        long UpSpeed,
        string State,
        string Category,
        string Tags,
        string SavePath,
        long Eta,
        double Ratio);

    [HttpGet("sync/maindata")]
    public ActionResult<Dictionary<string, object>> GetMainData([FromQuery] int rid = 0)
    {
        lock (SyncLock)
        {
            var torrents = this.torrentService.GetAll().ToList();
            var categories = this.categoryService.GetAll().ToDictionary(
                c => c.Name,
                c => (object)new { name = c.Name, savePath = c.SavePath });

            var serverState = new
            {
                dl_info_speed = torrents.Sum(t => t.DownloadSpeed),
                up_info_speed = torrents.Sum(t => t.UploadSpeed),
                dl_info_data = torrents.Sum(t => t.Downloaded),
                up_info_data = torrents.Sum(t => t.Uploaded),
                connection_status = "connected",
            };

            var currentHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // If rid == 0, or cached state is unavailable, or rid is out of sequence, perform a full update
            if (rid <= 0 || cachedTorrents.Count == 0 || rid > currentRid)
            {
                currentRid = rid <= 0 ? 1 : rid + 1;
                cachedTorrents.Clear();
                removedTorrents.Clear();

                var torrentDict = new Dictionary<string, object>();
                foreach (var t in torrents)
                {
                    currentHashes.Add(t.InfoHash);
                    var state = MapToQBitState(t.Status, t.Progress);
                    var snapshot = new QBitTorrentSnapshot(
                        t.Name,
                        t.TotalSize,
                        t.Progress,
                        t.DownloadSpeed,
                        t.UploadSpeed,
                        state,
                        t.Category ?? string.Empty,
                        t.Label ?? string.Empty,
                        t.SavePath ?? string.Empty,
                        t.Eta > 0 ? t.Eta : 8640000,
                        t.Ratio);

                    cachedTorrents[t.InfoHash] = (snapshot, currentRid);

                    torrentDict[t.InfoHash] = new
                    {
                        name = snapshot.Name,
                        size = snapshot.Size,
                        progress = snapshot.Progress,
                        dlspeed = snapshot.DlSpeed,
                        upspeed = snapshot.UpSpeed,
                        state = snapshot.State,
                        category = snapshot.Category,
                        tags = snapshot.Tags,
                        save_path = snapshot.SavePath,
                        eta = snapshot.Eta,
                        ratio = snapshot.Ratio,
                    };
                }

                var fullResult = new Dictionary<string, object>
                {
                    ["rid"] = currentRid,
                    ["full_update"] = true,
                    ["torrents"] = torrentDict,
                    ["categories"] = categories,
                    ["server_state"] = serverState,
                };

                return this.Ok(fullResult);
            }

            // Incremental delta sync
            var nextRid = currentRid + 1;
            var updatedTorrents = new Dictionary<string, object>();

            foreach (var t in torrents)
            {
                currentHashes.Add(t.InfoHash);
                var state = MapToQBitState(t.Status, t.Progress);
                var snapshot = new QBitTorrentSnapshot(
                    t.Name,
                    t.TotalSize,
                    t.Progress,
                    t.DownloadSpeed,
                    t.UploadSpeed,
                    state,
                    t.Category ?? string.Empty,
                    t.Label ?? string.Empty,
                    t.SavePath ?? string.Empty,
                    t.Eta > 0 ? t.Eta : 8640000,
                    t.Ratio);

                if (!cachedTorrents.TryGetValue(t.InfoHash, out var existing) || existing.Snapshot != snapshot)
                {
                    cachedTorrents[t.InfoHash] = (snapshot, nextRid);
                    updatedTorrents[t.InfoHash] = new
                    {
                        name = snapshot.Name,
                        size = snapshot.Size,
                        progress = snapshot.Progress,
                        dlspeed = snapshot.DlSpeed,
                        upspeed = snapshot.UpSpeed,
                        state = snapshot.State,
                        category = snapshot.Category,
                        tags = snapshot.Tags,
                        save_path = snapshot.SavePath,
                        eta = snapshot.Eta,
                        ratio = snapshot.Ratio,
                    };
                }
            }

            // Detect removed torrents
            var removedNow = cachedTorrents.Keys.Where(h => !currentHashes.Contains(h)).ToList();
            foreach (var hash in removedNow)
            {
                cachedTorrents.Remove(hash);
                removedTorrents.Add((hash, nextRid));
            }

            if (removedTorrents.Count > 500)
            {
                removedTorrents.RemoveRange(0, removedTorrents.Count - 500);
            }

            var torrentsRemoved = removedTorrents
                .Where(r => r.RemovedAtRid > rid)
                .Select(r => r.Hash)
                .ToList();

            currentRid = nextRid;

            var deltaResult = new Dictionary<string, object>
            {
                ["rid"] = currentRid,
                ["full_update"] = false,
                ["torrents"] = updatedTorrents,
                ["torrents_removed"] = torrentsRemoved,
                ["categories"] = categories,
                ["server_state"] = serverState,
            };

            return this.Ok(deltaResult);
        }
    }

    [HttpGet("transfer/info")]
    public ActionResult<Dictionary<string, object>> GetTransferInfo()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var result = new Dictionary<string, object>
        {
            ["dl_info_speed"] = torrents.Sum(t => t.DownloadSpeed),
            ["up_info_speed"] = torrents.Sum(t => t.UploadSpeed),
            ["dl_info_data"] = torrents.Sum(t => t.Downloaded),
            ["up_info_data"] = torrents.Sum(t => t.Uploaded),
            ["connection_status"] = "connected",
        };

        return this.Ok(result);
    }

    [HttpPost("torrents/setLocation")]
    [HttpPost("torrents/setSavePath")]
    public async Task<ActionResult> SetLocation([FromForm] string hashes, [FromForm] string location)
    {
        if (!string.IsNullOrWhiteSpace(hashes) && !string.IsNullOrWhiteSpace(location))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                await this.torrentService.SetLocationAsync(t.Id, location, moveFiles: true);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/topPrio")]
    public async Task<ActionResult> TopPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                await this.torrentService.MoveQueueAsync(t.Id, "top");
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/bottomPrio")]
    public async Task<ActionResult> BottomPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                await this.torrentService.MoveQueueAsync(t.Id, "bottom");
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/increasePrio")]
    public async Task<ActionResult> IncreasePrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                await this.torrentService.MoveQueueAsync(t.Id, "up");
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/decreasePrio")]
    public async Task<ActionResult> DecreasePrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                await this.torrentService.MoveQueueAsync(t.Id, "down");
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setDownloadLimit")]
    public ActionResult SetTransferDownloadLimit([FromForm] long limit)
    {
        this.configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxDownloadSpeedKbps"] = (int)(limit / 1024) });
        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setUploadLimit")]
    public ActionResult SetTransferUploadLimit([FromForm] long limit)
    {
        this.configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxUploadSpeedKbps"] = (int)(limit / 1024) });
        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setDownloadLimit")]
    public async Task<ActionResult> SetTorrentDownloadLimit([FromForm] string hashes, [FromForm] long limit)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                t.DownloadLimit = (int)(limit / 1024);
                await this.torrentService.UpdateAsync(t);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setUploadLimit")]
    public async Task<ActionResult> SetTorrentUploadLimit([FromForm] string hashes, [FromForm] long limit)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in this.ResolveTorrents(hashes))
            {
                t.UploadLimit = (int)(limit / 1024);
                await this.torrentService.UpdateAsync(t);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/filePrio")]
    public async Task<ActionResult> FilePrio([FromForm] string hash, [FromForm] string id, [FromForm] int priority)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(id))
        {
            var t = this.torrentService.GetByInfoHash(hash);
            if (t != null)
            {
                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                var idStrings = id.Split('|', StringSplitOptions.RemoveEmptyEntries);
                foreach (var idStr in idStrings)
                {
                    if (int.TryParse(idStr, out var fileIndex) && fileIndex >= 0 && fileIndex < files.Count)
                    {
                        await this.torrentFileService.SetPriorityAsync(files[fileIndex].Id, priority);
                    }
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("search/start")]
    public ActionResult StartSearch(
        [FromForm] string pattern,
        [FromForm] string plugins = null,
        [FromForm] string category = null)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return this.BadRequest("Pattern is required.");
        }

        var id = this.qbittorrentSearchService.StartSearch(pattern, plugins, category);
        return this.Ok(new { id });
    }

    [HttpPost("search/stop")]
    public ActionResult StopSearch([FromForm] int id)
    {
        this.qbittorrentSearchService.StopSearch(id);
        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("search/status")]
    [HttpPost("search/status")]
    public ActionResult GetSearchStatus([FromQuery] int? id, [FromForm] int? formId)
    {
        var targetId = id ?? formId;
        if (targetId.HasValue)
        {
            var status = this.qbittorrentSearchService.GetStatus(targetId.Value);
            if (status == null)
            {
                return this.NotFound();
            }

            return this.Ok(new[] { status });
        }

        return this.Ok(this.qbittorrentSearchService.GetAllStatuses());
    }

    [HttpGet("search/results")]
    [HttpPost("search/results")]
    public ActionResult GetSearchResults(
        [FromQuery] int id,
        [FromQuery] int limit = 0,
        [FromQuery] int offset = 0,
        [FromForm] int? formId = null,
        [FromForm] int? formLimit = null,
        [FromForm] int? formOffset = null)
    {
        var searchId = formId ?? id;
        var searchLimit = formLimit ?? limit;
        var searchOffset = formOffset ?? offset;

        var results = this.qbittorrentSearchService.GetResults(searchId, searchLimit, searchOffset);
        return this.Ok(results);
    }

    [HttpPost("search/delete")]
    public ActionResult DeleteSearch([FromForm] int id)
    {
        this.qbittorrentSearchService.DeleteSearch(id);
        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("search/plugins")]
    public ActionResult GetSearchPlugins()
    {
        return this.Ok(this.qbittorrentSearchService.GetPlugins());
    }

    [HttpGet("search/categories")]
    public ActionResult GetSearchCategories()
    {
        return this.Ok(this.qbittorrentSearchService.GetCategories());
    }

    private IEnumerable<Torrent> ResolveTorrents(string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return Enumerable.Empty<Torrent>();
        }

        if (string.Equals(hashes.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return this.torrentService.GetAll();
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<Torrent>();
        foreach (var hash in hashList)
        {
            var torrent = this.torrentService.GetByInfoHash(hash.Trim());
            if (torrent != null)
            {
                result.Add(torrent);
            }
        }

        return result;
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
            TorrentStatus.Stopped => progress >= 1.0 ? "pausedUP" : "pausedDL",
            TorrentStatus.Completed => progress >= 1.0 ? "pausedUP" : "pausedDL",
            TorrentStatus.Error => "error",
            TorrentStatus.Stalled => progress >= 1.0 ? "stalledUP" : "stalledDL",
            _ => "unknown",
        };
    }
}
