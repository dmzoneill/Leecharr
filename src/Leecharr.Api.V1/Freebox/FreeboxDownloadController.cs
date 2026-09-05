// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Freebox;

public class FreeboxUpdateRequest
{
    public string Status { get; set; }

    public string QueuePos { get; set; }

    public double? StopRatio { get; set; }
}

[ApiController]
public class FreeboxDownloadController : ControllerBase
{
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public FreeboxDownloadController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    private bool IsFreeboxAuthenticated()
    {
        if (this.configFileProvider != null && !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        var token = this.Request.Headers["X-Fbx-App-Auth"].ToString();
        if (!string.IsNullOrEmpty(token) && string.Equals(token, "freebox-session-token", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    [HttpGet]
    [Route("api/v4/downloads/config")]
    public IActionResult GetDownloadConfig()
    {
        if (!this.IsFreeboxAuthenticated())
        {
            return this.Unauthorized();
        }

        var downloadDir = this.configService.DownloadDir ?? "/downloads";
        var b64Dir = Convert.ToBase64String(Encoding.UTF8.GetBytes(downloadDir));
        return this.Ok(new
        {
            success = true,
            result = new
            {
                download_dir = b64Dir,
                max_downloading_tasks = 10,
                use_watch_dir = false
            },
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("api/v4/login/authorize")]
    public IActionResult LoginAuthorize()
    {
        return this.Ok(new
        {
            success = true,
            result = new
            {
                logged_in = true,
                challenge = "freebox-challenge-token",
            },
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [HttpPost]
    [Route("api/v4/login/session")]
    [Route("api/v4/login")]
    public IActionResult LoginSession()
    {
        return this.Ok(new
        {
            success = true,
            result = new
            {
                session_token = "freebox-session-token",
                logged_in = true,
                permissions = new { downloader = true },
            },
        });
    }

    [HttpGet]
    [Route("api/v4/downloads")]
    public IActionResult GetDownloads()
    {
        if (!this.IsFreeboxAuthenticated())
        {
            return this.Unauthorized();
        }

        var all = this.torrentService.GetAll().ToList();
        var results = all.Select(t =>
        {
            var savePath = t.SavePath ?? (this.configService.DownloadDir ?? "/downloads");
            var b64Dir = Convert.ToBase64String(Encoding.UTF8.GetBytes(savePath));
            return new
            {
                id = t.Id,
                name = t.Name ?? string.Empty,
                download_dir = b64Dir,
                size = t.TotalSize,
                rx_pct = (long)(t.Progress * 10000),
                tx_pct = (long)(t.Ratio * 10000),
                rx_bytes = t.Downloaded,
                tx_bytes = t.Uploaded,
                rx_rate = t.DownloadSpeed,
                tx_rate = t.UploadSpeed,
                status = t.Status switch
                {
                    TorrentStatus.Downloading => "downloading",
                    TorrentStatus.Seeding => "seeding",
                    TorrentStatus.Paused => "stopped",
                    TorrentStatus.Stopped => "done",
                    TorrentStatus.Error => "error",
                    _ => "queued",
                },
                type = "bt",
                queue_pos = t.QueuePosition,
                io_priority = "normal",
                stop_ratio = (int)(t.TargetRatio * 100),
                error = "none",
                created_ts = t.DateAdded != default ? new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                eta = t.Eta,
            };
        }).ToList();

        return this.Ok(new
        {
            success = true,
            result = results,
        });
    }

    [HttpPost]
    [Route("api/v4/downloads/add")]
    public async Task<IActionResult> AddDownload([FromForm] string download_url, [FromForm] string download_dir)
    {
        if (!this.IsFreeboxAuthenticated())
        {
            return this.Unauthorized();
        }

        var addedId = 0;
        if (!string.IsNullOrWhiteSpace(download_url))
        {
            if (download_url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                var added = await this.torrentService.AddFromMagnetAsync(download_url, null, download_dir, false);
                addedId = added?.Id ?? 0;
            }
            else
            {
                var bytes = await this.safeHttpClientService.DownloadBytesAsync(download_url, maxSizeBytes: 10 * 1024 * 1024);
                var parsed = this.torrentFileParser.Parse(bytes);
                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, download_dir, false, bytes);
                addedId = added?.Id ?? 0;
            }
        }
        else if (this.Request.HasFormContentType && this.Request.Form.Files.Count > 0)
        {
            var file = this.Request.Form.Files[0];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = this.torrentFileParser.Parse(bytes);
            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, download_dir, false, bytes);
            addedId = added?.Id ?? 0;
        }

        return this.Ok(new
        {
            success = true,
            result = new { id = addedId },
        });
    }

    [HttpDelete]
    [Route("api/v4/downloads/{id}")]
    public async Task<IActionResult> DeleteDownload(int id)
    {
        if (!this.IsFreeboxAuthenticated())
        {
            return this.Unauthorized();
        }

        await this.torrentService.DeleteAsync(id, false);
        return this.Ok(new { success = true });
    }

    [HttpDelete]
    [Route("api/v4/downloads/{id}/erase")]
    public async Task<IActionResult> EraseDownload(int id)
    {
        if (!this.IsFreeboxAuthenticated())
        {
            return this.Unauthorized();
        }

        await this.torrentService.DeleteAsync(id, true);
        return this.Ok(new { success = true });
    }

    [HttpPut]
    [Route("api/v4/downloads/{id}")]
    public async Task<IActionResult> UpdateDownload(int id)
    {
        if (!this.IsFreeboxAuthenticated())
        {
            return this.Unauthorized();
        }

        string status = null;
        string queuePos = null;

        if (this.Request.HasFormContentType)
        {
            status = this.Request.Form["status"].ToString().ToLowerInvariant();
            queuePos = this.Request.Form["queue_pos"].ToString().ToLowerInvariant();
            if (double.TryParse(this.Request.Form["stop_ratio"].ToString(), out var formRatio) && formRatio >= 0)
            {
                var t = this.torrentService.Get(id);
                if (t != null)
                {
                    t.TargetRatio = formRatio / 100.0;
                    await this.torrentService.UpdateAsync(t);
                }
            }
        }
        else
        {
            try
            {
                using var reader = new StreamReader(this.Request.Body);
                var bodyStr = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(bodyStr))
                {
                    var jsonRequest = JsonSerializer.Deserialize<FreeboxUpdateRequest>(bodyStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (jsonRequest != null)
                    {
                        status = jsonRequest.Status?.ToLowerInvariant();
                        queuePos = jsonRequest.QueuePos?.ToLowerInvariant();
                        if (jsonRequest.StopRatio.HasValue && jsonRequest.StopRatio.Value >= 0)
                        {
                            var t = this.torrentService.Get(id);
                            if (t != null)
                            {
                                t.TargetRatio = jsonRequest.StopRatio.Value / 100.0;
                                await this.torrentService.UpdateAsync(t);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        if (status == "stopped")
        {
            await this.torrentService.PauseAsync(id);
        }
        else if (status == "downloading")
        {
            await this.torrentService.ResumeAsync(id);
        }

        if (!string.IsNullOrWhiteSpace(queuePos))
        {
            await this.torrentService.MoveQueueAsync(id, queuePos);
        }

        return this.Ok(new { success = true });
    }
}
