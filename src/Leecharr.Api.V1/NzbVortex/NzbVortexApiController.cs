// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.NzbVortex;

[ApiController]
public class NzbVortexApiController : ControllerBase, IActionFilter
{
    private static readonly ConcurrentDictionary<string, DateTime> authenticatedSessions = new();
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public NzbVortexApiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var actionName = context.ActionDescriptor.RouteValues["action"];
        if (string.Equals(actionName, nameof(this.GetNonce), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionName, nameof(this.Login), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!this.IsNzbVortexAuthenticated())
        {
            context.Result = this.StatusCode(StatusCodes.Status401Unauthorized, new { error = 401, result = "Unauthorized" });
        }
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private bool IsNzbVortexAuthenticated()
    {
        if (this.configFileProvider != null && !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        var session = this.Request.Query["session"].ToString();
        if (!string.IsNullOrEmpty(session) && authenticatedSessions.TryGetValue(session, out var expiry) && expiry > DateTime.UtcNow)
        {
            return true;
        }

        return false;
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("nzbvortex/api/v1/auth/nonce")]
    [Route("api/v1/auth/nonce")]
    public IActionResult GetNonce()
    {
        return this.Ok(new
        {
            authNonce = "leecharr-vortex-nonce",
            nonce = "leecharr-vortex-nonce",
            error = 0,
            result = 0,
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [HttpPost]
    [NzbVortexLoginConstraint]
    [Route("nzbvortex/api/v1/auth/login")]
    [Route("api/v1/auth/login")]
    public IActionResult Login()
    {
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
            if (!isAuth)
            {
                return this.Ok(new
                {
                    loginResult = 1,
                    auth = false,
                    error = 401,
                    result = 1,
                });
            }
        }

        var sessionToken = Guid.NewGuid().ToString("N");
        authenticatedSessions[sessionToken] = DateTime.UtcNow.AddHours(24);

        return this.Ok(new
        {
            loginResult = 0,
            auth = true,
            session = sessionToken,
            error = 0,
            result = 0,
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/app/appversion")]
    [Route("api/v1/app/appversion")]
    public IActionResult GetAppVersion()
    {
        if (!this.IsNzbVortexAuthenticated())
        {
            return this.Unauthorized();
        }

        return this.Ok(new
        {
            appVersion = "3.4.2",
            error = 0,
            result = 0,
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/app/apilevel")]
    [Route("api/v1/app/apilevel")]
    public IActionResult GetApiLevel()
    {
        return this.Ok(new
        {
            apiLevel = 7,
            error = 0,
            result = 0,
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/group")]
    [Route("api/v1/group")]
    public IActionResult GetGroups()
    {
        var cats = this.categoryService.GetAll().ToList();
        var groups = cats.Select(c => new
        {
            groupName = c.Name,
            destinationPath = c.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
            isDefault = false,
        }).ToList();

        return this.Ok(new
        {
            groups,
            error = 0,
            result = 0,
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/nzb")]
    [Route("api/v1/nzb")]
    [Route("nzbvortex/api/v1/queue")]
    [Route("api/v1/queue")]
    public IActionResult GetNzbs()
    {
        if (!this.IsNzbVortexAuthenticated())
        {
            return this.Unauthorized();
        }

        var all = this.torrentService.GetAll().ToList();
        var nzbs = all.Select(t => new
        {
            id = t.Id,
            uiTitle = t.Name ?? string.Empty,
            nzbFilename = t.Name ?? string.Empty,
            nzbName = t.Name ?? string.Empty,
            groupName = t.Category ?? string.Empty,
            totalDownloadSize = t.TotalSize,
            totalBytes = t.TotalSize,
            downloadedSize = t.Downloaded,
            downloadedBytes = t.Downloaded,
            transferedSpeed = (int)t.DownloadSpeed,
            speed = t.DownloadSpeed,
            isPaused = t.Status == TorrentStatus.Paused,
            state = t.Status switch
            {
                TorrentStatus.Downloading => 1,
                TorrentStatus.Seeding => 20,
                TorrentStatus.Stopped => 20,
                TorrentStatus.Paused => 0,
                TorrentStatus.Error => 21,
                _ => 0,
            },
            statusText = t.Status.ToString(),
            destinationPath = t.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
            downloadRatio = t.Ratio,
            progress = t.Progress,
        }).ToList();

        return this.Ok(new
        {
            nzbs,
            queue = nzbs,
            error = 0,
            result = 0,
        });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/nzb/add")]
    [Route("api/v1/nzb/add")]
    [Route("nzbvortex/api/v1/queue/add")]
    [Route("api/v1/queue/add")]
    public async Task<IActionResult> AddNzb(
        [FromQuery(Name = "groupname")] string queryGroup = null,
        [FromQuery(Name = "name")] string queryName = null,
        [FromForm] string name = null,
        [FromForm] string groupName = null)
    {
        var category = !string.IsNullOrWhiteSpace(queryGroup) ? queryGroup : groupName;
        var addedId = 1;
        if (this.Request.HasFormContentType && this.Request.Form.Files.Count > 0)
        {
            var file = this.Request.Form.Files[0];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = this.torrentFileParser.Parse(bytes);
            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, false, bytes);
            addedId = added?.Id ?? addedId;
        }
        else
        {
            var url = this.Request.Query["url"].ToString();
            if (string.IsNullOrEmpty(url) && this.Request.HasFormContentType)
            {
                url = this.Request.Form["url"].ToString();
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await this.torrentService.AddFromMagnetAsync(url, category, null, false);
                    addedId = added?.Id ?? addedId;
                }
                else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: 10 * 1024 * 1024);
                    var parsed = this.torrentFileParser.Parse(bytes);
                    var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, false, bytes);
                    addedId = added?.Id ?? addedId;
                }
            }
        }

        return this.Ok(new
        {
            id = addedId.ToString(),
            nzb = new { id = addedId.ToString() },
            error = 0,
            result = 0,
        });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/nzb/{id}/pause")]
    [Route("api/v1/nzb/{id}/pause")]
    [Route("nzbvortex/api/v1/queue/{id}/pause")]
    [Route("api/v1/queue/{id}/pause")]
    public async Task<IActionResult> PauseNzb(int id)
    {
        await this.torrentService.PauseAsync(id);
        return this.Ok(new { error = 0, result = 0 });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/nzb/{id}/resume")]
    [Route("api/v1/nzb/{id}/resume")]
    [Route("nzbvortex/api/v1/queue/{id}/resume")]
    [Route("api/v1/queue/{id}/resume")]
    public async Task<IActionResult> ResumeNzb(int id)
    {
        await this.torrentService.ResumeAsync(id);
        return this.Ok(new { error = 0, result = 0 });
    }

    [HttpGet]
    [HttpPost]
    [HttpDelete]
    [Route("nzbvortex/api/v1/nzb/{id}/cancel")]
    [Route("api/v1/nzb/{id}/cancel")]
    [Route("nzbvortex/api/v1/nzb/{id}/cancelDelete")]
    [Route("api/v1/nzb/{id}/cancelDelete")]
    [Route("nzbvortex/api/v1/nzb/{id}/delete")]
    [Route("api/v1/nzb/{id}/delete")]
    [Route("nzbvortex/api/v1/queue/{id}")]
    [Route("api/v1/queue/{id}")]
    public async Task<IActionResult> CancelNzb(int id, [FromQuery] bool deleteFiles = false)
    {
        var isDelete = deleteFiles || (this.Request.Path.Value?.Contains("cancelDelete", StringComparison.OrdinalIgnoreCase) == true);
        await this.torrentService.DeleteAsync(id, isDelete);
        return this.Ok(new { error = 0, result = 0 });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/file/{id}")]
    [Route("api/v1/file/{id}")]
    public IActionResult GetFiles(int id)
    {
        var torrent = this.torrentService.Get(id);
        var files = this.torrentFileService.GetFiles(id).ToList();
        if (torrent != null)
        {
            var downloadTask = this.torrentService.GetDownloadTask(id);
            TorrentFileProgressEnricher.Enrich(torrent, files, downloadTask);
        }

        var result = files.Select(f => new
        {
            id = f.Id,
            fileName = f.Path,
            fileSize = f.Size,
            downloaded = f.BytesCompleted,
            isIgnored = f.Priority == 0,
        }).ToList();

        return this.Ok(new
        {
            files = result,
            result = 0,
        });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/file/{fileId}/ignore")]
    [Route("api/v1/file/{fileId}/ignore")]
    public async Task<IActionResult> IgnoreFile(int fileId)
    {
        await this.torrentFileService.SetPriorityAsync(fileId, 0);
        return this.Ok(new { error = 0, result = 0 });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/file/{fileId}/unignore")]
    [Route("api/v1/file/{fileId}/unignore")]
    public async Task<IActionResult> UnignoreFile(int fileId)
    {
        await this.torrentFileService.SetPriorityAsync(fileId, 1);
        return this.Ok(new { error = 0, result = 0 });
    }
}
