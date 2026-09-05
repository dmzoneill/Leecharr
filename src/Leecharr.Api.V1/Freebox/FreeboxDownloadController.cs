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
    private static readonly RpcSessionStore authenticatedSessions = new();
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
        if (string.IsNullOrEmpty(token))
        {
            token = this.Request.Query["X-Fbx-App-Auth"].ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
            token = this.Request.Query["session_token"].ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
            token = this.Request.Query["session"].ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
            token = this.Request.Query["token"].ToString();
        }

        if (!string.IsNullOrEmpty(token))
        {
            if (authenticatedSessions.IsValid(token))
            {
                return true;
            }

            if (this.configFileProvider != null &&
                !string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey) &&
                RpcAuthenticationHelper.FixedTimeEquals(token, this.configFileProvider.ApiKey))
            {
                return true;
            }
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
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
            if (!isAuth && !string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey))
            {
                var masterKey = this.configFileProvider.ApiKey;
                var token = this.Request.Headers["X-Fbx-App-Auth"].ToString();
                if (!string.IsNullOrEmpty(token) && (authenticatedSessions.IsValid(token) || RpcAuthenticationHelper.FixedTimeEquals(token, masterKey)))
                {
                    isAuth = true;
                }
                else if (this.Request.Query.TryGetValue("password", out var queryPass) && !string.IsNullOrWhiteSpace(queryPass) &&
                         RpcAuthenticationHelper.FixedTimeEquals(queryPass.ToString(), masterKey))
                {
                    isAuth = true;
                }
                else if (this.Request.Query.TryGetValue("app_token", out var queryAppToken) && !string.IsNullOrWhiteSpace(queryAppToken) &&
                         RpcAuthenticationHelper.FixedTimeEquals(queryAppToken.ToString(), masterKey))
                {
                    isAuth = true;
                }
                else if (this.Request.HasFormContentType)
                {
                    var formPass = this.Request.Form["password"].ToString();
                    var formAppToken = this.Request.Form["app_token"].ToString();
                    var formApiKey = this.Request.Form["api_key"].ToString();
                    if (!string.IsNullOrWhiteSpace(formPass) && RpcAuthenticationHelper.FixedTimeEquals(formPass, masterKey))
                    {
                        isAuth = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(formAppToken) && RpcAuthenticationHelper.FixedTimeEquals(formAppToken, masterKey))
                    {
                        isAuth = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(formApiKey) && RpcAuthenticationHelper.FixedTimeEquals(formApiKey, masterKey))
                    {
                        isAuth = true;
                    }
                }
            }

            if (!isAuth)
            {
                return this.Ok(new
                {
                    success = false,
                    msg = "Invalid credentials",
                    error_code = "auth_required",
                });
            }
        }

        var sessionToken = Guid.NewGuid().ToString("N");
        authenticatedSessions.SetSession(sessionToken, DateTime.UtcNow.AddDays(7));

        return this.Ok(new
        {
            success = true,
            result = new
            {
                session_token = sessionToken,
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
            var isDone = t.Progress >= 1.0 || (t.TotalSize > 0 && t.Downloaded >= t.TotalSize) || t.Status == TorrentStatus.Completed || t.Status == TorrentStatus.Seeding;
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
                    TorrentStatus.Stopped => isDone ? "done" : "stopped",
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

        var effectiveDest = download_dir;
        if (!string.IsNullOrWhiteSpace(download_dir))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(download_dir));
                if (Path.IsPathRooted(decoded) || decoded.Contains('/') || decoded.Contains('\\'))
                {
                    effectiveDest = decoded;
                }
            }
            catch (FormatException)
            {
                // download_dir was plain text
            }
        }

        var addedId = 0;
        if (!string.IsNullOrWhiteSpace(download_url))
        {
            if (download_url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                var added = await this.torrentService.AddFromMagnetAsync(download_url, null, effectiveDest, false);
                addedId = added?.Id ?? 0;
            }
            else
            {
                var bytes = await this.safeHttpClientService.DownloadBytesAsync(download_url, maxSizeBytes: 10 * 1024 * 1024);
                var parsed = this.torrentFileParser.Parse(bytes);
                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, effectiveDest, false, bytes);
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
            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, effectiveDest, false, bytes);
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
