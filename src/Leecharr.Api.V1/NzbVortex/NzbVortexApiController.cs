using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.NzbVortex;

[AllowAnonymous]
[ApiController]
public class NzbVortexApiController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NzbVortexApiController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService)
    {
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _configService = configService;
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/auth/nonce")]
    public IActionResult GetNonce()
    {
        return Ok(new
        {
            error = 0,
            nonce = "leecharr-vortex-nonce"
        });
    }

    [HttpGet]
    [HttpPost]
    [Route("nzbvortex/api/v1/auth/login")]
    public IActionResult Login()
    {
        return Ok(new
        {
            error = 0,
            auth = true
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/queue")]
    public IActionResult GetQueue()
    {
        var all = _torrentService.GetAll().ToList();
        var queue = all
            .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Queued || t.Status == TorrentStatus.Paused)
            .Select(t => new
            {
                id = t.Id.ToString(),
                name = t.Name ?? string.Empty,
                totalSize = t.TotalSize,
                downloadedSize = t.Downloaded,
                speed = t.DownloadSpeed,
                state = t.Status == TorrentStatus.Paused ? "paused" : "downloading",
                group = t.Category ?? string.Empty
            }).ToList();

        return Ok(new
        {
            error = 0,
            queue
        });
    }

    [HttpGet]
    [Route("nzbvortex/api/v1/history")]
    public IActionResult GetHistory()
    {
        var all = _torrentService.GetAll().ToList();
        var history = all
            .Where(t => t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding)
            .Select(t => new
            {
                id = t.Id.ToString(),
                name = t.Name ?? string.Empty,
                totalSize = t.TotalSize,
                state = "completed",
                group = t.Category ?? string.Empty,
                destinationPath = t.SavePath ?? (_configService.DownloadDir ?? "/downloads")
            }).ToList();

        return Ok(new
        {
            error = 0,
            history
        });
    }

    [HttpPost]
    [Route("nzbvortex/api/v1/queue/add")]
    public async Task<IActionResult> AddQueue()
    {
        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            var file = Request.Form.Files[0];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var parsed = _torrentFileParser.Parse(bytes);
            var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, bytes);
            return Ok(new
            {
                error = 0,
                nzb = new { id = (added?.Id ?? 1).ToString() }
            });
        }

        return Ok(new
        {
            error = 0,
            nzb = new { id = "1" }
        });
    }

    [HttpDelete]
    [Route("nzbvortex/api/v1/queue/{id}")]
    public async Task<IActionResult> DeleteQueue(int id)
    {
        await _torrentService.DeleteAsync(id, false);
        return Ok(new { error = 0 });
    }
}
