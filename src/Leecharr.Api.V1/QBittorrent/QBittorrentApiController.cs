// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.BitTorrent.Creation;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers.Search;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Api.V1.QBittorrent;

[AllowAnonymous]
[ApiController]
[Route("api/v2")]
public class QBittorrentApiController : ControllerBase, IActionFilter
{
    private static readonly RpcSessionStore authenticatedSessions = new();
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
    private readonly IDownloadEngine downloadEngine;
    private readonly ISpeedSchedulerService speedSchedulerService;
    private readonly IDiskProvider diskProvider;
    private readonly IStoragePathService storagePathService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IPeerConnectionHistoryService peerConnectionHistoryService;
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
        ISafeHttpClientService safeHttpClientService = null,
        IDownloadEngine downloadEngine = null,
        ISpeedSchedulerService speedSchedulerService = null,
        IDiskProvider diskProvider = null,
        IStoragePathService storagePathService = null,
        IAppFolderInfo appFolderInfo = null,
        IPeerConnectionHistoryService peerConnectionHistoryService = null)
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
        this.downloadEngine = downloadEngine;
        this.speedSchedulerService = speedSchedulerService;
        this.diskProvider = diskProvider;
        this.storagePathService = storagePathService;
        this.appFolderInfo = appFolderInfo;
        this.peerConnectionHistoryService = peerConnectionHistoryService;
    }

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        string actionName = null;
        if (context.ActionDescriptor?.RouteValues != null &&
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var val))
        {
            actionName = val;
        }

        if (string.IsNullOrEmpty(actionName) &&
            context.ActionDescriptor is ControllerActionDescriptor cad)
        {
            actionName = cad.ActionName;
        }

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
            if (authenticatedSessions.IsValid(sid))
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
            if (RpcAuthenticationHelper.FixedTimeEquals(apiKey, this.configFileProvider.ApiKey))
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
                ((!string.IsNullOrWhiteSpace(password) && RpcAuthenticationHelper.FixedTimeEquals(password, masterApiKey)) ||
                 (!string.IsNullOrWhiteSpace(username) && RpcAuthenticationHelper.FixedTimeEquals(username, masterApiKey))))
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
        authenticatedSessions.SetSession(sid, DateTime.UtcNow.AddDays(7));

        this.Response.Cookies.Append("SID", sid, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = this.Request.IsHttps,
        });

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("auth/logout")]
    public ActionResult Logout()
    {
        if (this.Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
        {
            authenticatedSessions.RemoveSession(sid);
            sessionSyncStates.TryRemove($"sid:{sid}", out _);
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

    [HttpPost("app/setPreferences")]
    public async Task<ActionResult> SetPreferencesAsync([FromForm] string json = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (this.Request?.Body != null && this.Request.Body.CanRead)
                {
                    using var reader = new StreamReader(this.Request.Body);
                    json = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read body stream in SetPreferences");
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return this.Content("Ok.", "text/plain");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var dict = new Dictionary<string, object>();

            if (root.TryGetProperty("save_path", out var sp) && sp.ValueKind == JsonValueKind.String)
            {
                dict["DownloadDir"] = sp.GetString();
            }

            if (root.TryGetProperty("temp_path", out var tp) && tp.ValueKind == JsonValueKind.String)
            {
                dict["IncompleteDownloadDir"] = tp.GetString();
            }

            if (root.TryGetProperty("listen_port", out var lp) && lp.TryGetInt32(out var port))
            {
                dict["ListeningPort"] = port;
            }

            if (root.TryGetProperty("dl_limit", out var dl) && dl.TryGetInt32(out var dlBytes))
            {
                dict["MaxDownloadSpeedKbps"] = dlBytes / 1024;
            }

            if (root.TryGetProperty("up_limit", out var ul) && ul.TryGetInt32(out var ulBytes))
            {
                dict["MaxUploadSpeedKbps"] = ulBytes / 1024;
            }

            if (root.TryGetProperty("alt_dl_limit", out var adl) && adl.TryGetInt32(out var adlBytes))
            {
                dict["AltDownloadSpeedKbps"] = adlBytes / 1024;
            }

            if (root.TryGetProperty("alt_up_limit", out var aul) && aul.TryGetInt32(out var aulBytes))
            {
                dict["AltUploadSpeedKbps"] = aulBytes / 1024;
            }

            if (root.TryGetProperty("max_connec", out var mc) && mc.TryGetInt32(out var maxConnec))
            {
                dict["MaxGlobalConnections"] = maxConnec;
            }

            if (root.TryGetProperty("max_connec_per_torrent", out var mcpt) && mcpt.TryGetInt32(out var maxPerTor))
            {
                dict["MaxPerTorrentConnections"] = maxPerTor;
            }

            if (root.TryGetProperty("max_active_downloads", out var mad) && mad.TryGetInt32(out var maxActDl))
            {
                dict["MaxActiveDownloads"] = maxActDl;
            }

            if (root.TryGetProperty("max_active_uploads", out var mau) && mau.TryGetInt32(out var maxActUl))
            {
                dict["MaxActiveUploads"] = maxActUl;
            }

            if (root.TryGetProperty("max_active_torrents", out var mat) && mat.TryGetInt32(out var maxActTor))
            {
                dict["MaxActiveTorrents"] = maxActTor;
            }

            if (root.TryGetProperty("dht", out var dht) && (dht.ValueKind == JsonValueKind.True || dht.ValueKind == JsonValueKind.False))
            {
                dict["EnableDht"] = dht.GetBoolean();
            }

            if (root.TryGetProperty("pex", out var pex) && (pex.ValueKind == JsonValueKind.True || pex.ValueKind == JsonValueKind.False))
            {
                dict["EnablePex"] = pex.GetBoolean();
            }

            if (root.TryGetProperty("lsd", out var lsd) && (lsd.ValueKind == JsonValueKind.True || lsd.ValueKind == JsonValueKind.False))
            {
                dict["EnableLpd"] = lsd.GetBoolean();
            }

            if (dict.Count > 0)
            {
                this.configService.SaveConfigDictionary(dict);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to parse qBittorrent preferences payload");
        }

        return this.Content("Ok.", "text/plain");
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
        [FromQuery] string tag = null,
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

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var filterTags = tag.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (filterTags.Count > 0)
            {
                torrents = torrents.Where(t =>
                {
                    if (string.IsNullOrEmpty(t.Label))
                    {
                        return false;
                    }

                    var torrentTags = t.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l));

                    return filterTags.Any(ft => torrentTags.Any(tt => string.Equals(tt, ft, StringComparison.OrdinalIgnoreCase)));
                });
            }
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
                case "stalled":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Stalled || (t.Status == TorrentStatus.Downloading && t.DownloadSpeed == 0) || (t.Status == TorrentStatus.Seeding && t.UploadSpeed == 0));
                    break;
                case "stalled_downloading":
                    torrents = torrents.Where(t => (t.Status == TorrentStatus.Stalled && t.Progress < 1.0) || (t.Status == TorrentStatus.Downloading && t.DownloadSpeed == 0));
                    break;
                case "stalled_uploading":
                    torrents = torrents.Where(t => (t.Status == TorrentStatus.Stalled && t.Progress >= 1.0) || (t.Status == TorrentStatus.Seeding && t.UploadSpeed == 0));
                    break;
                case "checking":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Checking);
                    break;
                case "errored":
                case "error":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Error);
                    break;
                case "resumed":
                case "running":
                    torrents = torrents.Where(t => t.Status != TorrentStatus.Paused && t.Status != TorrentStatus.Stopped);
                    break;
            }
        }

        var torrentList = torrents.ToList();
        var filesByTorrent = this.torrentFileService?.GetFilesForTorrents(torrentList.Select(t => t.Id)) ?? new Dictionary<int, List<TorrentFile>>();

        var result = torrentList.Select(t =>
        {
            var state = MapToQBitState(t.Status, t.Progress);
            var (resolvedSavePath, resolvedContentPath) = this.ResolvePaths(t, filesByTorrent);
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
                ["eta"] = CalculateEta(t),
                ["state"] = state,
                ["seq_dl"] = t.SequentialDownload,
                ["f_l_piece_prio"] = t.FirstLastPiecePriority,
                ["category"] = t.Category ?? string.Empty,
                ["tags"] = t.Label ?? string.Empty,
                ["save_path"] = resolvedSavePath,
                ["content_path"] = resolvedContentPath,
                ["added_on"] = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds(),
                ["completion_on"] = t.DateCompleted.HasValue ? new DateTimeOffset(t.DateCompleted.Value).ToUnixTimeSeconds() : -1,
                ["amount_left"] = t.Progress >= 1.0 ? 0L : (long)Math.Max(0, (1.0 - t.Progress) * t.TotalSize),
                ["downloaded"] = t.Downloaded,
                ["uploaded"] = t.Uploaded,
                ["max_ratio"] = t.TargetRatio,
                ["max_seeding_time"] = t.TargetSeedTimeMinutes * 60,
                ["ratio_limit"] = t.TargetRatio > 0 ? t.TargetRatio : -2.0,
                ["seeding_time_limit"] = t.TargetSeedTimeMinutes > 0 ? t.TargetSeedTimeMinutes * 60 : -2,
                ["seeding_time"] = t.SeedingTimeSeconds,
                ["last_activity"] = new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds(),
                ["is_private"] = t.IsPrivate,
                ["private"] = t.IsPrivate,
                ["super_seeding"] = t.InitialSeeding,
            };
        }).ToList();

        return this.Ok(result);
    }

    [HttpPost("torrents/add")]
    public async Task<ActionResult> AddTorrents([FromForm] QBitAddTorrentsRequest request = null)
    {
        request ??= new QBitAddTorrentsRequest();

        var addedFromUrls = await this.AddTorrentsFromUrlsAsync(request);
        var addedFromFiles = await this.AddTorrentsFromFilesAsync(request);

        if (addedFromUrls + addedFromFiles > 0)
        {
            return this.Content("Ok.", "text/plain");
        }

        return this.Content("Fails.", "text/plain");
    }

    private async Task<int> AddTorrentsFromUrlsAsync(QBitAddTorrentsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Urls))
        {
            return 0;
        }

        var addedCount = 0;
        var lines = request.Urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var url in lines)
        {
            var trimmed = url.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            try
            {
                if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await this.torrentService.AddFromMagnetAsync(trimmed, request.Category, request.EffectiveSavePath, request.IsPaused);
                    if (added != null)
                    {
                        await this.ApplyTorrentRequestOptionsAsync(added, request);
                        addedCount++;
                    }

                    continue;
                }

                if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await this.AddTorrentFromHttpUrlAsync(trimmed, request);
                    if (added)
                    {
                        addedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to add torrent from URL: {0}", trimmed);
            }
        }

        return addedCount;
    }

    private long GetMaxTorrentFileSizeBytes()
    {
        if (this.configService != null && this.configService.MaxTorrentFileSizeBytes > 0)
        {
            return this.configService.MaxTorrentFileSizeBytes;
        }

        if (this.configFileProvider != null && this.configFileProvider.MaxTorrentFileSizeBytes > 0)
        {
            return this.configFileProvider.MaxTorrentFileSizeBytes;
        }

        return 250L * 1024 * 1024;
    }

    private async Task<bool> AddTorrentFromHttpUrlAsync(string url, QBitAddTorrentsRequest request)
    {
        try
        {
            var maxTorrentBytes = this.GetMaxTorrentFileSizeBytes();
            byte[] bytes;
            if (!string.IsNullOrWhiteSpace(request.EffectiveCookie) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var customHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cookie"] = request.EffectiveCookie,
                };
                bytes = await this.safeHttpClientService.DownloadBytesAsync(uri, maxSizeBytes: maxTorrentBytes, customHeaders: customHeaders);
            }
            else
            {
                bytes = await this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: maxTorrentBytes);
            }

            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            var parsed = this.torrentFileParser.Parse(bytes);
            if (parsed == null)
            {
                return false;
            }

            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.EffectiveSavePath, request.IsPaused, bytes);
            if (added != null)
            {
                await this.ApplyTorrentRequestOptionsAsync(added, request);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to download torrent file from URL: {0}", url);
            return false;
        }
    }

    private async Task<int> AddTorrentsFromFilesAsync(QBitAddTorrentsRequest request)
    {
        if (request?.Torrents == null || request.Torrents.Count == 0)
        {
            return 0;
        }

        var addedCount = 0;
        var maxTorrentBytes = this.GetMaxTorrentFileSizeBytes();

        foreach (var file in request.Torrents)
        {
            if (file == null || file.Length <= 0)
            {
                continue;
            }

            if (file.Length > maxTorrentBytes)
            {
                this.logger.Warn("Uploaded torrent file exceeds maximum allowed size: {0} bytes (max: {1})", file.Length, maxTorrentBytes);
                continue;
            }

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                if (bytes.Length > maxTorrentBytes)
                {
                    this.logger.Warn("Uploaded torrent bytes exceed maximum allowed size: {0} bytes (max: {1})", bytes.Length, maxTorrentBytes);
                    continue;
                }

                var parsed = this.torrentFileParser.Parse(bytes);
                if (parsed == null)
                {
                    this.logger.Warn("Failed to parse uploaded torrent file: {0}", file.FileName);
                    continue;
                }

                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, request.Category, request.EffectiveSavePath, request.IsPaused, bytes);
                if (added != null)
                {
                    await this.ApplyTorrentRequestOptionsAsync(added, request);
                    addedCount++;
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to parse or add uploaded torrent file: {0}", file.FileName);
            }
        }

        return addedCount;
    }

    private async Task ApplyTorrentRequestOptionsAsync(Torrent added, QBitAddTorrentsRequest request)
    {
        if (added == null || request == null)
        {
            return;
        }

        var needsUpdate = false;
        if (!string.IsNullOrWhiteSpace(request.Tags))
        {
            added.Label = request.Tags;
            needsUpdate = true;
        }

        if (request.IsSequential)
        {
            added.SequentialDownload = true;
            needsUpdate = true;
        }

        if (request.IsFirstLastPiecePrio)
        {
            added.FirstLastPiecePriority = true;
            needsUpdate = true;
        }

        if (request.RatioLimit.HasValue && request.RatioLimit.Value > 0)
        {
            added.TargetRatio = request.RatioLimit.Value;
            needsUpdate = true;
        }

        if (request.SeedingTimeLimit.HasValue && request.SeedingTimeLimit.Value > 0)
        {
            added.TargetSeedTimeMinutes = (int)(request.SeedingTimeLimit.Value / 60);
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            await this.torrentService.UpdateAsync(added);
        }
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
                torrent.TargetSeedTimeMinutes = seedingTimeLimit.Value >= 0 ? (int)(seedingTimeLimit.Value / 60) : 0;
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

    [HttpPost("torrents/rename")]
    public async Task<ActionResult> RenameTorrent(
        [FromForm] string hash,
        [FromForm] string name)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(name))
        {
            return this.BadRequest();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        torrent.Name = name.Trim();
        await this.torrentService.UpdateAsync(torrent);
        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/renameFile")]
    public async Task<ActionResult> RenameFile(
        [FromForm] string hash,
        [FromForm] string oldPath = null,
        [FromForm] string newPath = null,
        [FromForm] int? id = null,
        [FromForm] int? fileId = null)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(newPath))
        {
            return this.BadRequest();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var targetOldPath = oldPath;
        if (string.IsNullOrWhiteSpace(targetOldPath))
        {
            var targetIndex = id ?? fileId;
            if (targetIndex.HasValue && targetIndex.Value >= 0)
            {
                var files = this.torrentFileService.GetFiles(torrent.Id).ToList();
                if (targetIndex.Value < files.Count)
                {
                    targetOldPath = files[targetIndex.Value].Path;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(targetOldPath))
        {
            return this.BadRequest();
        }

        var normalizedOldPath = targetOldPath.Replace('\\', '/');
        var parentDir = Path.GetDirectoryName(normalizedOldPath)?.Replace('\\', '/');
        var effectiveNewPath = !string.IsNullOrEmpty(parentDir) && parentDir != "." && !newPath.Contains('/') && !newPath.Contains('\\')
            ? $"{parentDir}/{newPath}"
            : newPath;
        var success = await this.torrentService.RenameFileAsync(torrent.Id, targetOldPath, effectiveNewPath);
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
            if (value && (torrent.Progress < 1.0 || torrent.Status != TorrentStatus.Seeding))
            {
                return this.BadRequest("Super seeding can only be enabled for 100% completed seeding torrents.");
            }

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
            if (force)
            {
                await this.torrentService.ResumeAsync(torrent.Id);
            }
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
            ["priority"] = ToQBittorrentPriority(f.Priority),
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

        var newTags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (newTags.Count == 0)
        {
            return this.Content("Ok.", "text/plain");
        }

        if (this.tagRepository != null)
        {
            var existingTags = this.tagRepository.All().Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in newTags)
            {
                if (!existingTags.Contains(tag))
                {
                    this.tagRepository.Insert(new Tag { Label = tag });
                    existingTags.Add(tag);
                }
            }
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            var currentTags = (torrent.Label ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            foreach (var tag in newTags)
            {
                if (!currentTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    currentTags.Add(tag);
                }
            }

            torrent.Label = string.Join(", ", currentTags);
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
            c => (object)new { name = c.Name, savePath = c.SavePath ?? string.Empty });

        return this.Ok(result);
    }

    [HttpPost("torrents/createCategory")]
    public ActionResult CreateCategory([FromForm] string category, [FromForm] string savePath)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return this.BadRequest();
        }

        var normalized = CategoryService.NormalizeCategoryName(category);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return this.BadRequest();
        }

        this.categoryService.Add(new Category
        {
            Name = normalized,
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

        var normalized = CategoryService.NormalizeCategoryName(category);
        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            await this.torrentService.SetCategoryAsync(torrent.Id, normalized);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/editCategory")]
    public ActionResult EditCategory([FromForm] string category, [FromForm] string savePath)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return this.BadRequest();
        }

        var normalized = CategoryService.NormalizeCategoryName(category);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return this.BadRequest();
        }

        var existing = this.categoryService.GetByName(normalized) ?? this.categoryService.GetByName(category);
        if (existing != null)
        {
            existing.SavePath = savePath ?? string.Empty;
            this.categoryService.Update(existing);
        }
        else
        {
            this.categoryService.Add(new Category { Name = normalized, SavePath = savePath ?? string.Empty });
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
                var normalized = CategoryService.NormalizeCategoryName(c);
                var existing = this.categoryService.GetByName(normalized) ?? this.categoryService.GetByName(c);
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

        var addedDate = new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds();
        var completionDate = torrent.DateCompleted.HasValue ? new DateTimeOffset(torrent.DateCompleted.Value).ToUnixTimeSeconds() : 0L;
        var creationDate = torrent.CreationDate.HasValue ? new DateTimeOffset(torrent.CreationDate.Value).ToUnixTimeSeconds() : addedDate;
        var totalWasted = this.downloadEngine?.GetTorrentResourceMetrics(torrent.Id)?.WastedBytes ?? 0L;
        var pieceSize = torrent.PieceLength > 0
            ? torrent.PieceLength
            : (torrent.PieceCount > 0 && torrent.TotalSize > 0 ? (int)(torrent.TotalSize / torrent.PieceCount) : 0);

        var (resolvedSavePath, _) = ResolvePaths(torrent);
        return this.Ok(new Dictionary<string, object>
        {
            ["save_path"] = resolvedSavePath,
            ["creation_date"] = creationDate,
            ["addition_date"] = addedDate,
            ["completion_date"] = completionDate,
            ["created_by"] = torrent.CreatedBy ?? string.Empty,
            ["dl_speed"] = torrent.DownloadSpeed,
            ["dl_speed_avg"] = torrent.DownloadSpeed,
            ["up_speed"] = torrent.UploadSpeed,
            ["up_speed_avg"] = torrent.UploadSpeed,
            ["eta"] = torrent.Eta,
            ["peers"] = torrent.Leechers,
            ["peers_total"] = torrent.Leechers,
            ["seeds"] = torrent.Seeders,
            ["seeds_total"] = torrent.Seeders,
            ["total_size"] = torrent.TotalSize,
            ["total_wasted"] = totalWasted,
            ["piece_size"] = pieceSize,
            ["pieces_num"] = torrent.PieceCount,
            ["pieces_have"] = (int)(torrent.PieceCount * torrent.Progress),
            ["total_downloaded"] = torrent.Downloaded,
            ["total_uploaded"] = torrent.Uploaded,
            ["up_limit"] = torrent.UploadLimit,
            ["dl_limit"] = torrent.DownloadLimit,
            ["time_elapsed"] = (int)(DateTime.UtcNow - torrent.DateAdded).TotalSeconds,
            ["seeding_time"] = (int)torrent.SeedingTimeSeconds,
            ["nb_connections"] = torrent.Seeders + torrent.Leechers,
            ["share_ratio"] = torrent.Ratio,
            ["is_private"] = torrent.IsPrivate,
            ["private"] = torrent.IsPrivate,
            ["super_seeding"] = torrent.InitialSeeding,
            ["comment"] = torrent.Comment ?? string.Empty,
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
                int qbStatus;
                if (!t.Enabled)
                {
                    qbStatus = 0;
                }
                else if (t.Status == 2 || !string.IsNullOrWhiteSpace(t.ErrorMessage))
                {
                    qbStatus = 4;
                }
                else if (t.Status == 0)
                {
                    qbStatus = 1;
                }
                else
                {
                    qbStatus = 2;
                }

                trackers.Add(new Dictionary<string, object>
                {
                    ["url"] = t.Url ?? string.Empty,
                    ["status"] = qbStatus,
                    ["tier"] = t.Tier,
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
                ["tier"] = 0,
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
    public async Task<ActionResult> AddTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = this.torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                if (torrent.IsPrivate)
                {
                    return this.BadRequest("Cannot add public trackers to private torrents");
                }

                var existingTrackers = (this.trackerEntryRepository.GetByTorrentId(torrent.Id) ?? Enumerable.Empty<TrackerEntry>()).ToList();
                var existingUrls = existingTrackers
                    .Where(t => !string.IsNullOrWhiteSpace(t.Url))
                    .Select(t => t.Url.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var rawLines = urls.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var validUrls = new List<string>();
                var now = DateTime.UtcNow;

                var currentTier = existingTrackers.Count > 0 ? existingTrackers.Max(t => t.Tier) + 1 : 0;
                var hasTrackersInCurrentTier = false;

                foreach (var rawLine in rawLines)
                {
                    var trimmed = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        if (hasTrackersInCurrentTier)
                        {
                            currentTier++;
                            hasTrackersInCurrentTier = false;
                        }
                    }
                    else
                    {
                        if (!existingUrls.Contains(trimmed))
                        {
                            existingUrls.Add(trimmed);
                            validUrls.Add(trimmed);

                            this.trackerEntryRepository.Insert(new TrackerEntry
                            {
                                TorrentId = torrent.Id,
                                Url = trimmed,
                                Tier = currentTier,
                                Enabled = true,
                                Status = 0,
                                Seeders = 0,
                                Leechers = 0,
                                Downloaded = 0,
                                TotalAnnounces = 0,
                                SuccessfulAnnounces = 0,
                                AnnounceInterval = 1800,
                                LastAnnounce = null,
                                NextAnnounce = now.AddSeconds(1800),
                            });
                        }

                        hasTrackersInCurrentTier = true;
                    }
                }

                if (validUrls.Count > 0 && this.downloadEngine != null)
                {
                    await this.downloadEngine.AddTrackersAsync(torrent.Id, validUrls);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTrackers")]
    public async Task<ActionResult> RemoveTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = this.torrentService.GetByInfoHash(hash);
            if (torrent != null)
            {
                var urlSet = urls.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (this.downloadEngine != null)
                {
                    await this.downloadEngine.RemoveTrackersAsync(torrent.Id, urlSet);
                }

                var existing = this.trackerEntryRepository.GetByTorrentId(torrent.Id);
                foreach (var t in existing.Where(t => urlSet.Contains(t.Url)))
                {
                    this.trackerEntryRepository.Delete(t.Id);
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/editTracker")]
    public async Task<ActionResult> EditTracker(
        [FromForm] string hash,
        [FromForm] string origUrl,
        [FromForm] string newUrl)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(origUrl) || string.IsNullOrWhiteSpace(newUrl))
        {
            return this.BadRequest();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var trimmedOrig = origUrl.Trim();
        var trimmedNew = newUrl.Trim();

        var existing = this.trackerEntryRepository.GetByTorrentId(torrent.Id);
        var match = existing.FirstOrDefault(t => string.Equals(t.Url, trimmedOrig, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            match.Url = trimmedNew;
            this.trackerEntryRepository.Update(match);
        }

        if (this.downloadEngine != null)
        {
            await this.downloadEngine.RemoveTrackersAsync(torrent.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { trimmedOrig });
            await this.downloadEngine.AddTrackersAsync(torrent.Id, new List<string> { trimmedNew });
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

    [HttpPost("torrents/reannounce")]
    public async Task<ActionResult> ReannounceTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            await this.torrentService.ForceAnnounceAsync(torrent.Id);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/toggleSequentialDownload")]
    public async Task<ActionResult> ToggleSequentialDownload([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.SequentialDownload = !torrent.SequentialDownload;
            await this.torrentService.UpdateAsync(torrent);
            if (this.downloadEngine != null)
            {
                await this.downloadEngine.SetSequentialDownloadAsync(torrent.Id, torrent.SequentialDownload);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setSequentialDownload")]
    public async Task<ActionResult> SetSequentialDownload(
        [FromForm] string hashes,
        [FromForm] string value = null,
        [FromForm] bool? enable = null,
        [FromForm] bool? sequential = null)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        var enabled = enable ?? sequential ?? (bool.TryParse(value, out var b) ? b : true);

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.SequentialDownload = enabled;
            await this.torrentService.UpdateAsync(torrent);
            if (this.downloadEngine != null)
            {
                await this.downloadEngine.SetSequentialDownloadAsync(torrent.Id, torrent.SequentialDownload);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/toggleFirstLastPiecePrio")]
    public async Task<ActionResult> ToggleFirstLastPiecePrio([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.FirstLastPiecePriority = !torrent.FirstLastPiecePriority;
            await this.torrentService.UpdateAsync(torrent);
            if (this.downloadEngine != null)
            {
                await this.downloadEngine.SetFirstLastPiecePriorityAsync(torrent.Id, torrent.FirstLastPiecePriority);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setFirstLastPiecePrio")]
    public async Task<ActionResult> SetFirstLastPiecePrio(
        [FromForm] string hashes,
        [FromForm] string value = null,
        [FromForm] bool? enable = null)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return this.BadRequest();
        }

        var enabled = enable ?? (bool.TryParse(value, out var b) ? b : true);

        foreach (var torrent in this.ResolveTorrents(hashes))
        {
            torrent.FirstLastPiecePriority = enabled;
            await this.torrentService.UpdateAsync(torrent);
            if (this.downloadEngine != null)
            {
                await this.downloadEngine.SetFirstLastPiecePriorityAsync(torrent.Id, torrent.FirstLastPiecePriority);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setPiecePriority")]
    public ActionResult SetPiecePriority(
        [FromForm] string hash = null,
        [FromForm] string hashes = null,
        [FromForm] int? piece = null,
        [FromForm] int? pieceIndex = null,
        [FromForm] int? pieces = null,
        [FromForm] int? priority = null)
    {
        var targetHash = !string.IsNullOrWhiteSpace(hash) ? hash : hashes;
        if (string.IsNullOrWhiteSpace(targetHash) || (!piece.HasValue && !pieceIndex.HasValue && !pieces.HasValue) || !priority.HasValue)
        {
            return this.BadRequest();
        }

        var idx = piece ?? pieceIndex ?? pieces.Value;
        var prio = priority.Value;

        foreach (var torrent in this.ResolveTorrents(targetHash))
        {
            var task = this.downloadEngine?.GetTask(torrent.Id);
            if (task?.Picker != null && idx >= 0)
            {
                task.Picker.SetPiecePriority(idx, prio);
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    private static readonly ConcurrentDictionary<string, QBitSessionSyncState> sessionSyncStates = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime lastSyncCleanupTime = DateTime.UtcNow;

    public static void ResetSyncState()
    {
        sessionSyncStates.Clear();
    }

    private static void CleanupExpiredSessionSyncStates()
    {
        var now = DateTime.UtcNow;
        if (now - lastSyncCleanupTime < TimeSpan.FromMinutes(10))
        {
            return;
        }

        lastSyncCleanupTime = now;
        var expirationThreshold = now.AddHours(-1);

        foreach (var kvp in sessionSyncStates)
        {
            if (kvp.Value.LastAccessed < expirationThreshold)
            {
                sessionSyncStates.TryRemove(kvp.Key, out _);
            }
        }
    }

    internal string GetClientSessionKey()
    {
        try
        {
            if (this.HttpContext != null && this.Request != null)
            {
                var clientId = this.Request.Headers?["X-Client-Id"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    clientId = this.Request.Headers?["X-Arr-Instance"].FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(clientId) && this.Request.Query != null && this.Request.Query.TryGetValue("client_id", out var qClientId))
                {
                    clientId = qClientId.FirstOrDefault();
                }

                string clientTag = null;
                if (!string.IsNullOrWhiteSpace(clientId))
                {
                    clientTag = clientId.Trim();
                }
                else
                {
                    var userAgent = this.Request.Headers?["User-Agent"].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(userAgent))
                    {
                        var trimmedUa = userAgent.Trim();
                        var slashIndex = trimmedUa.IndexOf('/');
                        var tag = (slashIndex >= 0 ? trimmedUa[..slashIndex] : trimmedUa).Trim();
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            clientTag = tag;
                        }
                    }
                }

                if (this.Request.Cookies != null && this.Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
                {
                    return !string.IsNullOrWhiteSpace(clientTag) ? $"sid:{sid}:{clientTag}" : $"sid:{sid}";
                }

                var apiKey = this.Request.Headers?["X-Api-Key"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(apiKey) && this.Request.Query != null && this.Request.Query.TryGetValue("apikey", out var qKey))
                {
                    apiKey = qKey.FirstOrDefault();
                }

                var ip = this.HttpContext.Connection?.RemoteIpAddress?.ToString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    var resolvedIp = !string.IsNullOrWhiteSpace(ip) ? ip : "unknown";
                    return !string.IsNullOrWhiteSpace(clientTag)
                        ? $"key:{apiKey}:{resolvedIp}:{clientTag}"
                        : $"key:{apiKey}:{resolvedIp}";
                }

                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return !string.IsNullOrWhiteSpace(clientTag)
                        ? $"ip:{ip}:{clientTag}"
                        : $"ip:{ip}";
                }

                if (!string.IsNullOrWhiteSpace(clientTag))
                {
                    return $"client:{clientTag}";
                }

                var connId = this.HttpContext.Connection?.Id;
                if (!string.IsNullOrWhiteSpace(connId))
                {
                    return $"conn:{connId}";
                }
            }
        }
        catch
        {
            // Fallback for mock/test contexts where Request is uninitialized
        }

        return "default_session";
    }

    [HttpGet("sync/maindata")]
    public ActionResult<Dictionary<string, object>> GetMainData([FromQuery] int rid = 0)
    {
        CleanupExpiredSessionSyncStates();

        var sessionKey = this.GetClientSessionKey();
        var sessionState = sessionSyncStates.GetOrAdd(sessionKey, _ => new QBitSessionSyncState());

        lock (sessionState.Lock)
        {
            sessionState.LastAccessed = DateTime.UtcNow;

            var torrents = this.torrentService.GetAll().ToList();
            var categories = this.categoryService.GetAll().ToDictionary(
                c => c.Name,
                c => (object)new { name = c.Name, savePath = c.SavePath ?? string.Empty });

            var dlLimit = (this.configService.AlternativeSpeedEnabled ? this.configService.AltDownloadSpeedKbps : this.configService.MaxDownloadSpeedKbps) * 1024;
            var upLimit = (this.configService.AlternativeSpeedEnabled ? this.configService.AltUploadSpeedKbps : this.configService.MaxUploadSpeedKbps) * 1024;
            var totalDl = torrents.Sum(t => t.Downloaded);
            var totalUl = torrents.Sum(t => t.Uploaded);
            var globalRatio = totalDl > 0 ? (double)totalUl / totalDl : 0.0;

            var serverState = new
            {
                dl_info_speed = torrents.Sum(t => t.DownloadSpeed),
                up_info_speed = torrents.Sum(t => t.UploadSpeed),
                dl_info_data = totalDl,
                up_info_data = totalUl,
                alltime_dl = totalDl,
                alltime_ul = totalUl,
                dl_rate_limit = dlLimit,
                up_rate_limit = upLimit,
                use_alt_speed_limits = this.configService.AlternativeSpeedEnabled,
                use_alt_dl_limit = this.configService.AlternativeSpeedEnabled,
                use_alt_up_limit = this.configService.AlternativeSpeedEnabled,
                alt_dl_limit = this.configService.AltDownloadSpeedKbps * 1024,
                alt_up_limit = this.configService.AltUploadSpeedKbps * 1024,
                connection_status = "connected",
                dht_nodes = this.downloadEngine?.DhtNodeCount ?? 0,
                free_space_on_disk = this.GetDriveFreeSpace(this.configService?.DownloadDir),
                global_ratio = Math.Round(globalRatio, 2),
                refresh_interval = 2000,
            };

            var currentHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var torrentList = torrents.ToList();
            var filesByTorrent = this.torrentFileService?.GetFilesForTorrents(torrentList.Select(t => t.Id)) ?? new Dictionary<int, List<TorrentFile>>();

            // If rid == 0, or cached session is not initialized, or rid is out of sequence, perform a full update
            if (rid <= 0 || !sessionState.Initialized || rid > sessionState.CurrentRid)
            {
                sessionState.Initialized = true;
                sessionState.CurrentRid = rid <= 0 ? 1 : rid + 1;
                sessionState.CachedTorrents.Clear();
                sessionState.RemovedTorrents.Clear();

                var torrentDict = new Dictionary<string, object>();
                foreach (var t in torrentList)
                {
                    currentHashes.Add(t.InfoHash);
                    var (resolvedSavePath, resolvedContentPath) = this.ResolvePaths(t, filesByTorrent);
                    var snapshot = QBitTorrentSnapshot.FromTorrent(t, resolvedSavePath, resolvedContentPath);

                    sessionState.CachedTorrents[t.InfoHash] = (snapshot, sessionState.CurrentRid);

                    torrentDict[t.InfoHash] = new
                    {
                        name = snapshot.Name,
                        size = snapshot.Size,
                        total_size = snapshot.Size,
                        progress = snapshot.Progress,
                        dlspeed = snapshot.DlSpeed,
                        upspeed = snapshot.UpSpeed,
                        state = snapshot.State,
                        category = snapshot.Category,
                        tags = snapshot.Tags,
                        save_path = snapshot.SavePath,
                        content_path = snapshot.ContentPath,
                        eta = snapshot.Eta,
                        ratio = snapshot.Ratio,
                        num_seeds = snapshot.NumSeeds,
                        num_complete = snapshot.NumSeeds,
                        num_leechs = snapshot.NumLeechs,
                        num_incomplete = snapshot.NumLeechs,
                        downloaded = snapshot.Downloaded,
                        uploaded = snapshot.Uploaded,
                        amount_left = snapshot.AmountLeft,
                        added_on = snapshot.AddedOn,
                        completion_on = snapshot.CompletionOn,
                        seq_dl = snapshot.SeqDl,
                        f_l_piece_prio = snapshot.FLPiecePrio,
                    };
                }

                var fullResult = new Dictionary<string, object>
                {
                    ["rid"] = sessionState.CurrentRid,
                    ["full_update"] = true,
                    ["torrents"] = torrentDict,
                    ["categories"] = categories,
                    ["server_state"] = serverState,
                };

                return this.Ok(fullResult);
            }

            // Incremental delta sync
            var nextRid = sessionState.CurrentRid + 1;
            var updatedTorrents = new Dictionary<string, object>();

            foreach (var t in torrentList)
            {
                currentHashes.Add(t.InfoHash);
                var (resolvedSavePath, resolvedContentPath) = this.ResolvePaths(t, filesByTorrent);
                var snapshot = QBitTorrentSnapshot.FromTorrent(t, resolvedSavePath, resolvedContentPath);

                var isNewOrChanged = !sessionState.CachedTorrents.TryGetValue(t.InfoHash, out var existing) || existing.Snapshot != snapshot;
                if (isNewOrChanged)
                {
                    sessionState.CachedTorrents[t.InfoHash] = (snapshot, nextRid);
                }

                if (isNewOrChanged || existing.ChangedAtRid > rid)
                {
                    updatedTorrents[t.InfoHash] = new
                    {
                        name = snapshot.Name,
                        size = snapshot.Size,
                        total_size = snapshot.Size,
                        progress = snapshot.Progress,
                        dlspeed = snapshot.DlSpeed,
                        upspeed = snapshot.UpSpeed,
                        state = snapshot.State,
                        category = snapshot.Category,
                        tags = snapshot.Tags,
                        save_path = snapshot.SavePath,
                        content_path = snapshot.ContentPath,
                        eta = snapshot.Eta,
                        ratio = snapshot.Ratio,
                        num_seeds = snapshot.NumSeeds,
                        num_complete = snapshot.NumSeeds,
                        num_leechs = snapshot.NumLeechs,
                        num_incomplete = snapshot.NumLeechs,
                        downloaded = snapshot.Downloaded,
                        uploaded = snapshot.Uploaded,
                        amount_left = snapshot.AmountLeft,
                        added_on = snapshot.AddedOn,
                        completion_on = snapshot.CompletionOn,
                        seq_dl = snapshot.SeqDl,
                        f_l_piece_prio = snapshot.FLPiecePrio,
                    };
                }
            }

            // Detect removed torrents
            var removedNow = sessionState.CachedTorrents.Keys.Where(h => !currentHashes.Contains(h)).ToList();
            foreach (var hash in removedNow)
            {
                sessionState.CachedTorrents.Remove(hash);
                sessionState.RemovedTorrents.Add((hash, nextRid));
            }

            if (sessionState.RemovedTorrents.Count > 500)
            {
                sessionState.RemovedTorrents.RemoveRange(0, sessionState.RemovedTorrents.Count - 500);
            }

            var torrentsRemoved = sessionState.RemovedTorrents
                .Where(r => r.RemovedAtRid > rid)
                .Select(r => r.Hash)
                .ToList();

            sessionState.CurrentRid = nextRid;

            var deltaResult = new Dictionary<string, object>
            {
                ["rid"] = sessionState.CurrentRid,
                ["full_update"] = false,
                ["torrents"] = updatedTorrents,
                ["torrents_removed"] = torrentsRemoved,
                ["categories"] = categories,
                ["server_state"] = serverState,
            };

            return this.Ok(deltaResult);
        }
    }

    [HttpGet("sync/torrentPeers")]
    public ActionResult GetTorrentPeers([FromQuery] string hash, [FromQuery] int rid = 0)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return this.NotFound();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        CleanupExpiredSessionSyncStates();

        var sessionKey = this.GetClientSessionKey();
        var sessionState = sessionSyncStates.GetOrAdd(sessionKey, _ => new QBitSessionSyncState());

        lock (sessionState.Lock)
        {
            sessionState.LastAccessed = DateTime.UtcNow;

            var task = this.downloadEngine?.GetTask(torrent.Id);
            var peers = task?.GetPeers() ?? Array.Empty<PeerInfo>();

            if (!sessionState.PeerSyncStates.TryGetValue(torrent.InfoHash, out var peerSyncState))
            {
                peerSyncState = new QBitTorrentPeersSyncState();
                sessionState.PeerSyncStates[torrent.InfoHash] = peerSyncState;
            }

            if (rid <= 0)
            {
                peerSyncState.Initialized = true;
                peerSyncState.CurrentRid = 1;
                peerSyncState.CachedPeers.Clear();
                peerSyncState.RemovedPeers.Clear();

                var peerDict = new Dictionary<string, object>();
                foreach (var p in peers)
                {
                    var key = $"{p.Ip}:{p.Port}";
                    var snapshot = QBitPeerSnapshot.FromPeerInfo(p);
                    peerSyncState.CachedPeers[key] = (snapshot, peerSyncState.CurrentRid);

                    peerDict[key] = new
                    {
                        client = snapshot.Client,
                        ip = snapshot.Ip,
                        port = snapshot.Port,
                        connection = snapshot.Connection,
                        flags = snapshot.Flags,
                        flags_desc = snapshot.FlagsDesc,
                        progress = snapshot.Progress,
                        dl_speed = snapshot.DlSpeed,
                        up_speed = snapshot.UpSpeed,
                        downloaded = snapshot.Downloaded,
                        uploaded = snapshot.Uploaded,
                        relevance = snapshot.Relevance,
                        files = snapshot.Files,
                    };
                }

                return this.Ok(new
                {
                    full_update = true,
                    peers = peerDict,
                    rid = peerSyncState.CurrentRid,
                    show_flags = true,
                });
            }

            // Incremental delta sync (rid > 0)
            var nextRid = rid + 1;
            var updatedPeers = new Dictionary<string, object>();
            var currentPeerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in peers)
            {
                var key = $"{p.Ip}:{p.Port}";
                currentPeerKeys.Add(key);
                var snapshot = QBitPeerSnapshot.FromPeerInfo(p);

                var isNewOrChanged = !peerSyncState.CachedPeers.TryGetValue(key, out var existing) || existing.Snapshot != snapshot;
                if (isNewOrChanged)
                {
                    peerSyncState.CachedPeers[key] = (snapshot, nextRid);
                }

                if (isNewOrChanged || existing.ChangedAtRid > rid)
                {
                    updatedPeers[key] = new
                    {
                        client = snapshot.Client,
                        ip = snapshot.Ip,
                        port = snapshot.Port,
                        connection = snapshot.Connection,
                        flags = snapshot.Flags,
                        flags_desc = snapshot.FlagsDesc,
                        progress = snapshot.Progress,
                        dl_speed = snapshot.DlSpeed,
                        up_speed = snapshot.UpSpeed,
                        downloaded = snapshot.Downloaded,
                        uploaded = snapshot.Uploaded,
                        relevance = snapshot.Relevance,
                        files = snapshot.Files,
                    };
                }
            }

            // Detect removed peers
            var removedNow = peerSyncState.CachedPeers.Keys.Where(k => !currentPeerKeys.Contains(k)).ToList();
            foreach (var key in removedNow)
            {
                peerSyncState.CachedPeers.Remove(key);
                peerSyncState.RemovedPeers.Add((key, nextRid));
            }

            if (peerSyncState.RemovedPeers.Count > 500)
            {
                peerSyncState.RemovedPeers.RemoveRange(0, peerSyncState.RemovedPeers.Count - 500);
            }

            var peersRemoved = peerSyncState.RemovedPeers
                .Where(r => r.RemovedAtRid > rid)
                .Select(r => r.PeerKey)
                .ToArray();

            peerSyncState.CurrentRid = nextRid;

            return this.Ok(new
            {
                full_update = false,
                peers = updatedPeers,
                peers_removed = peersRemoved,
                rid = nextRid,
                show_flags = true,
            });
        }
    }

    [HttpGet("torrents/pieceStates")]
    public ActionResult<List<int>> GetPieceStates([FromQuery] string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return this.NotFound();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var task = this.downloadEngine?.GetTask(torrent.Id);
        var bitfield = task?.PieceBitfield;

        var downloadingPieces = new HashSet<int>();
        if (task?.PartialPieces != null)
        {
            foreach (var p in task.PartialPieces)
            {
                downloadingPieces.Add(p);
            }
        }

        if (task?.Picker?.PartialPieces != null)
        {
            foreach (var p in task.Picker.PartialPieces)
            {
                downloadingPieces.Add(p);
            }
        }

        try
        {
            var managerProp = task?.GetType().GetProperty("Manager");
            if (managerProp != null)
            {
                var mgr = managerProp.GetValue(task);
                if (mgr != null)
                {
                    var activePiecesProp = mgr.GetType().GetProperty("PartialPieces") ?? mgr.GetType().GetProperty("ActivePieces");
                    if (activePiecesProp?.GetValue(mgr) is IEnumerable<int> mgrPieces)
                    {
                        foreach (var p in mgrPieces)
                        {
                            downloadingPieces.Add(p);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        if (bitfield == null || bitfield.Length == 0)
        {
            var pieceCount = torrent.PieceCount > 0 ? torrent.PieceCount : 0;
            if (pieceCount == 0)
            {
                return this.Ok(new List<int>());
            }

            if (torrent.Status == TorrentStatus.Completed || torrent.Status == TorrentStatus.Seeding)
            {
                return this.Ok(Enumerable.Repeat(2, pieceCount).ToList());
            }

            var uncompletedStates = new List<int>(pieceCount);
            for (var i = 0; i < pieceCount; i++)
            {
                uncompletedStates.Add(downloadingPieces.Contains(i) ? 1 : 0);
            }

            return this.Ok(uncompletedStates);
        }

        var states = new List<int>(bitfield.Length);
        for (var i = 0; i < bitfield.Length; i++)
        {
            if (bitfield[i])
            {
                states.Add(2);
            }
            else if (downloadingPieces.Contains(i))
            {
                states.Add(1);
            }
            else
            {
                states.Add(0);
            }
        }

        return this.Ok(states);
    }

    [HttpGet("torrents/pieceHashes")]
    public ActionResult<List<string>> GetPieceHashes([FromQuery] string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return this.NotFound();
        }

        var torrent = this.torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var result = new List<string>();

        try
        {
            var appData = !string.IsNullOrWhiteSpace(this.appFolderInfo?.AppDataFolder)
                ? this.appFolderInfo.AppDataFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var filePath = Path.Combine(appData, "Torrents", $"{torrent.InfoHash.ToLowerInvariant()}.torrent");
            if (global::System.IO.File.Exists(filePath))
            {
                var bytes = global::System.IO.File.ReadAllBytes(filePath);
                var parsed = this.torrentFileParser.Parse(bytes);
                if (parsed.PieceHashes != null && parsed.PieceHashes.Length >= 20)
                {
                    for (int i = 0; i + 20 <= parsed.PieceHashes.Length; i += 20)
                    {
                        var hex = Convert.ToHexString(parsed.PieceHashes, i, 20).ToLowerInvariant();
                        result.Add(hex);
                    }

                    return this.Ok(result);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to read piece hashes from torrent file for {0}", hash);
        }

        var task = this.downloadEngine?.GetTask(torrent.Id);
        var pieceCount = torrent.PieceCount > 0 ? torrent.PieceCount : (task?.PieceBitfield?.Length ?? 0);
        for (int i = 0; i < pieceCount; i++)
        {
            result.Add(new string('0', 40));
        }

        return this.Ok(result);
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

    [HttpGet("transfer/speedLimitsMode")]
    public ActionResult<int> GetSpeedLimitsMode()
    {
        return this.Ok(this.configService.AlternativeSpeedEnabled ? 1 : 0);
    }

    [HttpPost("transfer/toggleSpeedLimitsMode")]
    public async Task<ActionResult> ToggleSpeedLimitsMode()
    {
        var newState = !this.configService.AlternativeSpeedEnabled;
        this.configService.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["AlternativeSpeedEnabled"] = newState,
        });

        if (this.speedSchedulerService != null)
        {
            await this.speedSchedulerService.ApplyCurrentLimitsAsync();
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setSpeedLimitsMode")]
    public async Task<ActionResult> SetSpeedLimitsMode([FromForm] int mode)
    {
        var enabled = mode == 1;
        this.configService.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["AlternativeSpeedEnabled"] = enabled,
        });

        if (this.speedSchedulerService != null)
        {
            await this.speedSchedulerService.ApplyCurrentLimitsAsync();
        }

        return this.Content("Ok.", "text/plain");
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
            var torrents = this.ResolveTorrents(hashes);
            for (var i = torrents.Count - 1; i >= 0; i--)
            {
                await this.torrentService.MoveQueueAsync(torrents[i].Id, "top");
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/bottomPrio")]
    public async Task<ActionResult> BottomPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var torrents = this.ResolveTorrents(hashes);
            foreach (var t in torrents)
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
            var torrents = this.ResolveTorrents(hashes).OrderBy(t => t.QueuePosition).ToList();
            foreach (var t in torrents)
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
            var torrents = this.ResolveTorrents(hashes).OrderByDescending(t => t.QueuePosition).ToList();
            foreach (var t in torrents)
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
                var internalPrio = FromQBittorrentPriority(priority);
                foreach (var idStr in idStrings)
                {
                    if (int.TryParse(idStr, out var fileIndex) && fileIndex >= 0 && fileIndex < files.Count)
                    {
                        await this.torrentFileService.SetPriorityAsync(files[fileIndex].Id, internalPrio);
                    }
                }
            }
        }

        return this.Content("Ok.", "text/plain");
    }

    private static int ToQBittorrentPriority(int priority)
    {
        return priority switch
        {
            <= 0 => 0,
            1 or 2 => 1,
            3 => 1,
            4 => 6,
            >= 5 => 7,
        };
    }

    private static int FromQBittorrentPriority(int priority)
    {
        return priority switch
        {
            0 => 0,
            1 => 3,
            6 => 4,
            7 => 5,
            _ => 3,
        };
    }

    [HttpGet("search/start")]
    [HttpPost("search/start")]
    public ActionResult StartSearch(
        [FromQuery] string pattern = null,
        [FromQuery] string plugins = null,
        [FromQuery] string category = null,
        [FromForm(Name = "pattern")] string formPattern = null,
        [FromForm(Name = "plugins")] string formPlugins = null,
        [FromForm(Name = "category")] string formCategory = null)
    {
        var finalPattern = !string.IsNullOrWhiteSpace(formPattern) ? formPattern : pattern;
        var finalPlugins = !string.IsNullOrWhiteSpace(formPlugins) ? formPlugins : plugins;
        var finalCategory = !string.IsNullOrWhiteSpace(formCategory) ? formCategory : category;

        if (string.IsNullOrWhiteSpace(finalPattern))
        {
            return this.BadRequest("Pattern is required.");
        }

        var id = this.qbittorrentSearchService.StartSearch(finalPattern, finalPlugins, finalCategory);
        return this.Ok(new { id });
    }

    [HttpPost("search/stop")]
    public ActionResult StopSearch([FromQuery] int? id = null, [FromForm(Name = "id")] int? formId = null)
    {
        var targetId = formId ?? id;
        if (targetId.HasValue)
        {
            this.qbittorrentSearchService.StopSearch(targetId.Value);
        }

        return this.Content("Ok.", "text/plain");
    }

    [HttpGet("search/status")]
    [HttpPost("search/status")]
    public ActionResult GetSearchStatus([FromQuery] int? id = null, [FromForm(Name = "id")] int? formId = null)
    {
        var targetId = formId ?? id;
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
        [FromQuery] int? id = null,
        [FromQuery] int limit = 0,
        [FromQuery] int offset = 0,
        [FromForm(Name = "id")] int? formId = null,
        [FromForm(Name = "limit")] int? formLimit = null,
        [FromForm(Name = "offset")] int? formOffset = null)
    {
        var targetId = formId ?? id;
        if (!targetId.HasValue)
        {
            return this.NotFound();
        }

        var searchId = targetId.Value;
        var searchLimit = formLimit ?? limit;
        var searchOffset = formOffset ?? offset;

        var results = this.qbittorrentSearchService.GetResults(searchId, searchLimit, searchOffset);
        if (results == null)
        {
            return this.NotFound();
        }

        return this.Ok(results);
    }

    [HttpPost("search/delete")]
    public ActionResult DeleteSearch([FromQuery] int? id = null, [FromForm(Name = "id")] int? formId = null)
    {
        var targetId = formId ?? id;
        if (targetId.HasValue)
        {
            this.qbittorrentSearchService.DeleteSearch(targetId.Value);
        }

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

    private List<Torrent> ResolveTorrents(string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return new List<Torrent>();
        }

        if (string.Equals(hashes.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return this.torrentService.GetAll().OrderBy(t => t.QueuePosition).ToList();
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

    private (string SavePath, string ContentPath) ResolvePaths(Torrent t, IReadOnlyDictionary<int, List<TorrentFile>> filesByTorrentId = null)
    {
        var rawSavePath = t?.SavePath ?? string.Empty;
        var category = t?.Category;

        var normalized = this.storagePathService?.NormalizeCompletedSavePath(rawSavePath, category);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            rawSavePath = normalized;
        }
        else
        {
            var completedDir = this.storagePathService?.GetCompletedDirectory(category);
            if (string.IsNullOrWhiteSpace(completedDir))
            {
                completedDir = this.configService?.DownloadDir;
            }

            if (string.IsNullOrWhiteSpace(completedDir))
            {
                completedDir = "/downloads";
            }

            var trimmedRaw = rawSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var inc = this.configService?.IncompleteDownloadDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(rawSavePath) ||
                string.Equals(trimmedRaw, "/downloads/incomplete", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(inc) && string.Equals(trimmedRaw, inc, StringComparison.OrdinalIgnoreCase)))
            {
                rawSavePath = completedDir;
            }
            else if (trimmedRaw.StartsWith("/downloads/incomplete/", StringComparison.OrdinalIgnoreCase) ||
                     trimmedRaw.StartsWith("/downloads/incomplete\\", StringComparison.OrdinalIgnoreCase))
            {
                var relative = trimmedRaw.Substring("/downloads/incomplete".Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                rawSavePath = !string.IsNullOrWhiteSpace(relative) ? Path.Combine(completedDir, relative) : completedDir;
            }
            else if (!string.IsNullOrWhiteSpace(inc) &&
                     (trimmedRaw.StartsWith(inc + "/", StringComparison.OrdinalIgnoreCase) ||
                      trimmedRaw.StartsWith(inc + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                var relative = trimmedRaw.Substring(inc.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                rawSavePath = !string.IsNullOrWhiteSpace(relative) ? Path.Combine(completedDir, relative) : completedDir;
            }
        }

        if (string.IsNullOrWhiteSpace(rawSavePath))
        {
            return (string.Empty, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(t?.Name))
        {
            return (rawSavePath, rawSavePath);
        }

        var trimmedSave = rawSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (Path.HasExtension(trimmedSave))
        {
            var parent = Path.GetDirectoryName(trimmedSave);
            var savePath = !string.IsNullOrWhiteSpace(parent) ? parent : trimmedSave;
            return (savePath, trimmedSave);
        }

        var dirName = Path.GetFileName(trimmedSave);

        if (string.Equals(dirName, t.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(dirName), t.Name, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(trimmedSave);
            var savePath = !string.IsNullOrWhiteSpace(parent) ? parent : trimmedSave;
            return (savePath, trimmedSave);
        }

        if (this.torrentFileService != null && t.Id > 0)
        {
            try
            {
                List<TorrentFile> files = null;
                if (filesByTorrentId != null)
                {
                    filesByTorrentId.TryGetValue(t.Id, out files);
                }
                else
                {
                    files = this.torrentFileService?.GetFiles(t.Id)?.ToList();
                }

                if (files != null && files.Count == 1 && !string.IsNullOrWhiteSpace(files[0].Path))
                {
                    var singleFilePath = Path.Combine(trimmedSave, files[0].Path);
                    return (trimmedSave, singleFilePath);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to load files for torrent {0} in ResolvePaths", t.Id);
            }
        }

        return (trimmedSave, Path.Combine(trimmedSave, t.Name));
    }

    internal static string MapToQBitState(TorrentStatus status, double progress)
    {
        return status switch
        {
            TorrentStatus.Queued => progress >= 1.0 ? "queuedUP" : "queuedDL",
            TorrentStatus.Checking => progress >= 1.0 ? "checkingUP" : "checkingDL",
            TorrentStatus.QueuedForChecking => progress >= 1.0 ? "checkingUP" : "checkingDL",
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

    internal static long CalculateEta(Torrent t)
    {
        if (t == null)
        {
            return 8640000;
        }

        if (t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Completed)
        {
            return 0;
        }

        if (t.Eta > 0)
        {
            return t.Eta;
        }

        if (t.DownloadSpeed > 0)
        {
            var remaining = Math.Max(0, t.TotalSize - t.Downloaded);
            if (remaining == 0 && t.TotalSize > 0 && t.Progress < 1.0)
            {
                remaining = (long)(t.TotalSize * (1.0 - t.Progress));
            }

            if (remaining > 0)
            {
                return (long)Math.Ceiling((double)remaining / t.DownloadSpeed);
            }

            return 0;
        }

        return 8640000;
    }

    private long GetDriveFreeSpace(string path)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(path) ? (!string.IsNullOrWhiteSpace(this.configService?.DownloadDir) ? this.configService.DownloadDir : "/downloads") : path;
            var fullPath = global::System.IO.Path.GetFullPath(target);
            return this.diskProvider?.GetAvailableSpace(fullPath)
                ?? this.diskProvider?.GetAvailableSpace(target)
                ?? 0L;
        }
        catch
        {
            return 0L;
        }
    }

    [HttpGet("log/main")]
    public ActionResult<List<object>> GetLogMain(
        [FromQuery] bool normal = true,
        [FromQuery] bool info = true,
        [FromQuery] bool warning = true,
        [FromQuery] bool critical = true,
        [FromQuery(Name = "last_known_id")] int last_known_id = -1)
    {
        var target = RingBufferTarget.Instance;
        var entries = target?.GetEntries(2048, LogLevel.Trace) ?? new List<LogEntryRecord>();
        var results = new List<object>();

        foreach (var entry in entries)
        {
            if (entry == null || entry.Id <= last_known_id)
            {
                continue;
            }

            var type = MapLogLevelToQBitType(entry.Level);
            var include = (type == 1 && normal) ||
                          (type == 2 && info) ||
                          (type == 4 && warning) ||
                          (type == 8 && critical);

            if (!include)
            {
                continue;
            }

            results.Add(new
            {
                id = (int)entry.Id,
                message = entry.Message ?? string.Empty,
                timestamp = new DateTimeOffset(entry.Time.ToUniversalTime()).ToUnixTimeSeconds(),
                type = type,
            });
        }

        return this.Ok(results);
    }

    [HttpGet("log/peers")]
    public ActionResult<List<object>> GetLogPeers([FromQuery(Name = "last_known_id")] int last_known_id = -1)
    {
        var records = this.peerConnectionHistoryService?.GetRecords();
        var results = new List<object>();

        if (records != null)
        {
            foreach (var r in records)
            {
                if (r == null || r.Id <= last_known_id)
                {
                    continue;
                }

                var isBlocked = string.Equals(r.EventType, "Blocked", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.EventType, "Rejected", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.EventType, "Banned", StringComparison.OrdinalIgnoreCase);

                if (isBlocked)
                {
                    results.Add(new
                    {
                        id = (int)r.Id,
                        ip = r.RemoteIp ?? string.Empty,
                        timestamp = new DateTimeOffset(r.Timestamp.ToUniversalTime()).ToUnixTimeSeconds(),
                        blocked = true,
                        reason = r.EventType ?? string.Empty,
                    });
                }
            }
        }

        return this.Ok(results);
    }

    private static int MapLogLevelToQBitType(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return 1;
        }

        return level.ToLowerInvariant() switch
        {
            "trace" or "debug" or "normal" => 1,
            "info" or "information" => 2,
            "warn" or "warning" => 4,
            "error" or "fatal" or "critical" => 8,
            _ => 1,
        };
    }
}

public record QBitTorrentSnapshot
{
    public string Name { get; init; } = string.Empty;

    public long Size { get; init; }

    public double Progress { get; init; }

    public long DlSpeed { get; init; }

    public long UpSpeed { get; init; }

    public string State { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    public string SavePath { get; init; } = string.Empty;

    public string ContentPath { get; init; } = string.Empty;

    public long Eta { get; init; }

    public double Ratio { get; init; }

    public int NumSeeds { get; init; }

    public int NumLeechs { get; init; }

    public long Downloaded { get; init; }

    public long Uploaded { get; init; }

    public long AmountLeft { get; init; }

    public long AddedOn { get; init; }

    public long CompletionOn { get; init; }

    public bool SeqDl { get; init; }

    public bool FLPiecePrio { get; init; }

    public static QBitTorrentSnapshot FromTorrent(Torrent torrent, string savePath = "", string contentPath = "")
    {
        ArgumentNullException.ThrowIfNull(torrent);

        var addedOn = new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds();
        var completionOn = torrent.DateCompleted.HasValue ? new DateTimeOffset(torrent.DateCompleted.Value).ToUnixTimeSeconds() : 0L;
        var amountLeft = torrent.Progress >= 1.0
            ? 0L
            : (long)Math.Max(0, (1.0 - torrent.Progress) * torrent.TotalSize);

        return new QBitTorrentSnapshot
        {
            Name = torrent.Name ?? string.Empty,
            Size = torrent.TotalSize,
            Progress = torrent.Progress,
            DlSpeed = torrent.DownloadSpeed,
            UpSpeed = torrent.UploadSpeed,
            State = QBittorrentApiController.MapToQBitState(torrent.Status, torrent.Progress),
            Category = torrent.Category ?? string.Empty,
            Tags = torrent.Label ?? string.Empty,
            SavePath = savePath ?? string.Empty,
            ContentPath = contentPath ?? string.Empty,
            Eta = QBittorrentApiController.CalculateEta(torrent),
            Ratio = torrent.Ratio,
            NumSeeds = torrent.Seeders,
            NumLeechs = torrent.Leechers,
            Downloaded = torrent.Downloaded,
            Uploaded = torrent.Uploaded,
            AmountLeft = amountLeft,
            AddedOn = addedOn,
            CompletionOn = completionOn,
            SeqDl = torrent.SequentialDownload,
            FLPiecePrio = torrent.FirstLastPiecePriority,
        };
    }
}

public class QBitSessionSyncState
{
    public bool Initialized { get; set; }

    public int CurrentRid { get; set; }

    public Dictionary<string, (QBitTorrentSnapshot Snapshot, int ChangedAtRid)> CachedTorrents { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Hash, int RemovedAtRid)> RemovedTorrents { get; } = new();

    public Dictionary<string, QBitTorrentPeersSyncState> PeerSyncStates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object Lock { get; } = new();

    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
}

public record QBitPeerSnapshot
{
    public string Client { get; init; } = string.Empty;

    public string Ip { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Connection { get; init; } = string.Empty;

    public string Flags { get; init; } = string.Empty;

    public string FlagsDesc { get; init; } = string.Empty;

    public double Progress { get; init; }

    public long DlSpeed { get; init; }

    public long UpSpeed { get; init; }

    public long Downloaded { get; init; }

    public long Uploaded { get; init; }

    public double Relevance { get; init; }

    public string Files { get; init; } = string.Empty;

    public static QBitPeerSnapshot FromPeerInfo(PeerInfo p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new QBitPeerSnapshot
        {
            Client = p.Client ?? string.Empty,
            Ip = p.Ip ?? string.Empty,
            Port = p.Port,
            Connection = (p.IsUtp || p.Flags?.Contains("P", StringComparison.OrdinalIgnoreCase) == true) ? "uTP" : "TCP",
            Flags = p.Flags ?? string.Empty,
            FlagsDesc = string.Empty,
            Progress = p.Progress,
            DlSpeed = p.DownloadSpeed,
            UpSpeed = p.UploadSpeed,
            Downloaded = p.Downloaded,
            Uploaded = p.Uploaded,
            Relevance = 1.0,
            Files = string.Empty,
        };
    }
}

public class QBitTorrentPeersSyncState
{
    public bool Initialized { get; set; }

    public int CurrentRid { get; set; }

    public Dictionary<string, (QBitPeerSnapshot Snapshot, int ChangedAtRid)> CachedPeers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string PeerKey, int RemovedAtRid)> RemovedPeers { get; } = new();
}
