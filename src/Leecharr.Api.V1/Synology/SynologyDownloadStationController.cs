// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Synology;

[ApiController]
public class SynologyDownloadStationController : ControllerBase
{
    private const string SynologySid = "leecharr-synology-session-id";
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public SynologyDownloadStationController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    private bool IsSynologyAuthenticated()
    {
        if (this.configFileProvider != null && !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        var sid = this.Request.Query["_sid"].ToString();
        if (string.IsNullOrEmpty(sid))
        {
            sid = this.Request.Cookies["id"];
        }

        if (!string.IsNullOrEmpty(sid) && string.Equals(sid, SynologySid, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    [AllowAnonymous]
    [HttpGet]
    [HttpPost]
    [Route("webapi/query.cgi")]
    public IActionResult Query([FromQuery] string api, [FromQuery] string method, [FromQuery] string query)
    {
        var apis = new Dictionary<string, object>
        {
            ["SYNO.API.Auth"] = new { maxVersion = 7, minVersion = 1, path = "auth.cgi" },
            ["SYNO.DSM.Info"] = new { maxVersion = 2, minVersion = 1, path = "entry.cgi" },
            ["SYNO.FileStation.List"] = new { maxVersion = 2, minVersion = 1, path = "entry.cgi" },
            ["SYNO.DownloadStation.Info"] = new { maxVersion = 2, minVersion = 1, path = "DownloadStation/info.cgi" },
            ["SYNO.DownloadStation.Statistic"] = new { maxVersion = 1, minVersion = 1, path = "DownloadStation/statistic.cgi" },
            ["SYNO.DownloadStation.Task"] = new { maxVersion = 2, minVersion = 1, path = "DownloadStation/task.cgi" },
            ["SYNO.DownloadStation2.Task"] = new { maxVersion = 2, minVersion = 1, path = "entry.cgi" },
        };

        return this.Ok(new
        {
            data = apis,
            success = true,
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [HttpPost]
    [Route("webapi/auth.cgi")]
    [Route("webapi/auth")]
    public IActionResult Auth([FromQuery] string api, [FromQuery] string method, [FromQuery] string account = null, [FromQuery] string passwd = null)
    {
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var masterKey = this.configFileProvider.ApiKey;
            var isAuth = (!string.IsNullOrWhiteSpace(masterKey) &&
                          (string.Equals(passwd, masterKey, StringComparison.Ordinal) ||
                           string.Equals(account, masterKey, StringComparison.Ordinal))) ||
                         RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);

            if (!isAuth)
            {
                return this.Ok(new { success = false, error = new { code = 400 } });
            }
        }

        return this.Ok(new
        {
            success = true,
            data = new
            {
                sid = SynologySid,
            },
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("webapi/DownloadStation/info.cgi")]
    public IActionResult Info([FromQuery] string method)
    {
        if (!this.IsSynologyAuthenticated())
        {
            return this.Unauthorized();
        }

        return this.Ok(new
        {
            success = true,
            data = new
            {
                version = 3890,
                version_string = "3.8-3890",
                is_manager = true,
            },
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("webapi/DownloadStation/statistic.cgi")]
    public IActionResult Statistic([FromQuery] string method)
    {
        if (!this.IsSynologyAuthenticated())
        {
            return this.Unauthorized();
        }

        var all = this.torrentService.GetAll().ToList();
        return this.Ok(new
        {
            success = true,
            data = new
            {
                speed_download = all.Sum(t => t.DownloadSpeed),
                speed_upload = all.Sum(t => t.UploadSpeed),
                emule_speed_download = 0,
                emule_speed_upload = 0,
            },
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
        if (!this.IsSynologyAuthenticated())
        {
            return this.Unauthorized();
        }

        if (string.Equals(api, "SYNO.DSM.Info", StringComparison.OrdinalIgnoreCase))
        {
            return this.Ok(new
            {
                data = new
                {
                    serial = "123456789",
                    version = "7.2-64570",
                    version_details = new { major = 7, minor = 2, buildnumber = 64570 },
                },
                success = true,
            });
        }

        if (string.Equals(api, "SYNO.FileStation.List", StringComparison.OrdinalIgnoreCase))
        {
            return this.Ok(new
            {
                data = new
                {
                    shares = new[]
                    {
                        new { name = "downloads", path = "/downloads" }
                    },
                },
                success = true,
            });
        }

        var formMethod = this.Request.HasFormContentType ? this.Request.Form["method"].ToString() : string.Empty;
        var effectiveMethod = (!string.IsNullOrWhiteSpace(method) ? method : formMethod).ToLowerInvariant();

        switch (effectiveMethod)
        {
            case "list":
                var all = this.torrentService.GetAll().ToList();
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
                        _ => 1,
                    },
                    status_text = t.Status switch
                    {
                        TorrentStatus.Downloading => "downloading",
                        TorrentStatus.Seeding => "seeding",
                        TorrentStatus.Paused => "paused",
                        TorrentStatus.Stopped => "finished",
                        TorrentStatus.Error => "error",
                        _ => "waiting",
                    },
                    type = "bt",
                    username = "admin",
                    additional = new
                    {
                        detail = new
                        {
                            destination = t.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
                            uri = string.Empty,
                            create_time = t.DateAdded != default ? new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            priority = "auto"
                        },
                        transfer = new
                        {
                            size_downloaded = t.Downloaded,
                            size_uploaded = t.Uploaded,
                            speed_download = t.DownloadSpeed,
                            speed_upload = t.UploadSpeed
                        }
                    },
                }).ToList();

                return this.Ok(new
                {
                    success = true,
                    data = new
                    {
                        total = tasks.Count,
                        offset = 0,
                        task = tasks,
                        tasks
                    },
                });

            case "create":
                var targetUri = !string.IsNullOrWhiteSpace(uri) ? uri : (!string.IsNullOrWhiteSpace(url) ? url : string.Empty);
                if (string.IsNullOrWhiteSpace(targetUri) && this.Request.HasFormContentType)
                {
                    if (this.Request.Form.TryGetValue("uri", out var formUriVal) && !string.IsNullOrWhiteSpace(formUriVal.ToString()))
                    {
                        targetUri = formUriVal.ToString();
                    }
                    else if (this.Request.Form.TryGetValue("url", out var formUrlVal) && !string.IsNullOrWhiteSpace(formUrlVal.ToString()))
                    {
                        targetUri = formUrlVal.ToString();
                    }
                }

                var targetDest = (!string.IsNullOrWhiteSpace(destination) ? destination : (this.Request.HasFormContentType && this.Request.Form.TryGetValue("destination", out var formDestVal) && !string.IsNullOrWhiteSpace(formDestVal.ToString()) ? formDestVal.ToString() : null))?.Trim('\"', '\'');

                if (!string.IsNullOrWhiteSpace(targetUri))
                {
                    if (targetUri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        await this.torrentService.AddFromMagnetAsync(targetUri, null, targetDest, false);
                    }
                    else
                    {
                        var bytes = await this.safeHttpClientService.DownloadBytesAsync(targetUri, maxSizeBytes: 10 * 1024 * 1024);
                        var parsed = this.torrentFileParser.Parse(bytes);
                        await this.torrentService.AddFromParsedTorrentAsync(parsed, null, targetDest, false, bytes);
                    }
                }
                else if (this.Request.HasFormContentType && this.Request.Form.Files.Count > 0)
                {
                    var file = this.Request.Form.Files[0];
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var parsed = this.torrentFileParser.Parse(bytes);
                    await this.torrentService.AddFromParsedTorrentAsync(parsed, null, targetDest, false, bytes);
                }

                return this.Ok(new { success = true });

            case "getinfo":
            case "getst":
                var formId = this.Request.HasFormContentType ? this.Request.Form["id"].ToString() : string.Empty;
                var effectiveId = !string.IsNullOrWhiteSpace(id) ? id : formId;
                var queryIds = effectiveId
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var infoTorrents = this.torrentService.GetAll().ToList();
                if (queryIds.Count > 0)
                {
                    infoTorrents = infoTorrents.Where(t => queryIds.Contains(t.InfoHash) || queryIds.Contains(t.Id.ToString())).ToList();
                }

                var infoTasks = infoTorrents.Select(t => new
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
                        _ => "waiting",
                    },
                    type = "bt",
                    username = "admin",
                    additional = new
                    {
                        detail = new
                        {
                            destination = t.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
                            uri = string.Empty,
                            create_time = t.DateAdded != default ? new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            priority = "auto"
                        },
                        transfer = new
                        {
                            size_downloaded = t.Downloaded,
                            size_uploaded = t.Uploaded,
                            speed_download = t.DownloadSpeed,
                            speed_upload = t.UploadSpeed
                        }
                    },
                }).ToList();

                return this.Ok(new
                {
                    success = true,
                    data = new
                    {
                        total = infoTasks.Count,
                        offset = 0,
                        task = infoTasks,
                        tasks = infoTasks
                    },
                });

            case "delete":
                var formDeleteId = this.Request.HasFormContentType ? this.Request.Form["id"].ToString() : string.Empty;
                var idsToDelete = (!string.IsNullOrWhiteSpace(id) ? id : formDeleteId).Split(',', StringSplitOptions.RemoveEmptyEntries);
                var formForceComplete = this.Request.HasFormContentType ? this.Request.Form["force_complete"].ToString() : string.Empty;
                var formDeleteData = this.Request.HasFormContentType ? this.Request.Form["delete_data"].ToString() : string.Empty;

                var deleteFiles = string.Equals(this.Request.Query["force_complete"], "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(this.Request.Query["delete_data"], "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(formForceComplete, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(formDeleteData, "true", StringComparison.OrdinalIgnoreCase);

                foreach (var taskId in idsToDelete)
                {
                    var t = this.torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await this.torrentService.DeleteAsync(t.Id, deleteFiles);
                    }
                }

                return this.Ok(new { success = true });

            case "pause":
                var formPauseId = this.Request.HasFormContentType ? this.Request.Form["id"].ToString() : string.Empty;
                var idsToPause = (!string.IsNullOrWhiteSpace(id) ? id : formPauseId).Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var taskId in idsToPause)
                {
                    var t = this.torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await this.torrentService.PauseAsync(t.Id);
                    }
                }

                return this.Ok(new { success = true });

            case "resume":
                var formResumeId = this.Request.HasFormContentType ? this.Request.Form["id"].ToString() : string.Empty;
                var idsToResume = (!string.IsNullOrWhiteSpace(id) ? id : formResumeId).Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var taskId in idsToResume)
                {
                    var t = this.torrentService.GetByInfoHash(taskId.Trim());
                    if (t != null)
                    {
                        await this.torrentService.ResumeAsync(t.Id);
                    }
                }

                return this.Ok(new { success = true });

            default:
                return this.Ok(new { success = true });
        }
    }
}
