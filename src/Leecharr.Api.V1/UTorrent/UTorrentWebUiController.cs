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
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public UTorrentWebUiController(
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
        [FromQuery] string token)
    {
        if (!string.IsNullOrWhiteSpace(action))
        {
            switch (action.ToLowerInvariant())
            {
                case "add-url":
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        if (s.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                        {
                            await _torrentService.AddFromMagnetAsync(s, null, null, false);
                        }
                        else
                        {
                            using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                            var bytes = await client.GetByteArrayAsync(s);
                            var parsed = _torrentFileParser.Parse(bytes);
                            await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, bytes);
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
                        await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, bytes);
                    }

                    return BuildTorrentListResponse();

                case "start":
                case "unpause":
                case "forcestart":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.ResumeAsync(t.Id);
                        }
                    }

                    return BuildTorrentListResponse();

                case "stop":
                case "pause":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.PauseAsync(t.Id);
                        }
                    }

                    return BuildTorrentListResponse();

                case "remove":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.DeleteAsync(t.Id, false);
                        }
                    }

                    return BuildTorrentListResponse();

                case "removedata":
                case "removedatatorrent":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.DeleteAsync(t.Id, true);
                        }
                    }

                    return BuildTorrentListResponse();

                case "recheck":
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await _torrentService.ForceRecheckAsync(t.Id);
                        }
                    }

                    return BuildTorrentListResponse();

                case "setprops":
                    if (!string.IsNullOrWhiteSpace(hash) && string.Equals(s, "label", StringComparison.OrdinalIgnoreCase))
                    {
                        var t = _torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            t.Category = v ?? string.Empty;
                            await _torrentService.UpdateAsync(t);
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
            var statusFlag = 201;
            if (t.Status == TorrentStatus.Paused)
            {
                statusFlag = 136;
            }
            else if (t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding)
            {
                statusFlag = 201;
            }
            else if (t.Status == TorrentStatus.Error)
            {
                statusFlag = 144;
            }

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
                0,
                t.Category ?? string.Empty,
                t.Leechers,
                t.Leechers,
                t.Seeders,
                t.Seeders,
                0,
                0,
                t.Downloaded,
                t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
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
