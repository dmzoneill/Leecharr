using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Nzbget;

public class NzbgetRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;
}

[AllowAnonymous]
[ApiController]
public class NzbgetRpcController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NzbgetRpcController(
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
    [HttpPost]
    [Route("nzbget/jsonrpc")]
    [Route("nzbget/xmlrpc")]
    [Route("nzbget")]
    [Route("{user}:{pass}/jsonrpc")]
    [Route("{user}:{pass}/xmlrpc")]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NzbgetRequest request = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(new { version = "1.1", result = "24.0", id = (object)1 });
        }

        var id = request.Id ?? 1;

        try
        {
            switch (request.Method.ToLowerInvariant())
            {
                case "version":
                    return Ok(new { version = "1.1", result = "24.0", id });

                case "config":
                case "loadconfig":
                    var configItems = new List<object>
                    {
                        new { Name = "MainDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "DestDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "InterDir", Value = _configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
                        new { Name = "NzbDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "QueueDir", Value = _configService.DownloadDir ?? "/downloads" },
                        new { Name = "TempDir", Value = _configService.IncompleteDownloadDir ?? "/downloads/incomplete" }
                    };

                    var nzbgetCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tv", "tv-sonarr", "movies", "music", "anime", "default" };
                    foreach (var c in _categoryService.GetAll())
                    {
                        nzbgetCats.Add(c.Name);
                    }

                    var catIndex = 1;
                    foreach (var cat in nzbgetCats)
                    {
                        configItems.Add(new { Name = $"Category{catIndex}.Name", Value = cat });
                        configItems.Add(new { Name = $"Category{catIndex}.DestDir", Value = global::System.IO.Path.Combine(_configService.DownloadDir ?? "/downloads", cat) });
                        catIndex++;
                    }

                    return Ok(new
                    {
                        version = "1.1",
                        result = configItems,
                        id
                    });

                case "status":
                    var all = _torrentService.GetAll().ToList();
                    return Ok(new
                    {
                        version = "1.1",
                        result = new
                        {
                            RemainingSizeMB = (int)(all.Sum(t => t.TotalSize - t.Downloaded) / (1024 * 1024)),
                            DownloadRate = (int)all.Sum(t => t.DownloadSpeed),
                            DownloadLimit = _configService.MaxDownloadSpeedKbps * 1024,
                            DownloadPaused = false,
                            ServerPaused = false,
                            ServerStandBy = false,
                            PostJobCount = 0,
                            ParJobCount = 0,
                            DownloadTimeSec = 0,
                            ServerTime = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
                            ResumeTime = 0,
                            FeedActive = false,
                            QueueScriptCount = 0
                        },
                        id
                    });

                case "listgroups":
                    var queueTorrents = _torrentService.GetAll()
                        .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Queued || t.Status == TorrentStatus.Paused)
                        .Select((t, index) => new
                        {
                            NZBID = t.Id,
                            NZBName = t.Name ?? string.Empty,
                            NZBNicename = t.Name ?? string.Empty,
                            Kind = "NZB",
                            URL = string.Empty,
                            DestDir = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                            Category = t.Category ?? string.Empty,
                            FileSizeMB = (int)(t.TotalSize / (1024 * 1024)),
                            RemainingSizeMB = (int)((t.TotalSize - t.Downloaded) / (1024 * 1024)),
                            PausedSizeMB = 0,
                            FileCount = 1,
                            RemainingFileCount = 1,
                            PausedFileCount = 0,
                            Status = t.Status == TorrentStatus.Paused ? "PAUSED" : "DOWNLOADING",
                            ActiveDownloads = 1
                        }).ToList();

                    return Ok(new { version = "1.1", result = queueTorrents, id });

                case "history":
                    var finished = _torrentService.GetAll()
                        .Where(t => t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding)
                        .Select(t => new
                        {
                            NZBID = t.Id,
                            Name = t.Name ?? string.Empty,
                            DestDir = t.SavePath ?? (_configService.DownloadDir ?? "/downloads"),
                            Category = t.Category ?? string.Empty,
                            FileSizeMB = (int)(t.TotalSize / (1024 * 1024)),
                            Status = "SUCCESS/ALL",
                            ParStatus = "SUCCESS",
                            UnpackStatus = "SUCCESS",
                            MoveStatus = "SUCCESS",
                            ScriptStatus = "NONE",
                            DeleteStatus = "NONE",
                            MarkStatus = "NONE",
                            UrlStatus = "NONE"
                        }).ToList();

                    return Ok(new { version = "1.1", result = finished, id });

                case "append":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 3)
                    {
                        var nzbName = request.Params[0].GetString();
                        var nzbContent = request.Params[1].GetString();
                        var category = request.Params[2].GetString();

                        if (!string.IsNullOrWhiteSpace(nzbContent))
                        {
                            try
                            {
                                var bytes = Convert.FromBase64String(nzbContent);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, category, null, false, bytes);
                                return Ok(new { version = "1.1", result = added?.Id ?? 1, id });
                            }
                            catch
                            {
                                return Ok(new { version = "1.1", result = 1, id });
                            }
                        }
                    }

                    return Ok(new { version = "1.1", result = 1, id });

                case "editqueue":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var command = request.Params[0].GetString()?.ToLowerInvariant();
                        var paramId = request.Params[1].GetInt32();

                        if (command == "grouppause")
                        {
                            await _torrentService.PauseAsync(paramId);
                        }
                        else if (command == "groupresume")
                        {
                            await _torrentService.ResumeAsync(paramId);
                        }
                        else if (command == "groupdelete")
                        {
                            await _torrentService.DeleteAsync(paramId, false);
                        }
                    }

                    return Ok(new { version = "1.1", result = true, id });

                default:
                    _logger.Debug("Unhandled NZBGet method: {0}", request.Method);
                    return Ok(new { version = "1.1", result = true, id });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in NZBGet RPC: {0}", request.Method);
            return Ok(new { version = "1.1", error = new { code = 1, message = ex.Message }, id });
        }
    }
}
