// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.UTorrent;

[AllowAnonymous]
[ApiController]
public class UTorrentWebUiController : ControllerBase
{
    private const string UtorrentToken = "LEECHARR_UTORRENT_AUTH_TOKEN";
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public UTorrentWebUiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
    }

    [HttpGet]
    [Route("gui/token.html")]
    public IActionResult GetToken()
    {
        this.Response.Cookies.Append("GUID", "leecharr-guid-cookie", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
        var html = $"<html><div id=\"token\">{UtorrentToken}</div></html>";
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
        if (!string.IsNullOrWhiteSpace(action))
        {
            var targetCategory = label ?? (this.Request.HasFormContentType ? this.Request.Form["label"].ToString() : null);
            var targetDir = download_dir ?? path ?? (this.Request.HasFormContentType ? (this.Request.Form["download_dir"].ToString() ?? this.Request.Form["path"].ToString()) : null);

            switch (action.ToLowerInvariant())
            {
                case "add-url":
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        if (s.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                        {
                            await this.torrentService.AddFromMagnetAsync(s, targetCategory, targetDir, false);
                        }
                        else
                        {
                            using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                            var bytes = await client.GetByteArrayAsync(s);
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var target = this.torrentService.GetByInfoHash(hash.Trim());
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            if (string.Equals(s, "label", StringComparison.OrdinalIgnoreCase))
                            {
                                t.Category = v ?? string.Empty;
                            }
                            else if (string.Equals(s, "seed_ratio", StringComparison.OrdinalIgnoreCase) && double.TryParse(v, out var rVal))
                            {
                                t.TargetRatio = rVal / 1000.0;
                            }
                            else if (string.Equals(s, "seed_time", StringComparison.OrdinalIgnoreCase) && long.TryParse(v, out var stVal))
                            {
                                t.TargetSeedTimeMinutes = (int)(stVal / 60);
                            }
                            else if (string.Equals(s, "max_dl_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var dlVal))
                            {
                                t.DownloadLimit = dlVal > 0 ? dlVal / 1024 : 0;
                            }
                            else if (string.Equals(s, "max_ul_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var ulVal))
                            {
                                t.UploadLimit = ulVal > 0 ? ulVal / 1024 : 0;
                            }

                            await this.torrentService.UpdateAsync(t);
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "getprops":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            var files = this.torrentFileService.GetFiles(t.Id);
                            var fileRows = files.Select(f => new object[]
                            {
                                f.Path,
                                f.Size,
                                (long)(f.Size * f.Progress),
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
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "up");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "queuedown":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "down");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "queuetop":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "top");
                        }
                    }

                    return this.BuildTorrentListResponse();

                case "queuebottom":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
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
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        var dict = new Dictionary<string, object>();
                        if (string.Equals(s, "max_dl_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var dl))
                        {
                            dict["MaxDownloadSpeedKbps"] = dl;
                        }
                        else if (string.Equals(s, "max_ul_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var ul))
                        {
                            dict["MaxUploadSpeedKbps"] = ul;
                        }
                        else if (string.Equals(s, "dir_completed_download", StringComparison.OrdinalIgnoreCase))
                        {
                            dict["DownloadDir"] = v;
                        }
                        else if (string.Equals(s, "dir_active_download", StringComparison.OrdinalIgnoreCase))
                        {
                            dict["IncompleteDownloadDir"] = v;
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
            var isFinished = t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding;
            var statusFlag = 1; // Loaded

            if (t.Status == TorrentStatus.Downloading)
            {
                statusFlag = 1 | 2 | 16 | 512; // 531: Loaded + Queued + Checked + Started
            }
            else if (t.Status == TorrentStatus.Seeding)
            {
                statusFlag = 1 | 2 | 16 | 128 | 512; // 659: Loaded + Queued + Checked + Finished + Started
            }
            else if (t.Status == TorrentStatus.Paused)
            {
                statusFlag = 1 | 4 | 16 | (isFinished ? 128 : 0);
            }
            else if (t.Status == TorrentStatus.Stopped)
            {
                statusFlag = 1 | 16 | (isFinished ? 128 : 0);
            }
            else if (t.Status == TorrentStatus.Checking)
            {
                statusFlag = 1 | 64;
            }
            else if (t.Status == TorrentStatus.Error)
            {
                statusFlag = 1 | 8 | 16;
            }
            else
            {
                statusFlag = 1 | 2 | 16 | (isFinished ? 128 : 0);
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
                0,
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
}
