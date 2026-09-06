// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
using NzbDrone.Core.Authentication;
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
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public FloodApiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        IUserService userService = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.userService = userService;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
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
                    string.Equals(jwtToken, this.configFileProvider.ApiKey, StringComparison.Ordinal))
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
                    string.Equals(tToken, this.configFileProvider.ApiKey, StringComparison.Ordinal))
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
            if (string.Equals(apiKey, this.configFileProvider.ApiKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [HttpPost]
    [Route("api/auth/authenticate")]
    public IActionResult Authenticate([FromBody] FloodAuthRequest request = null)
    {
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var username = request?.Username;
            var password = request?.Password;

            var authenticated = false;
            var masterKey = this.configFileProvider.ApiKey;

            if (!string.IsNullOrWhiteSpace(masterKey) &&
                ((!string.IsNullOrWhiteSpace(password) && string.Equals(password, masterKey, StringComparison.Ordinal)) ||
                 (!string.IsNullOrWhiteSpace(username) && string.Equals(username, masterKey, StringComparison.Ordinal))))
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
    public IActionResult Verify()
    {
        var isAllowed = this.IsFloodAuthenticated();
        return this.Ok(new { isInitialUser = false, isAllowed = isAllowed });
    }

    [HttpGet]
    [Route("api/client/settings")]
    public IActionResult GetClientSettings()
    {
        return this.Ok(new
        {
            directoryDefault = this.configService.DownloadDir ?? "/downloads",
        });
    }

    [HttpGet]
    [Route("api/torrents")]
    public IActionResult GetTorrents()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var dict = new Dictionary<string, object>();

        foreach (var t in torrents)
        {
            var hash = t.InfoHash.ToLowerInvariant();
            dict[hash] = new
            {
                hash = t.InfoHash,
                name = t.Name,
                bytesDone = t.Downloaded,
                sizeBytes = t.TotalSize,
                percentComplete = (t.Progress * 100).ToString("F1"),
                downRate = t.DownloadSpeed,
                upRate = t.UploadSpeed,
                ratio = t.Ratio,
                eta = t.Eta,
                status = new[] { MapToFloodStatus(t) },
                tags = string.IsNullOrWhiteSpace(t.Category)
                    ? (string.IsNullOrWhiteSpace(t.Label) ? Array.Empty<string>() : new[] { t.Label })
                    : new[] { t.Category },
                directory = t.SavePath ?? string.Empty,
                isPrivate = t.IsPrivate,
                isInitialSeeding = t.InitialSeeding,
                isSequential = t.SequentialDownload,
                seedsConnected = t.Seeders,
                seedsTotal = t.Seeders,
                peersConnected = t.Leechers,
                peersTotal = t.Leechers,
            };
        }

        return this.Ok(new { torrents = dict });
    }

    private static string MapToFloodStatus(Torrent t)
    {
        return t.Status switch
        {
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "seeding",
            TorrentStatus.Paused => "stopped",
            TorrentStatus.Stopped => (t.Progress >= 1.0 || t.DateCompleted.HasValue) ? "complete" : "stopped",
            _ => "inactive",
        };
    }

    [HttpPost]
    [Route("api/torrents/add-urls")]
    public async Task<IActionResult> AddUrls([FromBody] FloodAddUrlsRequest request)
    {
        if (request?.Urls != null)
        {
            var category = request.Tags?.FirstOrDefault();
            foreach (var url in request.Urls)
            {
                if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    await this.torrentService.AddFromMagnetAsync(url, category, request.Destination, !request.Start);
                }
                else
                {
                    var bytes = await this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: 10 * 1024 * 1024);
                    var parsed = this.torrentFileParser.Parse(bytes);
                    await this.torrentService.AddFromParsedTorrentAsync(parsed, category, request.Destination, !request.Start, bytes);
                }
            }
        }

        return this.Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/add-files")]
    public async Task<IActionResult> AddFiles([FromBody] FloodAddFilesRequest jsonRequest = null)
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
        else if (jsonRequest?.Files != null && jsonRequest.Files.Count > 0)
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
            priority = f.Priority,
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
                            await this.torrentFileService.SetPriorityAsync(files[idx].Id, request.Priority);
                        }
                    }
                }
            }
        }

        return this.Ok(new { success = true });
    }
}

public class FloodSetPriorityRequest
{
    public List<string> Hashes { get; set; } = new();

    public List<int> Indices { get; set; } = new();

    public int Priority { get; set; } = 1;
}
