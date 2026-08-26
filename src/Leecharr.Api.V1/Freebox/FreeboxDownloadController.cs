using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Freebox;

[AllowAnonymous]
[ApiController]
public class FreeboxDownloadController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public FreeboxDownloadController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _configService = configService;
    }

    [HttpGet]
    [Route("api/v4/login/authorize")]
    public IActionResult LoginAuthorize()
    {
        return Ok(new
        {
            success = true,
            result = new
            {
                logged_in = true,
                challenge = "freebox-challenge-token"
            }
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("api/v4/login/session")]
    [Route("api/v4/login")]
    public IActionResult LoginSession()
    {
        return Ok(new
        {
            success = true,
            result = new
            {
                session_token = "freebox-session-token",
                logged_in = true,
                permissions = new { downloader = true }
            }
        });
    }

    [HttpGet]
    [Route("api/v4/downloads")]
    public IActionResult GetDownloads()
    {
        var all = _torrentService.GetAll().ToList();
        var list = all.Select(t => new
        {
            id = t.Id,
            name = t.Name ?? string.Empty,
            size = t.TotalSize,
            status = t.Status switch
            {
                TorrentStatus.Downloading => "downloading",
                TorrentStatus.Seeding => "seeding",
                TorrentStatus.Paused => "stopped",
                TorrentStatus.Stopped => "done",
                TorrentStatus.Error => "error",
                _ => "waiting"
            },
            rx_bytes = t.Downloaded,
            tx_bytes = t.Uploaded,
            rx_rate = t.DownloadSpeed,
            tx_rate = t.UploadSpeed,
            eta = t.Eta,
            io_priority = "normal",
            download_dir = t.SavePath ?? (_configService.DownloadDir ?? "/downloads")
        }).ToList();

        return Ok(new
        {
            success = true,
            result = list
        });
    }

    [HttpPost]
    [Route("api/v4/downloads/add")]
    public async Task<IActionResult> AddDownload()
    {
        var addedId = 1;
        if (Request.HasFormContentType)
        {
            var downloadUrl = Request.Form["download_url"].ToString();
            var downloadDir = Request.Form["download_dir"].ToString();
            if (string.IsNullOrWhiteSpace(downloadDir))
            {
                downloadDir = null;
            }

            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                if (downloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await _torrentService.AddFromMagnetAsync(downloadUrl, null, downloadDir, false);
                    addedId = added?.Id ?? addedId;
                }
                else
                {
                    using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    var bytes = await client.GetByteArrayAsync(downloadUrl);
                    var parsed = _torrentFileParser.Parse(bytes);
                    var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, downloadDir, false, bytes);
                    addedId = added?.Id ?? addedId;
                }
            }
            else if (Request.Form.Files.Count > 0)
            {
                var file = Request.Form.Files[0];
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var parsed = _torrentFileParser.Parse(bytes);
                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, downloadDir, false, bytes);
                addedId = added?.Id ?? addedId;
            }
        }

        return Ok(new
        {
            success = true,
            result = new { id = addedId }
        });
    }

    [HttpDelete]
    [Route("api/v4/downloads/{id}")]
    public async Task<IActionResult> DeleteDownload(int id)
    {
        await _torrentService.DeleteAsync(id, false);
        return Ok(new { success = true });
    }

    [HttpPut]
    [Route("api/v4/downloads/{id}")]
    public async Task<IActionResult> UpdateDownload(int id)
    {
        if (Request.HasFormContentType)
        {
            var status = Request.Form["status"].ToString().ToLowerInvariant();
            if (status == "stopped")
            {
                await _torrentService.PauseAsync(id);
            }
            else if (status == "downloading")
            {
                await _torrentService.ResumeAsync(id);
            }
        }

        return Ok(new { success = true });
    }
}
