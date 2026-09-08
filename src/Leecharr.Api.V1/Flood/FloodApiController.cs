// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Flood;

public class FloodAuthRequest
{
    public string Username { get; set; }

    public string Password { get; set; }
}

public class FloodAddUrlsRequest
{
    public List<string> Urls { get; set; } = new();

    public string Destination { get; set; }

    public List<string> Tags { get; set; } = new();

    public bool Start { get; set; } = true;
}

public class FloodAddFilesRequest
{
    public List<string> Files { get; set; } = new();

    public string Destination { get; set; }

    public List<string> Tags { get; set; } = new();

    public bool Start { get; set; } = true;
}

public class FloodActionRequest
{
    public List<string> Hashes { get; set; } = new();

    public bool DeleteData { get; set; }

    public List<string> Tags { get; set; } = new();
}

[AllowAnonymous]
[ApiController]
public class FloodApiController : ControllerBase, IActionFilter
{
    private static readonly RpcSessionStore authenticatedSessions = new();
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IUserService userService;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly NzbDrone.Core.Network.GeoIp.IGeoIpService geoIpService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public FloodApiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        IUserService userService = null,
        ISafeHttpClientService safeHttpClientService = null,
        NzbDrone.Core.Network.GeoIp.IGeoIpService geoIpService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.userService = userService;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.geoIpService = geoIpService;
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

