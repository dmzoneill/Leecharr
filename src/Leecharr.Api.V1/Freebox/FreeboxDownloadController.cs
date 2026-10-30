// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Freebox;

public class FreeboxUpdateRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("queue_pos")]
    public string QueuePos { get; set; }

    [JsonPropertyName("stop_ratio")]
    public double? StopRatio { get; set; }
}

public class FreeboxLoginSessionRequest
{
    [JsonPropertyName("app_id")]
    public string AppId { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }
}

[ApiController]
public class FreeboxDownloadController : ControllerBase
{
    private const string MissingDownloadPayloadError = "Missing download URL or file";
    private const string FailedToAddDownloadError = "Failed to add download";

    private static readonly RpcSessionStore AuthenticatedSessions = new();
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object LoginChallengeLock = new();
    private static string pendingLoginChallenge;
    private static DateTime pendingLoginChallengeExpiryUtc;
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
            if (AuthenticatedSessions.IsValid(token))
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
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            if (this.HasFreeboxLoginCredentials())
            {
                return this.Ok(new
                {
                    success = true,
                    result = new
                    {
                        logged_in = true,
                    },
                });
            }

            var challenge = IssueLoginChallenge();
            return this.Ok(new
            {
                success = true,
                result = new
                {
                    logged_in = false,
                    challenge,
                },
            });
        }

        var openChallenge = IssueLoginChallenge();
        return this.Ok(new
        {
            success = true,
            result = new
            {
                logged_in = true,
                challenge = openChallenge,
            },
        });
    }

    [AllowAnonymous] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
    [HttpGet]
    [HttpPost]
    [Route("api/v4/login/session")]
    [Route("api/v4/login")]
    public async Task<IActionResult> LoginSession()
    {
        if (this.configFileProvider != null && this.configFileProvider.AuthenticationEnabled)
        {
            var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
            if (!isAuth && !string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey))
            {
                var masterKey = this.configFileProvider.ApiKey;
                var token = this.Request.Headers["X-Fbx-App-Auth"].ToString();
                if (!string.IsNullOrEmpty(token) && (AuthenticatedSessions.IsValid(token) || RpcAuthenticationHelper.FixedTimeEquals(token, masterKey)))
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
                else if (this.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isAuth = await this.TryAuthenticateJsonLoginAsync(masterKey);
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
        AuthenticatedSessions.SetSession(sessionToken, DateTime.UtcNow.AddDays(7));

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
                status = MapDownloadStatus(t.Status, isDone),
                type = "bt",
                queue_pos = t.QueuePosition,
                io_priority = "normal",
                stop_ratio = (int)(t.TargetRatio * 100),
                error = MapDownloadError(t),
                created_ts = t.DateAdded != default ? t.DateAdded.ToUnixTimeSecondsAsUtc() : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                eta = t.Eta,
            };
        }).ToList();

        return this.Ok(new
        {
            success = true,
            result = results,
        });
    }

    [HttpPost] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
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
            catch (FormatException ex)
            {
                this.logger.Trace(ex, "download_dir is plain text, not base64");
            }
        }

        var hasFileUpload = this.Request.HasFormContentType && this.Request.Form.Files.Count > 0;
        if (string.IsNullOrWhiteSpace(download_url) && !hasFileUpload)
        {
            return this.FreeboxDownloadAddError(MissingDownloadPayloadError, "invalid_argument");
        }

        Torrent added = null;
        if (!string.IsNullOrWhiteSpace(download_url))
        {
            if (download_url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                added = await this.torrentService.AddFromMagnetAsync(download_url, null, effectiveDest, false);
            }
            else
            {
                var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? (this.configFileProvider?.MaxTorrentFileSizeBytes ?? 0);
                if (maxTorrentBytes <= 0)
                {
                    maxTorrentBytes = 250L * 1024 * 1024;
                }
                var bytes = await this.safeHttpClientService.DownloadBytesAsync(download_url, maxSizeBytes: maxTorrentBytes);
                var parsed = this.torrentFileParser.Parse(bytes);
                added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, effectiveDest, false, bytes);
            }
        }
        else if (hasFileUpload)
        {
            var file = this.Request.Form.Files[0];
            var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? (this.configFileProvider?.MaxTorrentFileSizeBytes ?? 0);
            if (maxTorrentBytes <= 0)
            {
                maxTorrentBytes = 250L * 1024 * 1024;
            }

            if (file.Length > maxTorrentBytes)
            {
                return this.StatusCode(StatusCodes.Status413PayloadTooLarge, $"Torrent file size exceeds maximum allowed size of {maxTorrentBytes} bytes.");
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            if (ms.Length > maxTorrentBytes)
            {
                return this.StatusCode(StatusCodes.Status413PayloadTooLarge, $"Torrent file size exceeds maximum allowed size of {maxTorrentBytes} bytes.");
            }

            var bytes = ms.ToArray();
            var parsed = this.torrentFileParser.Parse(bytes);
            added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, effectiveDest, false, bytes);
        }

        if (added == null || added.Id <= 0)
        {
            return this.FreeboxDownloadAddError(FailedToAddDownloadError, "download_add_failed");
        }

        return this.Ok(new
        {
            success = true,
            result = new { id = added.Id },
        });
    }

    private IActionResult FreeboxDownloadAddError(string message, string errorCode)
    {
        return this.Ok(new
        {
            success = false,
            msg = message,
            error_code = errorCode,
        });
    }

    [HttpDelete] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
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

    [HttpPut] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
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
                    var jsonRequest = JsonSerializer.Deserialize<FreeboxUpdateRequest>(bodyStr, CaseInsensitiveJsonOptions);
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
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to parse Freebox download update JSON");
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
            await this.torrentService.MoveQueueAsync(id, ToMoveQueuePosition(queuePos));
        }

        return this.Ok(new { success = true });
    }

    private static string ToMoveQueuePosition(string queuePos)
    {
        if (int.TryParse(queuePos, out var oneBasedPosition))
        {
            return Math.Max(0, oneBasedPosition - 1).ToString();
        }

        return queuePos;
    }

    private bool HasFreeboxLoginCredentials()
    {
        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        var token = this.Request.Headers["X-Fbx-App-Auth"].ToString();
        if (!string.IsNullOrEmpty(token) &&
            (AuthenticatedSessions.IsValid(token) ||
             (this.configFileProvider != null &&
              !string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey) &&
              RpcAuthenticationHelper.FixedTimeEquals(token, this.configFileProvider.ApiKey))))
        {
            return true;
        }

        return false;
    }

    private static string MapDownloadStatus(TorrentStatus status, bool isDone)
    {
        return status switch
        {
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "seeding",
            TorrentStatus.Completed => "done",
            TorrentStatus.Checking => "checking",
            TorrentStatus.QueuedForChecking => "checking",
            TorrentStatus.Paused => "stopped",
            TorrentStatus.Stopped => isDone ? "done" : "stopped",
            TorrentStatus.Error => "error",
            TorrentStatus.Stalled => "retry",
            TorrentStatus.Queued => "queued",
            _ => "queued",
        };
    }

    private static string MapDownloadError(Torrent torrent)
    {
        if (!string.IsNullOrWhiteSpace(torrent.ErrorMessage))
        {
            return torrent.ErrorMessage;
        }

        return torrent.Status == TorrentStatus.Error ? "error" : "none";
    }

    private static string IssueLoginChallenge()
    {
        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (LoginChallengeLock)
        {
            pendingLoginChallenge = challenge;
            pendingLoginChallengeExpiryUtc = DateTime.UtcNow.AddMinutes(10);
        }

        return challenge;
    }

    private static bool TryGetActiveLoginChallenge(out string challenge)
    {
        lock (LoginChallengeLock)
        {
            if (!string.IsNullOrWhiteSpace(pendingLoginChallenge) && pendingLoginChallengeExpiryUtc > DateTime.UtcNow)
            {
                challenge = pendingLoginChallenge;
                return true;
            }
        }

        challenge = null;
        return false;
    }

    private static void ClearLoginChallenge()
    {
        lock (LoginChallengeLock)
        {
            pendingLoginChallenge = null;
            pendingLoginChallengeExpiryUtc = default;
        }
    }

    private async Task<bool> TryAuthenticateJsonLoginAsync(string appToken)
    {
        try
        {
            using var reader = new StreamReader(this.Request.Body, leaveOpen: true);
            var bodyStr = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(bodyStr))
            {
                return false;
            }

            var loginRequest = JsonSerializer.Deserialize<FreeboxLoginSessionRequest>(bodyStr, CaseInsensitiveJsonOptions);
            if (loginRequest == null ||
                string.IsNullOrWhiteSpace(loginRequest.AppId) ||
                string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                return false;
            }

            if (!TryGetActiveLoginChallenge(out var challenge))
            {
                return false;
            }

            var expectedPassword = FreeboxAuthHelper.ComputeSessionPassword(appToken, challenge);
            if (!RpcAuthenticationHelper.FixedTimeEquals(loginRequest.Password, expectedPassword))
            {
                return false;
            }

            ClearLoginChallenge();
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to parse Freebox JSON login request");
            return false;
        }
    }
}
