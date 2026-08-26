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
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public UTorrentWebUiController(
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
    [Route("gui/token.html")]
    public IActionResult GetToken()
    {
        Response.Cookies.Append("GUID", "leecharr-guid-cookie", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
        var html = $"<html><div id=\"token\">{UtorrentToken}</div></html>";
        return Content(html, "text/html", Encoding.UTF8);
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
            var targetCategory = label ?? (Request.HasFormContentType ? Request.Form["label"].ToString() : null);
            var targetDir = download_dir ?? path ?? (Request.HasFormContentType ? (Request.Form["download_dir"].ToString() ?? Request.Form["path"].ToString()) : null);

            switch (action.ToLowerInvariant())
            {
                case "add-url":
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        if (s.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                        {
                            await _torrentService.AddFromMagnetAsync(s, targetCategory, targetDir, false);
                        }
                        else
                        {
                            using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                            var bytes = await client.GetByteArrayAsync(s);
                            var parsed = _torrentFileParser.Parse(bytes);
                            await _torrentService.AddFromParsedTorrentAsync(parsed, targetCategory, targetDir, false, bytes);
                        }
                    }

                    return BuildTorrentListResponse();

                case "add-file":
                    if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                    {
                        var file = Request.Form.Files[0];
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        var bytes = ms.ToArray();
                        var parsed = _torrentFileParser.Parse(bytes);
                        await _torrentService.AddFromParsedTorrentAsync(parsed, targetCategory, targetDir, false, bytes);
                    }

                    return BuildTorrentListResponse();

                case "start":
                case "unpause":
                case "forcestart":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = _torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await _torrentService.ResumeAsync(t.Id);
                            }
                        }
                    }

                    return BuildTorrentListResponse();

                case "stop":
                case "pause":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = _torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await _torrentService.PauseAsync(t.Id);
                            }
                        }
                    }

                    return BuildTorrentListResponse();

                case "remove":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = _torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await _torrentService.DeleteAsync(t.Id, false);
                            }
                        }
                    }

                    return BuildTorrentListResponse();

                case "removedata":
                case "removedatatorrent":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = _torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await _torrentService.DeleteAsync(t.Id, true);
                            }
                        }
                    }

                    return BuildTorrentListResponse();

                case "recheck":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        foreach (var h in hash.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = _torrentService.GetByInfoHash(h.Trim());
                            if (t != null)
                            {
                                await _torrentService.ForceRecheckAsync(t.Id);
                            }
                        }
                    }

                    return BuildTorrentListResponse();

                case "setprio":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var target = _torrentService.GetByInfoHash(hash.Trim());
                        var pVal = Request.Query["p"].ToString();
                        var fVal = Request.Query["f"].ToString();
                        if (target != null && int.TryParse(pVal, out var prio) && int.TryParse(fVal, out var fileIdx))
                        {
                            var files = _torrentFileService.GetFiles(target.Id).ToList();
                            if (fileIdx >= 0 && fileIdx < files.Count)
                            {
                                await _torrentFileService.SetPriorityAsync(files[fileIdx].Id, prio);
                            }
                        }
                    }

                    return BuildTorrentListResponse();

                case "setprops":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
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
                                t.DownloadLimit = dlVal * 1024;
                            }
                            else if (string.Equals(s, "max_ul_rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var ulVal))
                            {
                                t.UploadLimit = ulVal * 1024;
                            }

                            await _torrentService.UpdateAsync(t);
                        }
                    }

                    return BuildTorrentListResponse();

                case "getprops":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            return Ok(new
                            {
                                build = 45000,
                                props = new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "hash", t.InfoHash.ToUpperInvariant() },
                                        { "trackers", string.Empty },
                                        { "ulrate", t.UploadLimit / 1024 },
                                        { "dlrate", t.DownloadLimit / 1024 },
                                        { "superseed", 0 },
                                        { "dht", 1 },
                                        { "pex", 1 },
                                        { "seed_override", t.TargetRatio > 0 ? 1 : 0 },
                                        { "seed_ratio", (int)(t.TargetRatio * 1000) },
                                        { "seed_time", 0 },
                                        { "ul_slots", 0 }
                                    }
                                }
                            });
                        }
                    }

                    return Ok(new { build = 45000, props = Array.Empty<object>() });

                case "getfiles":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            var files = _torrentFileService.GetFiles(t.Id);
                            var fileRows = files.Select(f => new object[]
                            {
                                f.Path,
                                f.Size,
                                (long)(f.Size * f.Progress),
                                f.Priority
                            }).ToList();

                            return Ok(new
                            {
                                build = 45000,
                                files = new object[] { t.InfoHash.ToUpperInvariant(), fileRows }
                            });
                        }
                    }

                    return Ok(new { build = 45000, files = Array.Empty<object>() });

                case "queueup":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "up");
                        }
                    }

                    return BuildTorrentListResponse();

                case "queuedown":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "down");
                        }
                    }

                    return BuildTorrentListResponse();

                case "queuetop":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "top");
                        }
                    }

                    return BuildTorrentListResponse();

                case "queuebottom":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.MoveQueueAsync(t.Id, "bottom");
                        }
                    }

                    return BuildTorrentListResponse();

                case "getsettings":
                    return Ok(new
                    {
                        build = 45000,
                        settings = new object[]
                        {
                            new object[] { "dir_active_download", 2, _configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
                            new object[] { "dir_completed_download", 2, _configService.DownloadDir ?? "/downloads" },
                            new object[] { "max_dl_rate", 0, _configService.MaxDownloadSpeedKbps },
                            new object[] { "max_ul_rate", 0, _configService.MaxUploadSpeedKbps }
                        }
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
                            _configService.SaveConfigDictionary(dict);
                        }
                    }

                    return Ok(new { build = 45000 });
            }
        }

        return BuildTorrentListResponse();
    }

    private IActionResult BuildTorrentListResponse()
    {
        var torrents = _torrentService.GetAll().ToList();
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
                t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                string.Empty,
                modifiedUnix
            });
        }

        var labels = _categoryService.GetAll().Select(c => new object[]
        {
            c.Name,
            torrents.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase))
        }).ToList();

        return Ok(new
        {
            build = 45000,
            torrents = rows,
            label = labels,
            torrentc = "1",
            rssfeeds = new object[] { },
            rssfilters = new object[] { }
        });
    }
}