        if (string.Equals(actionName, nameof(this.Authenticate), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionName, nameof(this.Verify), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!this.IsFloodAuthenticated())
        {
            context.Result = this.StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message = "Unauthorized" });
        }
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private bool IsFloodAuthenticated()
    {
        if (this.configFileProvider == null || !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        if (this.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        if (this.Request?.Cookies != null)
        {
            if (this.Request.Cookies.TryGetValue("flood-auth", out var token) && !string.IsNullOrWhiteSpace(token))
            {
                if (authenticatedSessions.IsValid(token))
                {
                    return true;
                }
            }

            if (this.Request.Cookies.TryGetValue("jwt", out var jwtToken) && !string.IsNullOrWhiteSpace(jwtToken))
            {
                if (authenticatedSessions.IsValid(jwtToken))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey) &&
                    RpcAuthenticationHelper.FixedTimeEquals(jwtToken, this.configFileProvider.ApiKey))
                {
                    return true;
                }
            }

            if (this.Request.Cookies.TryGetValue("token", out var tToken) && !string.IsNullOrWhiteSpace(tToken))
            {
                if (authenticatedSessions.IsValid(tToken))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey) &&
                    RpcAuthenticationHelper.FixedTimeEquals(tToken, this.configFileProvider.ApiKey))
                {
                    return true;
                }
            }
        }

        if (this.Request?.Headers != null)
        {
            var headerToken = this.Request.Headers["X-Flood-Auth"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerToken) && authenticatedSessions.IsValid(headerToken))
            {
                return true;
            }
        }

        var apiKey = this.Request?.Headers?["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey) && this.Request?.Query != null && this.Request.Query.TryGetValue("apikey", out var qKey))
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

    [HttpPost]
    [Route("api/auth/authenticate")]
    [Route("auth/authenticate")]
    public IActionResult Authenticate([FromBody] FloodAuthRequest request = null)
    {
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var username = request?.Username;
            var password = request?.Password;

            var authenticated = false;
            var masterKey = this.configFileProvider.ApiKey;

            if (!string.IsNullOrWhiteSpace(masterKey) &&
                ((!string.IsNullOrWhiteSpace(password) && RpcAuthenticationHelper.FixedTimeEquals(password, masterKey)) ||
                 (!string.IsNullOrWhiteSpace(username) && RpcAuthenticationHelper.FixedTimeEquals(username, masterKey))))
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
                return this.Unauthorized(new { success = false, message = "Invalid username or password" });
            }
        }

        var token = Guid.NewGuid().ToString("N");
        authenticatedSessions.SetSession(token, DateTime.UtcNow.AddDays(7));

        this.Response.Cookies.Append("flood-auth", token, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        this.Response.Cookies.Append("jwt", token, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        this.Response.Cookies.Append("token", token, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [HttpDelete]
    [Route("api/auth/logout")]
    [Route("auth/logout")]
    public IActionResult Logout()
    {
        if (this.Request.Cookies.TryGetValue("flood-auth", out var token) && !string.IsNullOrWhiteSpace(token))
        {
            authenticatedSessions.RemoveSession(token);
        }

        if (this.Request.Cookies.TryGetValue("jwt", out var jwtToken) && !string.IsNullOrWhiteSpace(jwtToken))
        {
            authenticatedSessions.RemoveSession(jwtToken);
        }

        if (this.Request.Cookies.TryGetValue("token", out var tToken) && !string.IsNullOrWhiteSpace(tToken))
        {
            authenticatedSessions.RemoveSession(tToken);
        }

        var headerToken = this.Request.Headers["X-Flood-Auth"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerToken))
        {
            authenticatedSessions.RemoveSession(headerToken);
        }

        this.Response.Cookies.Delete("flood-auth");
        this.Response.Cookies.Delete("jwt");
        this.Response.Cookies.Delete("token");
        return this.Ok(new { success = true });
    }

    [HttpGet]
    [Route("api/auth/verify")]
    [Route("auth/verify")]
    public IActionResult Verify()
    {
        var isAllowed = this.IsFloodAuthenticated();
        return this.Ok(new { isInitialUser = false, isAllowed = isAllowed });
    }

    [HttpGet]
    [Route("api/client/settings")]
    [Route("client/settings")]
    public IActionResult GetClientSettings()
    {
        return this.Ok(new
        {
            directoryDefault = this.configService.DownloadDir ?? "/downloads",
        });
    }

    [HttpGet]
    [Route("api/torrents")]
    [Route("torrents")]
    public IActionResult GetTorrents()
    {
        var dict = this.BuildTorrentDictionary();
        return this.Ok(new { torrents = dict });
    }

    [HttpGet]
    [Route("api/activity-stream")]
    [Route("activity-stream")]
    public async Task ActivityStream(CancellationToken cancellationToken = default)
    {
        this.Response.ContentType = "text/event-stream";
        this.Response.Headers.CacheControl = "no-cache";
        this.Response.Headers.Connection = "keep-alive";

        var dict = this.BuildTorrentDictionary();
        var json = JsonSerializer.Serialize(dict);
        await this.Response.WriteAsync($"event: TORRENT_LIST_DIFF\ndata: {json}\n\n", cancellationToken);
        await this.Response.Body.FlushAsync(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(2000, cancellationToken);
                var updateDict = this.BuildTorrentDictionary();
                var updateJson = JsonSerializer.Serialize(updateDict);
                await this.Response.WriteAsync($"event: TORRENT_LIST_DIFF\ndata: {updateJson}\n\n", cancellationToken);
                await this.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or test completed
        }
    }

    public Dictionary<string, object> BuildTorrentDictionary()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var dict = new Dictionary<string, object>();

        foreach (var t in torrents)
        {
            var hash = t.InfoHash.ToLowerInvariant();
            var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
            var peers = downloadTask?.GetPeers() ?? (IReadOnlyList<PeerInfo>)Array.Empty<PeerInfo>();
            var seedsConnected = peers.Count(p => p.Progress >= 1.0 || (p.Flags != null && p.Flags.Contains("S", StringComparison.OrdinalIgnoreCase)));
            var leechersConnected = peers.Count - seedsConnected;
            dict[hash] = new
            {
                hash = t.InfoHash,
                name = t.Name,
                bytesDone = t.Downloaded,
                sizeBytes = t.TotalSize,
                percentComplete = Math.Round(t.Progress * 100.0, 1),
                downRate = t.DownloadSpeed,
                upRate = t.UploadSpeed,
                ratio = t.Ratio,
                eta = t.Eta > 0 ? t.Eta : (t.Progress >= 1.0 ? 0 : (t.DownloadSpeed > 0 ? (Math.Max(0, t.TotalSize - t.Downloaded) / t.DownloadSpeed) : 8640000)),
                status = new[] { MapToFloodStatus(t) },
                tags = string.IsNullOrWhiteSpace(t.Category)
                    ? (string.IsNullOrWhiteSpace(t.Label) ? Array.Empty<string>() : new[] { t.Label })
                    : new[] { t.Category },
                directory = t.SavePath ?? string.Empty,
                isPrivate = t.IsPrivate,
                isInitialSeeding = t.InitialSeeding,
                isSequential = t.SequentialDownload,
                seedsConnected = peers.Count > 0 ? seedsConnected : t.Seeders,
                seedsTotal = t.Seeders,
                peersConnected = peers.Count > 0 ? leechersConnected : t.Leechers,
                peersTotal = t.Leechers,
            };
        }

        return dict;
    }

    public static string MapToFloodStatus(TorrentStatus status, double progress = 0.0)
    {
        return status switch
        {
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "seeding",
            TorrentStatus.Completed => "complete",
            TorrentStatus.Checking => "checking",
            TorrentStatus.Paused => progress >= 1.0 ? "complete" : "stopped",
            TorrentStatus.Stopped => progress >= 1.0 ? "complete" : "stopped",
            TorrentStatus.Error => "error",
            _ => "inactive",
        };
    }

    public static string MapToFloodStatus(Torrent torrent)
    {
        if (torrent == null)
        {
            return "inactive";
        }

        return MapToFloodStatus(torrent.Status, torrent.Progress);
    }

    [HttpPost]
    [Route("api/torrents/add-urls")]
    public async Task<IActionResult> AddUrls([FromBody] FloodAddUrlsRequest request)
    {
        if (request?.Urls != null)
        {
            var category = request.Tags?.FirstOrDefault();
            var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? (this.configFileProvider?.MaxTorrentFileSizeBytes ?? 250L * 1024 * 1024);
            foreach (var url in request.Urls)
            {
                if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    await this.torrentService.AddFromMagnetAsync(url, category, request.Destination, !request.Start);
                }
                else
                {
                    var bytes = await this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: maxTorrentBytes);
                    var parsed = this.torrentFileParser.Parse(bytes);
                    await this.torrentService.AddFromParsedTorrentAsync(parsed, category, request.Destination, !request.Start, bytes);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/add-files")]
    public async Task<IActionResult> AddFiles()
    {
        if (this.Request.HasFormContentType && this.Request.Form.Files.Count > 0)
        {
            var destination = this.Request.Form["destination"].ToString();
            var tagsStr = this.Request.Form["tags"].ToString();
            var category = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            var start = !string.Equals(this.Request.Form["start"].ToString(), "false", StringComparison.OrdinalIgnoreCase);

            foreach (var file in this.Request.Form.Files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var parsed = this.torrentFileParser.Parse(bytes);
                await this.torrentService.AddFromParsedTorrentAsync(parsed, category, destination, !start, bytes);
            }
        }
        else
        {
            FloodAddFilesRequest jsonRequest = null;
            try
            {
                using var reader = new StreamReader(this.Request.Body);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    jsonRequest = JsonSerializer.Deserialize<FloodAddFilesRequest>(body, options);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to parse FloodAddFilesRequest JSON");
            }

            if (jsonRequest?.Files != null && jsonRequest.Files.Count > 0)
            {
                var category = jsonRequest.Tags?.FirstOrDefault()?.Trim();
                foreach (var b64 in jsonRequest.Files)
                {
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        var bytes = Convert.FromBase64String(b64);
                        var parsed = this.torrentFileParser.Parse(bytes);
                        await this.torrentService.AddFromParsedTorrentAsync(parsed, category, jsonRequest.Destination, !jsonRequest.Start, bytes);
                    }
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/start")]
    public async Task<IActionResult> StartTorrents([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await this.torrentService.ResumeAsync(t.Id);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/stop")]
    public async Task<IActionResult> StopTorrents([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await this.torrentService.PauseAsync(t.Id);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/delete")]
    public async Task<IActionResult> DeleteTorrents([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await this.torrentService.DeleteAsync(t.Id, request.DeleteData);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpGet]
    [Route("api/torrents/tags")]
    public IActionResult GetTags()
    {
        var cats = this.categoryService.GetAll().Select(c => c.Name).ToList();
        return this.Ok(cats);
    }

    [HttpPost]
    [HttpPatch]
    [Route("api/torrents/tags")]
    [Route("api/torrents/set-tags")]
    public async Task<IActionResult> SetTags([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            var newCategory = request.Tags?.FirstOrDefault() ?? string.Empty;
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    t.Category = newCategory;
                    t.Label = string.Join(",", request.Tags ?? new List<string>());
                    await this.torrentService.UpdateAsync(t);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/check-hash")]
    public async Task<IActionResult> CheckHash([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await this.torrentService.ForceRecheckAsync(t.Id);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    public static int ToFloodPriority(int internalPriority) => internalPriority switch
    {
        <= 0 => 0,
        1 or 2 or 3 => 1,
        >= 4 => 2,
    };

    public static int FromFloodPriority(int floodPriority) => floodPriority switch
    {
        0 => 0,
        1 => 3,
        2 => 4,
        _ => 3,
    };

    [HttpGet]
    [Route("api/torrents/{hash}/contents")]
    public IActionResult GetContents([FromRoute] string hash)
    {
        var t = this.torrentService.GetByInfoHash(hash);
        if (t == null)
        {
            return this.NotFound();
        }

        var files = this.torrentFileService.GetFiles(t.Id).ToList();
        var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
        TorrentFileProgressEnricher.Enrich(t, files, downloadTask);
        var result = files.Select((f, idx) => new
        {
            index = idx,
            path = f.Path,
            sizeBytes = f.Size,
            percentComplete = f.Progress * 100.0,
            priority = ToFloodPriority(f.Priority),
        });

        return this.Ok(result);
    }

    [HttpPatch]
    [HttpPost]
    [Route("api/torrents/{hash}/contents")]
    [Route("api/torrents/contents-priority")]
    public async Task<IActionResult> SetContentsPriority([FromRoute] string hash = null, [FromBody] FloodSetPriorityRequest request = null)
    {
        var targetHashes = new List<string>();
        if (!string.IsNullOrWhiteSpace(hash))
        {
            targetHashes.Add(hash);
        }

        if (request?.Hashes != null)
        {
            targetHashes.AddRange(request.Hashes);
        }

        if (request?.Indices != null)
        {
            var internalPrio = FromFloodPriority(request.Priority);
            foreach (var h in targetHashes)
            {
                var t = this.torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    var files = this.torrentFileService.GetFiles(t.Id).ToList();
                    foreach (var idx in request.Indices)
                    {
                        if (idx >= 0 && idx < files.Count)
                        {
                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, internalPrio);
                        }
                    }
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [HttpPatch]
    [Route("api/torrents/set-location")]
    public async Task<IActionResult> SetLocation([FromBody] FloodSetLocationRequest request)
    {
        if (request?.Hashes != null && !string.IsNullOrWhiteSpace(request.Destination))
        {
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await this.torrentService.SetLocationAsync(t.Id, request.Destination, request.MoveFiles);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/reannounce")]
    public async Task<IActionResult> Reannounce([FromBody] FloodReannounceRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = this.torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await this.torrentService.ForceAnnounceAsync(t.Id);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpGet]
    [Route("api/torrents/{hash}/peers")]
    [Route("torrents/{hash}/peers")]
    public IActionResult GetTorrentPeers([FromRoute] string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return this.BadRequest();
        }

        var t = this.torrentService.GetByInfoHash(hash);
        if (t == null)
        {
            return this.NotFound();
        }

        var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
        var swarmPeers = downloadTask?.GetPeers() ?? (IReadOnlyList<PeerInfo>)Array.Empty<PeerInfo>();

        var result = swarmPeers.Select(p => new
        {
            address = p.Ip ?? string.Empty,
            client = p.Client ?? string.Empty,
            country = (!string.IsNullOrWhiteSpace(p.Ip) && this.geoIpService != null ? this.geoIpService.Lookup(p.Ip)?.CountryCode : null) ?? string.Empty,
            downloadRate = p.DownloadSpeed,
            uploadRate = p.UploadSpeed,
            progress = p.Progress * 100.0,
            flags = p.Flags ?? string.Empty,
            isEncrypted = p.IsEncrypted,
            isIncoming = p.IsIncoming,
            isUtp = p.IsUtp,
            peerIsChoked = p.IsChoked,
            peerIsInterested = p.IsInterested,
            clientIsChoked = p.ClientIsChoked,
            clientIsInterested = p.ClientIsInterested,
        });

        return this.Ok(result);
    }
}

public class FloodSetPriorityRequest
{
    public List<string> Hashes { get; set; } = new();

    public List<int> Indices { get; set; } = new();

    public int Priority { get; set; } = 1;
}

public class FloodSetLocationRequest
{
    public List<string> Hashes { get; set; } = new();

    public string Destination { get; set; }

    public bool MoveFiles { get; set; } = true;
}

public class FloodReannounceRequest
{
    public List<string> Hashes { get; set; } = new();
}
