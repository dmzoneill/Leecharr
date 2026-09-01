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

namespace Leecharr.Api.V1.Synology;

[AllowAnonymous]
[ApiController]
public class SynologyDownloadStationController : ControllerBase
{
    private const string SynologySid = "leecharr-synology-session-id";
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public SynologyDownloadStationController(
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
    [Route("webapi/auth.cgi")]
    [Route("webapi/auth")]
    public IActionResult Auth([FromQuery] string api, [FromQuery] string method)
    {
        return Ok(new
        {
            success = true,
            data = new
            {
                sid = SynologySid
            }
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("webapi/DownloadStation/info.cgi")]
    public IActionResult Info([FromQuery] string method)
    {
        return Ok(new
        {
            success = true,
            data = new
            {
                version = 3890,
                version_string = "3.8-3890",
                is_manager = true
            }
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("webapi/DownloadStation/statistic.cgi")]
    public IActionResult Statistic([FromQuery] string method)
    {
        var all = _torrentService.GetAll().ToList();
        return Ok(new
        {
            success = true,
            data = new
            {
                speed_download = all.Sum(t => t.DownloadSpeed),
                speed_upload = all.Sum(t => t.UploadSpeed),
                emule_speed_download = 0,
                emule_speed_upload = 0
            }
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("webapi/DownloadStation/task.cgi")]
    [Route("webapi/entry.cgi")]
    public async Task<IActionResult> TaskHandler(
        [FromQuery] string api,
        [FromQuery] string method,
        [FromQuery] string id,
        [FromQuery] string uri,
        [FromQuery] string destination)
    {
        var effectiveMethod = (method ?? Request.Form["method"].ToString() ?? string.Empty).ToLowerInvariant();

        switch (effectiveMethod)
        {
            case "list":
                var all = _torrentService.GetAll().ToList();
                var tasks = all.Select(t => new
                {
                    id = t.InfoHash.ToLowerInvariant(),
                    title = t.Name ?? string.Empty,
                    size = t.TotalSize,
                    status = t.Status switch
                    {
                        TorrentStatus.Downloading => "downloading",
                        TorrentStatus.Seeding => "seeding",
                        TorrentStatus.Paused => "paused",
                        TorrentStatus.Stopped => "finished",
                        TorrentStatus.Error => "error",
                        _ => "waiting"
                    },
                    type = "bt",
                    username = "admin",
                    additional = new
                    {
                        detail = new
                        {
                            destination = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                            uri = string.Empty,
                            create_time = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
                            priority = "auto"
                        },
                        transfer = new
                        {
                            size_downloaded = t.Downloaded,
                            size_uploaded = t.Uploaded,
                            speed_download = t.DownloadSpeed,
                            speed_upload = t.UploadSpeed
                        }
                    }
                }).ToList();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        total = tasks.Count,
                        offset = 0,
                        tasks
                    }
                });

            case "create":
                var targetUri = uri ?? Request.Form["uri"].ToString();
                var targetDest = destination ?? Request.Form["destination"].ToString();

                if (!string.IsNullOrWhiteSpace(targetUri))
                {
                    if (targetUri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        await _torrentService.AddFromMagnetAsync(targetUri, null, targetDest, false);
                    }
                    else
                    {
                        using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                        var bytes = await client.GetByteArrayAsync(targetUri);
                        var parsed = _torrentFileParser.Parse(bytes);
                        await _torrentService.AddFromParsedTorrentAsync(parsed, null, targetDest, false, bytes);
                    }
                }
                else if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                {
                    var file = Request.Form.Files[0];
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var parsed = _torrentFileParser.Parse(bytes);
                    await _torrentService.AddFromParsedTorrentAsync(parsed, null, targetDest, false, bytes);
                }

                return Ok(new { success = true });

            case "delete":
                var idsToDelete = (id ?? Request.Form["id"].ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var taskId in idsToDelete)
                {
                    var t = _torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await _torrentService.DeleteAsync(t.Id, false);
                    }
                }

                return Ok(new { success = true });

            case "pause":
                var idsToPause = (id ?? Request.Form["id"].ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var taskId in idsToPause)
                {
                    var t = _torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await _torrentService.PauseAsync(t.Id);
                    }
                }

                return Ok(new { success = true });

            case "resume":
                var idsToResume = (id ?? Request.Form["id"].ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var taskId in idsToResume)
                {
                    var t = _torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await _torrentService.ResumeAsync(t.Id);
                    }
                }

                return Ok(new { success = true });

            default:
                return Ok(new { success = true });
        }
    }
}
