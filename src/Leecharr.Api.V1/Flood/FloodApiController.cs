using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Flood;

public class FloodAddUrlsRequest
{
    public List<string> Urls { get; set; } = new();
    public string Destination { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool Start { get; set; } = true;
}

public class FloodAddFilesRequest
{
    public List<string> Files { get; set; } = new();
    public string Destination { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool Start { get; set; } = true;
}

public class FloodActionRequest
{
    public List<string> Hashes { get; set; } = new();
    public bool DeleteData { get; set; }
    public List<string> Tags { get; set; } = new();
}

[AllowAnonymous]
[ApiController]
public class FloodApiController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public FloodApiController(
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

    [HttpPost]
    [Route("api/auth/authenticate")]
    public IActionResult Authenticate()
    {
        Response.Cookies.Append("flood-auth", "leecharr-flood-token");
        return Ok(new { success = true });
    }

    [HttpGet]
    [Route("api/auth/verify")]
    public IActionResult Verify()
    {
        return Ok(new { isInitialUser = false, isAllowed = true });
    }

    [HttpGet]
    [Route("api/client/settings")]
    public IActionResult GetClientSettings()
    {
        return Ok(new
        {
            directoryDefault = _configService.DownloadDir ?? "/downloads"
        });
    }

    [HttpGet]
    [Route("api/torrents")]
    public IActionResult GetTorrents()
    {
        var torrents = _torrentService.GetAll().ToList();
        var dict = new Dictionary<string, object>();

        foreach (var t in torrents)
        {
            var hash = t.InfoHash.ToLowerInvariant();
            dict[hash] = new
            {
                hash = t.InfoHash,
                name = t.Name,
                bytesDone = t.Downloaded,
                sizeBytes = t.TotalSize,
                percentComplete = (t.Progress * 100).ToString("F1"),
                downRate = t.DownloadSpeed,
                upRate = t.UploadSpeed,
                ratio = t.Ratio,
                eta = t.Eta,
                status = new[] { MapToFloodStatus(t.Status) },
                tags = string.IsNullOrWhiteSpace(t.Category)
                    ? (string.IsNullOrWhiteSpace(t.Label) ? Array.Empty<string>() : new[] { t.Label })
                    : new[] { t.Category },
                directory = t.SavePath ?? string.Empty,
                isPrivate = false,
                isInitialSeeding = false,
                isSequential = t.SequentialDownload,
                seedsConnected = t.Seeders,
                seedsTotal = t.Seeders,
                peersConnected = t.Leechers,
                peersTotal = t.Leechers
            };
        }

        return Ok(new { torrents = dict });
    }

    private string MapToFloodStatus(TorrentStatus status)
    {
        return status switch
        {
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "seeding",
            TorrentStatus.Paused => "stopped",
            TorrentStatus.Stopped => "complete",
            _ => "inactive"
        };
    }

    [HttpPost]
    [Route("api/torrents/add-urls")]
    public async Task<IActionResult> AddUrls([FromBody] FloodAddUrlsRequest request)
    {
        if (request?.Urls != null)
        {
            var category = request.Tags?.FirstOrDefault();
            foreach (var url in request.Urls)
            {
                if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    await _torrentService.AddFromMagnetAsync(url, category, request.Destination, !request.Start);
                }
                else
                {
                    using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    var bytes = await client.GetByteArrayAsync(url);
                    var parsed = _torrentFileParser.Parse(bytes);
                    await _torrentService.AddFromParsedTorrentAsync(parsed, category, request.Destination, !request.Start, bytes);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/add-files")]
    public async Task<IActionResult> AddFiles([FromBody] FloodAddFilesRequest jsonRequest = null)
    {
        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            var destination = Request.Form["destination"].ToString();
            var tagsStr = Request.Form["tags"].ToString();
            var category = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            var start = !string.Equals(Request.Form["start"].ToString(), "false", StringComparison.OrdinalIgnoreCase);

            foreach (var file in Request.Form.Files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var parsed = _torrentFileParser.Parse(bytes);
                await _torrentService.AddFromParsedTorrentAsync(parsed, category, destination, !start, bytes);
            }
        }
        else if (jsonRequest?.Files != null && jsonRequest.Files.Count > 0)
        {
            var category = jsonRequest.Tags?.FirstOrDefault()?.Trim();
            foreach (var b64 in jsonRequest.Files)
            {
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    var bytes = Convert.FromBase64String(b64);
                    var parsed = _torrentFileParser.Parse(bytes);
                    await _torrentService.AddFromParsedTorrentAsync(parsed, category, jsonRequest.Destination, !jsonRequest.Start, bytes);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/start")]
    public async Task<IActionResult> StartTorrents([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = _torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await _torrentService.ResumeAsync(t.Id);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/stop")]
    public async Task<IActionResult> StopTorrents([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = _torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await _torrentService.PauseAsync(t.Id);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/delete")]
    public async Task<IActionResult> DeleteTorrents([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = _torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await _torrentService.DeleteAsync(t.Id, request.DeleteData);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpGet]
    [Route("api/torrents/tags")]
    public IActionResult GetTags()
    {
        var cats = _categoryService.GetAll().Select(c => c.Name).ToList();
        return Ok(cats);
    }

    [HttpPost]
    [HttpPatch]
    [Route("api/torrents/tags")]
    [Route("api/torrents/set-tags")]
    public async Task<IActionResult> SetTags([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            var newCategory = request.Tags?.FirstOrDefault() ?? string.Empty;
            foreach (var hash in request.Hashes)
            {
                var t = _torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    t.Category = newCategory;
                    t.Label = string.Join(",", request.Tags ?? new List<string>());
                    await _torrentService.UpdateAsync(t);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpPost]
    [Route("api/torrents/check-hash")]
    public async Task<IActionResult> CheckHash([FromBody] FloodActionRequest request)
    {
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                var t = _torrentService.GetByInfoHash(hash);
                if (t != null)
                {
                    await _torrentService.ForceRecheckAsync(t.Id);
                }
            }
        }

        return Ok(new { success = true });
    }

    [HttpGet]
    [Route("api/torrents/{hash}/contents")]
    public IActionResult GetContents([FromRoute] string hash)
    {
        var t = _torrentService.GetByInfoHash(hash);
        if (t == null)
        {
            return NotFound();
        }

        var files = _torrentFileService.GetFiles(t.Id);
        return Ok(files);
    }
}
