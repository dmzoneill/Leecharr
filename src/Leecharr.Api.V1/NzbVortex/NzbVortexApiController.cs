using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.NzbVortex;

[AllowAnonymous]
[ApiController]
public class NzbVortexApiController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NzbVortexApiController(
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

    [HttpGet]
    [Route("nzbvortex/api/v1/auth/nonce")]
    [Route("api/v1/auth/nonce")]
    public IActionResult GetNonce()
    {
        return Ok(new
        {
            authNonce = "leecharr-vortex-nonce",
            nonce = "leecharr-vortex-nonce",
            error = 0,
            result = 0
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("nzbvortex/api/v1/auth/login")]
    [Route("api/v1/auth/login")]
    public IActionResult Login()
    {
        return Ok(new
        {
            loginResult = 0,
            auth = true,
            session = "leecharr-session-token",
            error = 0,
            result = 0
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/app/appversion")]
    [Route("api/v1/app/appversion")]
    public IActionResult GetAppVersion()
    {
        return Ok(new
        {
            appVersion = "3.4.2",
            error = 0,
            result = 0
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/app/apilevel")]
    [Route("api/v1/app/apilevel")]
    public IActionResult GetApiLevel()
    {
        return Ok(new
        {
            apiLevel = 7,
            error = 0,
            result = 0
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/group")]
    [Route("api/v1/group")]
    public IActionResult GetGroups()
    {
        var cats = _categoryService.GetAll().ToList();
        var groups = cats.Select(c => new
        {
            groupName = c.Name,
            destinationPath = c.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
            isDefault = false
        }).ToList();

        return Ok(new
        {
            groups,
            error = 0,
            result = 0
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/nzb")]
    [Route("api/v1/nzb")]
    [Route("nzbvortex/api/v1/queue")]
    [Route("api/v1/queue")]
    public IActionResult GetNzbs()
    {
        var all = _torrentService.GetAll().ToList();
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
                _ => 0
            },
            statusText = t.Status.ToString(),
            destinationPath = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
            downloadRatio = t.Ratio,
            progress = t.Progress
        }).ToList();

        return Ok(new
        {
            nzbs,
            queue = nzbs,
            error = 0,
            result = 0
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
        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            var file = Request.Form.Files[0];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = _torrentFileParser.Parse(bytes);
            var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, null, false, bytes);
            addedId = added?.Id ?? addedId;
        }
        else
        {
            var url = Request.Query["url"].ToString();
            if (string.IsNullOrEmpty(url) && Request.HasFormContentType)
            {
                url = Request.Form["url"].ToString();
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                var added = await _torrentService.AddFromMagnetAsync(url, category, null, false);
                addedId = added?.Id ?? addedId;
            }
        }

        return Ok(new
        {
            id = addedId.ToString(),
            nzb = new { id = addedId.ToString() },
            error = 0,
            result = 0
        });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/nzb/{id}/pause")]
    [Route("api/v1/nzb/{id}/pause")]
    [Route("nzbvortex/api/v1/queue/{id}/pause")]
    [Route("api/v1/queue/{id}/pause")]
    public async Task<IActionResult> PauseNzb(int id)
    {
        await _torrentService.PauseAsync(id);
        return Ok(new { error = 0, result = 0 });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/nzb/{id}/resume")]
    [Route("api/v1/nzb/{id}/resume")]
    [Route("nzbvortex/api/v1/queue/{id}/resume")]
    [Route("api/v1/queue/{id}/resume")]
    public async Task<IActionResult> ResumeNzb(int id)
    {
        await _torrentService.ResumeAsync(id);
        return Ok(new { error = 0, result = 0 });
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
        var isDelete = deleteFiles || (Request.Path.Value?.Contains("cancelDelete", StringComparison.OrdinalIgnoreCase) == true);
        await _torrentService.DeleteAsync(id, isDelete);
        return Ok(new { error = 0, result = 0 });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/file/{id}")]
    [Route("api/v1/file/{id}")]
    public IActionResult GetFiles(int id)
    {
        var files = _torrentFileService.GetFiles(id).ToList();
        var result = files.Select(f => new
        {
            id = f.Id,
            fileName = f.Path,
            fileSize = f.Size,
            downloaded = (long)(f.Size * f.Progress),
            isIgnored = f.Priority == 0
        }).ToList();

        return Ok(new
        {
            files = result,
            result = 0
        });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/file/{fileId}/ignore")]
    [Route("api/v1/file/{fileId}/ignore")]
    public async Task<IActionResult> IgnoreFile(int fileId)
    {
        await _torrentFileService.SetPriorityAsync(fileId, 0);
        return Ok(new { error = 0, result = 0 });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/file/{fileId}/unignore")]
    [Route("api/v1/file/{fileId}/unignore")]
    public async Task<IActionResult> UnignoreFile(int fileId)
    {
        await _torrentFileService.SetPriorityAsync(fileId, 1);
        return Ok(new { error = 0, result = 0 });
    }
}
