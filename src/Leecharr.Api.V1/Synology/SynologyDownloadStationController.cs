using System;
using System.Collections.Generic;
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
    [Route("webapi/query.cgi")]
    public IActionResult Query([FromQuery] string api, [FromQuery] string method, [FromQuery] string query)
    {
        var apis = new Dictionary<string, object>
        {
            ["SYNO.API.Auth"] = new { maxVersion = 7, minVersion = 1, path = "auth.cgi" },
            ["SYNO.DownloadStation.Info"] = new { maxVersion = 2, minVersion = 1, path = "DownloadStation/info.cgi" },
            ["SYNO.DownloadStation.Statistic"] = new { maxVersion = 1, minVersion = 1, path = "DownloadStation/statistic.cgi" },
            ["SYNO.DownloadStation.Task"] = new { maxVersion = 2, minVersion = 1, path = "DownloadStation/task.cgi" },
            ["SYNO.DownloadStation2.Task"] = new { maxVersion = 2, minVersion = 1, path = "entry.cgi" }
        };

        return Ok(new
        {
            data = apis,
            success = true
        });
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
        [FromQuery] string url,
        [FromQuery] string destination)
    {
        var formMethod = Request.HasFormContentType ? Request.Form["method"].ToString() : string.Empty;
        var effectiveMethod = (!string.IsNullOrWhiteSpace(method) ? method : formMethod).ToLowerInvariant();

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
                        TorrentStatus.Downloading => 2,
                        TorrentStatus.Paused => 3,
                        TorrentStatus.Stopped => 5,
                        TorrentStatus.Seeding => 7,
                        TorrentStatus.Error => 9,
                        _ => 1
                    },
                    status_text = t.Status switch
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
                        task = tasks,
                        tasks
                    }
                });

            case "create":
                var targetUri = !string.IsNullOrWhiteSpace(uri) ? uri : (!string.IsNullOrWhiteSpace(url) ? url : (Request.HasFormContentType ? (Request.Form["uri"].ToString() ?? Request.Form["url"].ToString()) : string.Empty));
                var targetDest = destination ?? (Request.HasFormContentType ? Request.Form["destination"].ToString() : null);

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

            case "getinfo":
            case "getst":
                var formId = Request.HasFormContentType ? Request.Form["id"].ToString() : string.Empty;
                var effectiveId = !string.IsNullOrWhiteSpace(id) ? id : formId;
                var queryIds = effectiveId
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var infoTorrents = _torrentService.GetAll().ToList();
                if (queryIds.Count > 0)
                {
                    infoTorrents = infoTorrents.Where(t => queryIds.Contains(t.InfoHash) || queryIds.Contains(t.Id.ToString())).ToList();
                }

                var infoTasks = infoTorrents.Select(t => new
                {
                    id = t.InfoHash,
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
                        total = infoTasks.Count,
                        offset = 0,
                        task = infoTasks,
                        tasks = infoTasks
                    }
                });

            case "delete":
                var formDeleteId = Request.HasFormContentType ? Request.Form["id"].ToString() : string.Empty;
                var idsToDelete = (!string.IsNullOrWhiteSpace(id) ? id : formDeleteId).Split(',', StringSplitOptions.RemoveEmptyEntries);
                var formForceComplete = Request.HasFormContentType ? Request.Form["force_complete"].ToString() : string.Empty;
                var formDeleteData = Request.HasFormContentType ? Request.Form["delete_data"].ToString() : string.Empty;

                var deleteFiles = string.Equals(Request.Query["force_complete"], "true", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(Request.Query["delete_data"], "true", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(formForceComplete, "true", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(formDeleteData, "true", StringComparison.OrdinalIgnoreCase);

                foreach (var taskId in idsToDelete)
                {
                    var t = _torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await _torrentService.DeleteAsync(t.Id, deleteFiles);
                    }
                }

                return Ok(new { success = true });

            case "pause":
                var formPauseId = Request.HasFormContentType ? Request.Form["id"].ToString() : string.Empty;
                var idsToPause = (!string.IsNullOrWhiteSpace(id) ? id : formPauseId).Split(',', StringSplitOptions.RemoveEmptyEntries);
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
                var formResumeId = Request.HasFormContentType ? Request.Form["id"].ToString() : string.Empty;
                var idsToResume = (!string.IsNullOrWhiteSpace(id) ? id : formResumeId).Split(',', StringSplitOptions.RemoveEmptyEntries);
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
