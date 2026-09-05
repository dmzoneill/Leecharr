// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

namespace Leecharr.Api.V1.UTorrent;

[ApiController]
public class UTorrentWebUiController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> TokenStore = new();
    private static readonly HashSet<string> MutatingActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "add-url",
        "add-file",
        "start",
        "unpause",
        "forcestart",
        "stop",
        "pause",
        "remove",
        "removedata",
        "removedatatorrent",
        "recheck",
        "setprio",
        "setprops",
        "queueup",
        "queuedown",
        "queuetop",
        "queuebottom",
        "setsetting",
    };

    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public UTorrentWebUiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        ISafeHttpClientService safeHttpClientService = null,
        IConfigFileProvider configFileProvider = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.configFileProvider = configFileProvider;
    }

    [HttpGet]
    [Route("gui/token.html")]
    public IActionResult GetToken()
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"uTorrent\"";
            return this.Unauthorized();
        }

        CleanExpiredTokens();

        var guid = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        TokenStore[guid] = (token, DateTime.UtcNow.AddHours(24));

        this.Response.Cookies.Append("GUID", guid, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
        var html = $"<html><div id=\"token\">{token}</div></html>";
        return this.Content(html, "text/html", Encoding.UTF8);
    }

    [HttpGet]
    [HttpPost]
    [Route("gui")]
    public async Task<IActionResult> HandleWebUi(
        [FromQuery] string list,
        [FromQuery] string action,
        [FromQuery] string hash,
        [FromQuery] string s,
        [FromQuery] string v,
        [FromQuery] string label,
        [FromQuery] string path,
        [FromQuery] string download_dir,
        [FromQuery] string token)
    {
        var effAction = !string.IsNullOrWhiteSpace(action) ? action : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("action") ? this.Request.Form["action"].ToString() : null);
        var effHash = !string.IsNullOrWhiteSpace(hash) ? hash : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("hash") ? this.Request.Form["hash"].ToString() : null);
        var effS = !string.IsNullOrWhiteSpace(s) ? s : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("s") ? this.Request.Form["s"].ToString() : null);
        var effV = !string.IsNullOrWhiteSpace(v) ? v : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("v") ? this.Request.Form["v"].ToString() : null);
        var effLabel = !string.IsNullOrWhiteSpace(label) ? label : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("label") ? this.Request.Form["label"].ToString() : null);
        var effPath = !string.IsNullOrWhiteSpace(path) ? path : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("path") ? this.Request.Form["path"].ToString() : null);
        var effDownloadDir = !string.IsNullOrWhiteSpace(download_dir) ? download_dir : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("download_dir") ? this.Request.Form["download_dir"].ToString() : null);
        var effToken = !string.IsNullOrWhiteSpace(token) ? token : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("token") ? this.Request.Form["token"].ToString() : null);
        var effList = !string.IsNullOrWhiteSpace(list) ? list : (this.Request.HasFormContentType && this.Request.Form.ContainsKey("list") ? this.Request.Form["list"].ToString() : null);

        var isApiAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
        var isTokenValid = this.ValidateToken(effToken);

        if (!isApiAuth && !isTokenValid)
        {
            return this.BadRequest("invalid request");
        }

        if (!string.IsNullOrWhiteSpace(effAction))
        {
            if (MutatingActions.Contains(effAction) && !HttpMethods.IsPost(this.Request.Method))
            {
                return this.StatusCode(StatusCodes.Status405MethodNotAllowed, "State-mutating actions must be performed using HTTP POST.");
            }

            var targetCategory = !string.IsNullOrWhiteSpace(effLabel) ? effLabel : null;
            var targetDir = !string.IsNullOrWhiteSpace(effDownloadDir) ? effDownloadDir : (!string.IsNullOrWhiteSpace(effPath) ? effPath : null);

            switch (effAction.ToLowerInvariant())
            {
                case "add-url":
                    if (!string.IsNullOrWhiteSpace(effS))
                    {
                        if (effS.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                        {
                            await this.torrentService.AddFromMagnetAsync(effS, targetCategory, targetDir, false);
                        }
                        else
                        {
                            var bytes = await this.safeHttpClientService.DownloadBytesAsync(effS);
                            var parsed = this.torrentFileParser.Parse(bytes);
                            await this.torrentService.AddFromParsedTorrentAsync(parsed, targetCategory, targetDir, false, bytes);
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "add-file":
                    if (this.Request.HasFormContentType && this.Request.Form.Files.Count > 0)
                    {
                        var file = this.Request.Form.Files[0];
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        var bytes = ms.ToArray();
                        var parsed = this.torrentFileParser.Parse(bytes);
                        await this.torrentService.AddFromParsedTorrentAsync(parsed, targetCategory, targetDir, false, bytes);
                    }

                    return this.BuildTorrentListResponse();

                case "start":
                case "unpause":
                case "forcestart":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        foreach (var h in effHash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = this.torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await this.torrentService.ResumeAsync(t.Id);
                            }
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "stop":
                case "pause":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        foreach (var h in effHash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = this.torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await this.torrentService.PauseAsync(t.Id);
                            }
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "remove":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        foreach (var h in effHash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = this.torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await this.torrentService.DeleteAsync(t.Id, false);
                            }
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "removedata":
                case "removedatatorrent":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        foreach (var h in effHash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = this.torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await this.torrentService.DeleteAsync(t.Id, true);
                            }
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "recheck":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        foreach (var h in effHash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = this.torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await this.torrentService.ForceRecheckAsync(t.Id);
                            }
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "setprio":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var target = this.torrentService.GetByInfoHash(effHash.Trim());
                        var pVal = this.Request.Query["p"].ToString();
                        var fVal = this.Request.Query["f"].ToString();
                        if (string.IsNullOrEmpty(pVal) && this.Request.HasFormContentType)
                        {
                            pVal = this.Request.Form["p"].ToString();
                        }

                        if (string.IsNullOrEmpty(fVal) && this.Request.HasFormContentType)
                        {
                            fVal = this.Request.Form["f"].ToString();
                        }

                        if (target != null && int.TryParse(pVal, out var prio) && int.TryParse(fVal, out var fileIdx))
                        {
                            var files = this.torrentFileService.GetFiles(target.Id).ToList();
                            if (fileIdx >= 0 && fileIdx < files.Count)
                            {
                                await this.torrentFileService.SetPriorityAsync(files[fileIdx].Id, prio);
                            }
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "setprops":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            if (string.Equals(effS, "label", StringComparison.OrdinalIgnoreCase))
                            {
                                t.Category = effV ?? string.Empty;
                            }
                            else if (string.Equals(effS, "seed_ratio", StringComparison.OrdinalIgnoreCase) && double.TryParse(effV, out var rVal))
                            {
                                t.TargetRatio = rVal / 1000.0;
                            }
                            else if (string.Equals(effS, "seed_time", StringComparison.OrdinalIgnoreCase) && long.TryParse(effV, out var stVal))
                            {
                                t.TargetSeedTimeMinutes = (int)(stVal / 60);
                            }
                            else if ((string.Equals(effS, "dlrate", StringComparison.OrdinalIgnoreCase) || string.Equals(effS, "max_dl_rate", StringComparison.OrdinalIgnoreCase)) && int.TryParse(effV, out var dlVal))
                            {
                                t.DownloadLimit = dlVal > 0 ? dlVal / 1024 : 0;
                            }
                            else if ((string.Equals(effS, "ulrate", StringComparison.OrdinalIgnoreCase) || string.Equals(effS, "max_ul_rate", StringComparison.OrdinalIgnoreCase)) && int.TryParse(effV, out var ulVal))
                            {
                                t.UploadLimit = ulVal > 0 ? ulVal / 1024 : 0;
                            }

                            await this.torrentService.UpdateAsync(t);
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "getprops":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            return this.Ok(new
                            {
                                build = 45000,
                                props = new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "hash", t.InfoHash.ToUpperInvariant() },
                                        { "trackers", string.Empty },
                                        { "ulrate", t.UploadLimit * 1024 },
                                        { "dlrate", t.DownloadLimit * 1024 },
                                        { "super_seed", 0 },
                                        { "dht", 1 },
                                        { "pex", 1 },
                                        { "seed_override", t.TargetRatio > 0 ? 1 : 0 },
                                        { "seed_ratio", (int)(t.TargetRatio * 1000) },
                                        { "seed_time", 0 },
                                        { "ul_slots", 0 }
                                    }
                                },
                            });
                        }
                    }

                    return this.Ok(new { build = 45000, props = Array.Empty<object>() });

                case "getfiles":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            var files = this.torrentFileService.GetFiles(t.Id).ToList();
                            var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
                            TorrentFileProgressEnricher.Enrich(t, files, downloadTask);
                            var fileRows = files.Select(f => new object[]
                            {
                                f.Path,
                                f.Size,
                                f.BytesCompleted,
                                f.Priority,
                            }).ToList();

                            return this.Ok(new
                            {
                                build = 45000,
                                files = new object[] { t.InfoHash.ToUpperInvariant(), fileRows },
                            });
                        }
                    }

                    return this.Ok(new { build = 45000, files = Array.Empty<object>() });

                case "queueup":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "up");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "queuedown":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "down");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "queuetop":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "top");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "queuebottom":
                    if (!string.IsNullOrWhiteSpace(effHash))
                    {
                        var t = this.torrentService.GetByInfoHash(effHash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "bottom");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "getsettings":
                    return this.Ok(new
                    {
                        build = 45000,
                        settings = new object[]
                        {
                            new object[] { "dir_active_download", 2, this.configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
                            new object[] { "dir_completed_download", 2, this.configService.DownloadDir ?? "/downloads" },
                            new object[] { "max_dl_rate", 0, this.configService.MaxDownloadSpeedKbps },
                            new object[] { "max_ul_rate", 0, this.configService.MaxUploadSpeedKbps }
                        },
                    });

                case "setsetting":
                    if (!string.IsNullOrWhiteSpace(effS))
                    {
                        var dict = new Dictionary<string, object>();
                        if (string.Equals(effS, "max_dl_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(effV, out var dl))
                        {
                            dict["MaxDownloadSpeedKbps"] = dl;
                        }
                        else if (string.Equals(effS, "max_ul_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(effV, out var ul))
                        {
                            dict["MaxUploadSpeedKbps"] = ul;
                        }
                        else if (string.Equals(effS, "dir_completed_download", StringComparison.OrdinalIgnoreCase))
                        {
                            dict["DownloadDir"] = effV;
                        }
                        else if (string.Equals(effS, "dir_active_download", StringComparison.OrdinalIgnoreCase))
                        {
                            dict["IncompleteDownloadDir"] = effV;
                        }

                        if (dict.Count > 0)
                        {
                            this.configService.SaveConfigDictionary(dict);
                        }
                    }

                    return this.Ok(new { build = 45000 });
            }
        }

        return this.BuildTorrentListResponse();
    }

    private IActionResult BuildTorrentListResponse()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var rows = new List<object[]>();

        foreach (var t in torrents)
        {
            var statusFlag = 128; // Loaded

            if (t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding)
            {
                statusFlag = 1 | 128;
            }
            else if (t.Status == TorrentStatus.Paused)
            {
                statusFlag = 32 | 128;
            }
            else if (t.Status == TorrentStatus.Stopped)
            {
                statusFlag = 128;
            }
            else if (t.Status == TorrentStatus.Checking)
            {
                statusFlag = 2 | 128;
            }
            else if (t.Status == TorrentStatus.Error)
            {
                statusFlag = 16 | 128;
            }
            else if (t.Status == TorrentStatus.Queued)
            {
                statusFlag = 64 | 128;
            }

            var addedUnix = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds();
            var completedUnix = t.DateCompleted.HasValue ? new DateTimeOffset(t.DateCompleted.Value).ToUnixTimeSeconds() : 0;
            var modifiedUnix = t.LastActive.HasValue ? new DateTimeOffset(t.LastActive.Value).ToUnixTimeSeconds() : addedUnix;

            rows.Add(new object[]
            {
                t.InfoHash.ToUpperInvariant(),
                statusFlag,
                t.Name ?? string.Empty,
                t.TotalSize,
                (int)(t.Progress * 1000),
                t.Downloaded,
                t.Uploaded,
                (int)(t.Ratio * 1000),
                t.UploadSpeed,
                t.DownloadSpeed,
                t.Eta,
                t.Category ?? string.Empty,
                t.Leechers,
                t.Leechers,
                t.Seeders,
                t.Seeders,
                65536,
                t.QueuePosition,
                Math.Max(0, t.TotalSize - t.Downloaded),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                addedUnix,
                completedUnix,
                string.Empty,
                t.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
                string.Empty,
                modifiedUnix,
            });
        }

        var labels = this.categoryService.GetAll().Select(c => new object[]
        {
            c.Name,
            torrents.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase)),
        }).ToList();

        return this.Ok(new
        {
            build = 45000,
            torrents = rows,
            label = labels,
            torrentc = "1",
            rssfeeds = new object[] { },
            rssfilters = new object[] { },
        });
    }

    private static void CleanExpiredTokens()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in TokenStore)
        {
            if (kvp.Value.ExpiresAt <= now)
            {
                TokenStore.TryRemove(kvp.Key, out _);
            }
        }

        if (TokenStore.Count > 10000)
        {
            var toRemove = TokenStore.OrderBy(k => k.Value.ExpiresAt).Take(TokenStore.Count - 5000);
            foreach (var item in toRemove)
            {
                TokenStore.TryRemove(item.Key, out _);
            }
        }
    }

    private bool ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!this.Request.Cookies.TryGetValue("GUID", out var guid) || string.IsNullOrWhiteSpace(guid))
        {
            return false;
        }

        if (!TokenStore.TryGetValue(guid, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            TokenStore.TryRemove(guid, out _);
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(entry.Token);
        var actualBytes = Encoding.UTF8.GetBytes(token);

        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
