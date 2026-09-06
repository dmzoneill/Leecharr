// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
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

[ApiController]
public class NzbgetRpcController : ControllerBase
{
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IDiskProvider diskProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly ITorrentFileService torrentFileService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public NzbgetRpcController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        IDiskProvider diskProvider = null,
        ISafeHttpClientService safeHttpClientService = null,
        ITorrentFileService torrentFileService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.diskProvider = diskProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.torrentFileService = torrentFileService;
    }

    [HttpGet]
    [HttpPost]
    [Route("nzbget/jsonrpc")]
    [Route("nzbget")]
    [Route("{user}:{pass}/jsonrpc")]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NzbgetRequest request = null)
    {
        var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
        if (!isAuth && this.RouteData?.Values != null && !string.IsNullOrWhiteSpace(this.configFileProvider?.ApiKey))
        {
            var pass = this.RouteData.Values["pass"]?.ToString();
            var user = this.RouteData.Values["user"]?.ToString();
            if (RpcAuthenticationHelper.FixedTimeEquals(pass, this.configFileProvider.ApiKey) ||
                RpcAuthenticationHelper.FixedTimeEquals(user, this.configFileProvider.ApiKey))
            {
                isAuth = true;
            }
        }

        if (!isAuth)
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"NZBGet\"";
            return this.Unauthorized();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return this.Ok(new { version = "1.1", result = "24.0", id = (object)1 });
        }

        var id = request.Id ?? 1;

        try
        {
            switch (request.Method.ToLowerInvariant())
            {
                case "version":
                    return this.Ok(new { version = "1.1", result = "24.0", id });

                case "config":
                case "loadconfig":
                    return this.Ok(new
                    {
                        version = "1.1",
                        result = this.GetConfigItems(),
                        id,
                    });

                case "status":
                    return this.Ok(new
                    {
                        version = "1.1",
                        result = this.GetStatus(),
                        id,
                    });

                case "listgroups":
                    return this.Ok(new { version = "1.1", result = this.GetListGroups(), id });

                case "history":
                    return this.Ok(new { version = "1.1", result = this.GetHistory(), id });

                case "append":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var nzbName = request.Params[0].GetString();
                        var nzbContent = request.Params[1].GetString();
                        var category = request.Params.GetArrayLength() > 2 ? request.Params[2].GetString() : null;
                        var isPaused = false;
                        if (request.Params.GetArrayLength() > 5)
                        {
                            var pausedElem = request.Params[5];
                            if (pausedElem.ValueKind == JsonValueKind.True)
                            {
                                isPaused = true;
                            }
                            else if (pausedElem.ValueKind == JsonValueKind.Number)
                            {
                                isPaused = pausedElem.GetInt32() != 0;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(nzbContent))
                        {
                            try
                            {
                                var bytes = Convert.FromBase64String(nzbContent);
                                var parsed = this.torrentFileParser.Parse(bytes);
                                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, bytes);
                                return this.Ok(new { version = "1.1", result = added?.Id, id });
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Failed to parse and add torrent content in NZBGet append");
                                return this.Ok(new { version = "1.1", error = new { code = 1, message = ex.Message }, id });
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(nzbName))
                        {
                            if (nzbName.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var added = await this.torrentService.AddFromMagnetAsync(nzbName, category, null, isPaused);
                                    return this.Ok(new { version = "1.1", result = added?.Id, id });
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Error(ex, "Failed to add magnet in NZBGet append");
                                    return this.Ok(new { version = "1.1", error = new { code = 1, message = ex.Message }, id });
                                }
                            }

                            if (nzbName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                nzbName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var bytes = await this.safeHttpClientService.DownloadBytesAsync(nzbName, maxSizeBytes: 10 * 1024 * 1024);
                                    var parsed = this.torrentFileParser.Parse(bytes);
                                    var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, bytes);
                                    return this.Ok(new { version = "1.1", result = added?.Id, id });
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Error(ex, "Failed to download and add torrent from URL in NZBGet append: {0}", nzbName);
                                    return this.Ok(new { version = "1.1", error = new { code = 1, message = ex.Message }, id });
                                }
                            }
                        }
                    }

                    return this.Ok(new { version = "1.1", result = 1, id });

                case "pause":
                case "pauseall":
                case "pausedownload":
                case "pause-download":
                    foreach (var t in this.torrentService.GetAll())
                    {
                        await this.torrentService.PauseAsync(t.Id);
                    }

                    return this.Ok(new { version = "1.1", result = true, id });

                case "resume":
                case "resumeall":
                case "unpause":
                case "resumedownload":
                case "unpausedownload":
                case "unpause-download":
                    foreach (var t in this.torrentService.GetAll())
                    {
                        await this.torrentService.ResumeAsync(t.Id);
                    }

                    return this.Ok(new { version = "1.1", result = true, id });

                case "pausepost":
                case "pause-post":
                case "pause_post":
                    return this.Ok(new { version = "1.1", result = true, id });

                case "resumepost":
                case "resume-post":
                case "resume_post":
                case "unpausepost":
                case "unpause-post":
                    return this.Ok(new { version = "1.1", result = true, id });

                case "rate":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0)
                    {
                        var rate = request.Params[0].GetInt32();
                        this.configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxDownloadSpeedKbps"] = rate });
                    }

                    return this.Ok(new { version = "1.1", result = true, id });

                case "editqueue":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var command = request.Params[0].GetString()?.ToLowerInvariant();
                        var offset = 0;
                        var editText = string.Empty;
                        var targetIds = new List<int>();

                        if (request.Params.GetArrayLength() >= 4)
                        {
                            if (request.Params[1].ValueKind == JsonValueKind.Number)
                            {
                                offset = request.Params[1].GetInt32();
                            }

                            if (request.Params[2].ValueKind == JsonValueKind.String)
                            {
                                editText = request.Params[2].GetString() ?? string.Empty;
                            }

                            var idElem = request.Params[3];
                            if (idElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var elem in idElem.EnumerateArray())
                                {
                                    if (elem.TryGetInt32(out var parsedId))
                                    {
                                        targetIds.Add(parsedId);
                                    }
                                }
                            }
                            else if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var parsedId))
                            {
                                targetIds.Add(parsedId);
                            }
                        }
                        else
                        {
                            var idElem = request.Params[1];
                            if (idElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var elem in idElem.EnumerateArray())
                                {
                                    if (elem.TryGetInt32(out var parsedId))
                                    {
                                        targetIds.Add(parsedId);
                                    }
                                }
                            }
                            else if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt32(out var parsedId))
                            {
                                targetIds.Add(parsedId);
                            }
                        }

                        foreach (var targetId in targetIds)
                        {
                            if (command == "grouppause")
                            {
                                await this.torrentService.PauseAsync(targetId);
                            }
                            else if (command == "groupresume")
                            {
                                await this.torrentService.ResumeAsync(targetId);
                            }
                            else if (command == "groupdelete" || command == "historydelete")
                            {
                                await this.torrentService.DeleteAsync(targetId, false);
                            }
                            else if (command == "groupfinaldelete" || command == "historyfinaldelete")
                            {
                                await this.torrentService.DeleteAsync(targetId, true);
                            }
                            else if (command == "groupmovetop")
                            {
                                await this.torrentService.MoveQueueAsync(targetId, "top");
                            }
                            else if (command == "groupmoveup")
                            {
                                await this.torrentService.MoveQueueAsync(targetId, "up");
                            }
                            else if (command == "groupmovedown")
                            {
                                await this.torrentService.MoveQueueAsync(targetId, "down");
                            }
                            else if (command == "groupmovebottom")
                            {
                                await this.torrentService.MoveQueueAsync(targetId, "bottom");
                            }
                            else if (command == "groupmoveoffset")
                            {
                                await this.torrentService.MoveQueueAsync(targetId, offset > 0 ? "down" : "up");
                            }
                            else if (command == "groupsetcategory")
                            {
                                var t = this.torrentService.Get(targetId);
                                if (t != null && !string.IsNullOrWhiteSpace(editText))
                                {
                                    t.Category = editText;
                                    await this.torrentService.UpdateAsync(t);
                                }
                            }
                        }
                    }

                    return this.Ok(new { version = "1.1", result = true, id });

                default:
                    this.logger.Debug("Unhandled NZBGet method: {0}", request.Method);
                    return this.Ok(new { version = "1.1", result = true, id });
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error in NZBGet RPC: {0}", request.Method);
            return this.Ok(new { version = "1.1", error = new { code = 1, message = ex.Message }, id });
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("nzbget/xmlrpc")]
    [Route("{user}:{pass}/xmlrpc")]
    [Route("xmlrpc")]
    public async Task<IActionResult> HandleXmlRpc()
    {
        var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
        if (!isAuth && this.RouteData?.Values != null && !string.IsNullOrWhiteSpace(this.configFileProvider?.ApiKey))
        {
            var pass = this.RouteData.Values["pass"]?.ToString();
            var user = this.RouteData.Values["user"]?.ToString();
            if (RpcAuthenticationHelper.FixedTimeEquals(pass, this.configFileProvider.ApiKey) ||
                RpcAuthenticationHelper.FixedTimeEquals(user, this.configFileProvider.ApiKey))
            {
                isAuth = true;
            }
        }

        if (!isAuth)
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"NZBGet\"";
            return this.Unauthorized();
        }

        string requestBody;
        using (var reader = new StreamReader(this.Request.Body, Encoding.UTF8))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return this.BuildXmlRpcResponse(new XElement("string", "24.0"));
        }

        try
        {
            var doc = XDocument.Parse(requestBody);
            var methodName = doc.Root?.Element("methodName")?.Value ?? string.Empty;
            var paramsElement = doc.Root?.Element("params");
            var paramValues = ExtractParamValues(paramsElement);

            var result = await this.ExecuteXmlRpcMethodAsync(methodName, paramValues);
            return this.BuildXmlRpcResponse(result);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error processing NZBGet XML-RPC request");
            return this.BuildXmlRpcFault(1, ex.Message);
        }
    }

    private async Task<XElement> ExecuteXmlRpcMethodAsync(string methodName, List<object> paramValues)
    {
        switch (methodName.ToLowerInvariant())
        {
            case "version":
                return ToXmlRpcValue("24.0");

            case "config":
            case "loadconfig":
                return ToXmlRpcValue(this.GetConfigItems());

            case "status":
                return ToXmlRpcValue(this.GetStatus());

            case "listgroups":
                return ToXmlRpcValue(this.GetListGroups());

            case "history":
                return ToXmlRpcValue(this.GetHistory());

            case "append":
                var appendResult = await this.ExecuteAppendAsync(paramValues);
                return ToXmlRpcValue(appendResult);

            case "pause":
            case "pauseall":
            case "pausedownload":
            case "pause-download":
                foreach (var t in this.torrentService.GetAll())
                {
                    await this.torrentService.PauseAsync(t.Id);
                }

                return ToXmlRpcValue(true);

            case "resume":
            case "resumeall":
            case "unpause":
            case "resumedownload":
            case "unpausedownload":
            case "unpause-download":
                foreach (var t in this.torrentService.GetAll())
                {
                    await this.torrentService.ResumeAsync(t.Id);
                }

                return ToXmlRpcValue(true);

            case "pausepost":
            case "pause-post":
            case "pause_post":
                return ToXmlRpcValue(true);

            case "resumepost":
            case "resume-post":
            case "resume_post":
            case "unpausepost":
            case "unpause-post":
                return ToXmlRpcValue(true);

            case "rate":
                if (paramValues.Count > 0)
                {
                    var rate = paramValues[0] is int r ? r : (int.TryParse(paramValues[0]?.ToString(), out var parsedRate) ? parsedRate : 0);
                    this.configService.SaveConfigDictionary(new Dictionary<string, object> { ["MaxDownloadSpeedKbps"] = rate });
                }

                return ToXmlRpcValue(true);

            case "editqueue":
                await this.ExecuteEditQueueAsync(paramValues);
                return ToXmlRpcValue(true);

            case "listfiles":
                var nzbId = paramValues.Count > 0 ? (paramValues[0] is int id ? id : (int.TryParse(paramValues[0]?.ToString(), out var pid) ? pid : 0)) : 0;
                if (this.torrentFileService != null && nzbId > 0)
                {
                    var files = this.torrentFileService.GetFiles(nzbId)?.ToList();
                    if (files != null && files.Count > 0)
                    {
                        var mappedFiles = files.Select(f => (object)new
                        {
                            ID = f.Id,
                            NZBID = nzbId,
                            FileName = f.Path ?? string.Empty,
                            FileSizeLo = (int)(f.Size & 0xFFFFFFFF),
                            FileSizeHi = (int)(f.Size >> 32),
                            RemainingSizeLo = (int)((f.Size - f.BytesCompleted) & 0xFFFFFFFF),
                            RemainingSizeHi = (int)((f.Size - f.BytesCompleted) >> 32),
                            Progress = (int)(f.Progress * 1000),
                            Status = "FINISHED",
                        }).ToList();

                        return ToXmlRpcValue(mappedFiles);
                    }
                }

                return ToXmlRpcValue(Array.Empty<object>());

            case "shutdown":
            case "reload":
            case "restart":
            case "writelog":
                return ToXmlRpcValue(true);

            case "log":
                return ToXmlRpcValue(Array.Empty<object>());

            default:
                this.logger.Debug("Unhandled NZBGet XML-RPC method: {0}", methodName);
                return ToXmlRpcValue(true);
        }
    }

    private async Task<int> ExecuteAppendAsync(List<object> paramValues)
    {
        if (paramValues.Count >= 2)
        {
            var nzbName = paramValues[0]?.ToString() ?? string.Empty;
            var rawContent = paramValues[1];
            var category = paramValues.Count > 2 ? paramValues[2]?.ToString() : null;
            var isPaused = false;
            if (paramValues.Count > 5)
            {
                if (paramValues[5] is bool b)
                {
                    isPaused = b;
                }
                else if (paramValues[5] is int pInt)
                {
                    isPaused = pInt != 0;
                }
                else if (paramValues[5] is string pStr && bool.TryParse(pStr, out var pb))
                {
                    isPaused = pb;
                }
            }

            if (rawContent is byte[] bytes && bytes.Length > 0)
            {
                var parsed = this.torrentFileParser.Parse(bytes);
                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, bytes);
                return added?.Id ?? 1;
            }

            if (rawContent is string contentStr && !string.IsNullOrWhiteSpace(contentStr))
            {
                var contentBytes = Convert.FromBase64String(contentStr);
                var parsed = this.torrentFileParser.Parse(contentBytes);
                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, contentBytes);
                return added?.Id ?? 1;
            }

            if (!string.IsNullOrWhiteSpace(nzbName))
            {
                if (nzbName.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var added = await this.torrentService.AddFromMagnetAsync(nzbName, category, null, isPaused);
                    return added?.Id ?? 1;
                }

                if (nzbName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    nzbName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var urlBytes = await this.safeHttpClientService.DownloadBytesAsync(nzbName, maxSizeBytes: 10 * 1024 * 1024);
                    var parsed = this.torrentFileParser.Parse(urlBytes);
                    var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, null, isPaused, urlBytes);
                    return added?.Id ?? 1;
                }
            }
        }

        return 1;
    }

    private async Task ExecuteEditQueueAsync(List<object> paramValues)
    {
        if (paramValues.Count < 2)
        {
            return;
        }

        var command = paramValues[0]?.ToString()?.ToLowerInvariant();
        var offset = 0;
        var editText = string.Empty;
        var targetIds = new List<int>();

        if (paramValues.Count >= 4)
        {
            if (paramValues[1] is int off)
            {
                offset = off;
            }
            else if (int.TryParse(paramValues[1]?.ToString(), out var parsedOff))
            {
                offset = parsedOff;
            }

            editText = paramValues[2]?.ToString() ?? string.Empty;

            var idArg = paramValues[3];
            ExtractIds(idArg, targetIds);
        }
        else
        {
            var idArg = paramValues[1];
            ExtractIds(idArg, targetIds);
        }

        foreach (var targetId in targetIds)
        {
            if (command == "grouppause")
            {
                await this.torrentService.PauseAsync(targetId);
            }
            else if (command == "groupresume")
            {
                await this.torrentService.ResumeAsync(targetId);
            }
            else if (command == "groupdelete" || command == "historydelete")
            {
                await this.torrentService.DeleteAsync(targetId, false);
            }
            else if (command == "groupfinaldelete" || command == "historyfinaldelete")
            {
                await this.torrentService.DeleteAsync(targetId, true);
            }
            else if (command == "groupmovetop")
            {
                await this.torrentService.MoveQueueAsync(targetId, "top");
            }
            else if (command == "groupmoveup")
            {
                await this.torrentService.MoveQueueAsync(targetId, "up");
            }
            else if (command == "groupmovedown")
            {
                await this.torrentService.MoveQueueAsync(targetId, "down");
            }
            else if (command == "groupmovebottom")
            {
                await this.torrentService.MoveQueueAsync(targetId, "bottom");
            }
            else if (command == "groupmoveoffset")
            {
                await this.torrentService.MoveQueueAsync(targetId, offset > 0 ? "down" : "up");
            }
            else if (command == "groupsetcategory")
            {
                var t = this.torrentService.Get(targetId);
                if (t != null && !string.IsNullOrWhiteSpace(editText))
                {
                    t.Category = editText;
                    await this.torrentService.UpdateAsync(t);
                }
            }
        }
    }

    private static void ExtractIds(object idArg, List<int> targetIds)
    {
        if (idArg is List<object> list)
        {
            foreach (var elem in list)
            {
                if (elem is int i)
                {
                    targetIds.Add(i);
                }
                else if (int.TryParse(elem?.ToString(), out var pi))
                {
                    targetIds.Add(pi);
                }
            }
        }
        else if (idArg is int singleId)
        {
            targetIds.Add(singleId);
        }
        else if (idArg != null && int.TryParse(idArg.ToString(), out var parsedId))
        {
            targetIds.Add(parsedId);
        }
    }

    private List<object> GetConfigItems()
    {
        var configItems = new List<object>
        {
            new { Name = "MainDir", Value = this.configService.DownloadDir ?? "/downloads" },
            new { Name = "DestDir", Value = this.configService.DownloadDir ?? "/downloads" },
            new { Name = "InterDir", Value = this.configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
            new { Name = "NzbDir", Value = this.configService.DownloadDir ?? "/downloads" },
            new { Name = "QueueDir", Value = this.configService.DownloadDir ?? "/downloads" },
            new { Name = "TempDir", Value = this.configService.IncompleteDownloadDir ?? "/downloads/incomplete" },
        };

        var nzbgetCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tv", "tv-sonarr", "movies", "music", "anime", "default" };
        foreach (var c in this.categoryService.GetAll())
        {
            nzbgetCats.Add(c.Name);
        }

        var catIndex = 1;
        foreach (var cat in nzbgetCats)
        {
            configItems.Add(new { Name = $"Category{catIndex}.Name", Value = cat });
            configItems.Add(new { Name = $"Category{catIndex}.DestDir", Value = global::System.IO.Path.Combine(this.configService.DownloadDir ?? "/downloads", cat) });
            catIndex++;
        }

        return configItems;
    }

    private object GetStatus()
    {
        var all = this.torrentService.GetAll().ToList();
        var freeMb = (int)(this.GetDriveFreeSpace(this.configService.DownloadDir) / (1024 * 1024));
        return new
        {
            RemainingSizeMB = (int)(all.Sum(t => t.TotalSize - t.Downloaded) / (1024 * 1024)),
            DownloadRate = (int)all.Sum(t => t.DownloadSpeed),
            DownloadLimit = this.configService.MaxDownloadSpeedKbps * 1024,
            SpeedLimit = this.configService.MaxDownloadSpeedKbps * 1024,
            FreeDiskSpaceMB = freeMb,
            DownloadPaused = false,
            ServerPaused = false,
            ServerStandBy = false,
            PostJobCount = 0,
            ParJobCount = 0,
            DownloadTimeSec = 0,
            ServerTime = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
            ResumeTime = 0,
            FeedActive = false,
            QueueScriptCount = 0,
        };
    }

    private static bool IsComplete(Torrent t) =>
        t.Progress >= 1.0 ||
        (t.TotalSize > 0 && t.Downloaded >= t.TotalSize) ||
        t.Status == TorrentStatus.Completed ||
        t.Status == TorrentStatus.Seeding;

    private List<object> GetListGroups()
    {
        return this.torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Downloading ||
                        t.Status == TorrentStatus.Queued ||
                        t.Status == TorrentStatus.Paused ||
                        (t.Status == TorrentStatus.Stopped && !IsComplete(t)))
            .Select(t =>
            {
                var totalMb = (int)(t.TotalSize / (1024 * 1024));
                var remMb = (int)((t.TotalSize - t.Downloaded) / (1024 * 1024));
                var totalLo = (int)(t.TotalSize & 0xFFFFFFFF);
                var totalHi = (int)(t.TotalSize >> 32);
                var remBytes = Math.Max(0, t.TotalSize - t.Downloaded);
                var remLo = (int)(remBytes & 0xFFFFFFFF);
                var remHi = (int)(remBytes >> 32);

                return (object)new
                {
                    NZBID = t.Id,
                    NZBName = t.Name ?? string.Empty,
                    NZBNicename = t.Name ?? string.Empty,
                    Kind = "NZB",
                    URL = string.Empty,
                    DestDir = t.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
                    Category = t.Category ?? string.Empty,
                    FileSizeMB = totalMb,
                    RemainingSizeMB = remMb,
                    PausedSizeMB = 0,
                    FileSizeLo = totalLo,
                    FileSizeHi = totalHi,
                    RemainingSizeLo = remLo,
                    RemainingSizeHi = remHi,
                    PausedSizeLo = 0,
                    PausedSizeHi = 0,
                    FileCount = 1,
                    RemainingFileCount = 1,
                    PausedFileCount = 0,
                    Status = (t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped) ? "PAUSED" : "DOWNLOADING",
                    ActiveDownloads = 1,
                    Parameters = Array.Empty<object>(),
                };
            }).ToList();
    }

    private List<object> GetHistory()
    {
        return this.torrentService.GetAll()
            .Where(t => (t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Completed) && IsComplete(t))
            .Select(t =>
            {
                var totalMb = (int)(t.TotalSize / (1024 * 1024));
                var totalLo = (int)(t.TotalSize & 0xFFFFFFFF);
                var totalHi = (int)(t.TotalSize >> 32);

                return (object)new
                {
                    NZBID = t.Id,
                    Name = t.Name ?? string.Empty,
                    NZBName = t.Name ?? string.Empty,
                    NZBNicename = t.Name ?? string.Empty,
                    DestDir = t.SavePath ?? (this.configService.DownloadDir ?? "/downloads"),
                    Category = t.Category ?? string.Empty,
                    FileSizeMB = totalMb,
                    FileSizeLo = totalLo,
                    FileSizeHi = totalHi,
                    Status = "SUCCESS/ALL",
                    ParStatus = "SUCCESS",
                    UnpackStatus = "SUCCESS",
                    MoveStatus = "SUCCESS",
                    ScriptStatus = "NONE",
                    DeleteStatus = "NONE",
                    MarkStatus = "NONE",
                    UrlStatus = "NONE",
                    Parameters = Array.Empty<object>(),
                };
            }).ToList();
    }

    private static List<object> ExtractParamValues(XElement paramsElement)
    {
        var result = new List<object>();
        if (paramsElement == null)
        {
            return result;
        }

        foreach (var param in paramsElement.Elements("param"))
        {
            var valueElement = param.Element("value");
            if (valueElement != null)
            {
                result.Add(ParseXmlRpcValue(valueElement));
            }
        }

        return result;
    }

    private static object ParseXmlRpcValue(XElement valueElement)
    {
        var first = valueElement.Elements().FirstOrDefault();
        if (first == null)
        {
            return valueElement.Value;
        }

        switch (first.Name.LocalName.ToLowerInvariant())
        {
            case "string":
                return first.Value;
            case "int":
            case "i4":
                return int.TryParse(first.Value, out var i) ? i : 0;
            case "i8":
                return long.TryParse(first.Value, out var l) ? l : 0L;
            case "double":
                return double.TryParse(first.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
            case "boolean":
                return first.Value == "1" || first.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            case "base64":
                return Convert.FromBase64String(first.Value);
            case "struct":
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var member in first.Elements("member"))
                {
                    var name = member.Element("name")?.Value?.Trim();
                    var valElem = member.Element("value");
                    if (!string.IsNullOrEmpty(name) && valElem != null)
                    {
                        dict[name] = ParseXmlRpcValue(valElem);
                    }
                }

                return dict;
            case "array":
                var list = new List<object>();
                var data = first.Element("data");
                if (data != null)
                {
                    foreach (var item in data.Elements("value"))
                    {
                        list.Add(ParseXmlRpcValue(item));
                    }
                }

                return list;
            default:
                return first.Value;
        }
    }

    private static XElement ToXmlRpcValue(object value)
    {
        if (value == null)
        {
            return new XElement("string", string.Empty);
        }

        if (value is XElement elem)
        {
            return elem;
        }

        if (value is bool b)
        {
            return new XElement("boolean", b ? "1" : "0");
        }

        if (value is byte or sbyte or short or ushort or int)
        {
            return new XElement("int", value.ToString());
        }

        if (value is uint or long or ulong)
        {
            return new XElement("i8", value.ToString());
        }

        if (value is float or double or decimal)
        {
            return new XElement("double", Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        if (value is string s)
        {
            return new XElement("string", s);
        }

        if (value is byte[] bytes)
        {
            return new XElement("base64", Convert.ToBase64String(bytes));
        }

        if (value is DateTime dt)
        {
            return new XElement("dateTime.iso8601", dt.ToString("yyyyMMdd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }

        if (value is DateTimeOffset dto)
        {
            return new XElement("dateTime.iso8601", dto.UtcDateTime.ToString("yyyyMMdd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }

        if (value is IDictionary<string, object> dict)
        {
            var structElem = new XElement("struct");
            foreach (var kvp in dict)
            {
                structElem.Add(new XElement(
                    "member",
                    new XElement("name", kvp.Key),
                    new XElement("value", ToXmlRpcValue(kvp.Value))));
            }

            return structElem;
        }

        if (value is IDictionary idict)
        {
            var structElem = new XElement("struct");
            foreach (DictionaryEntry entry in idict)
            {
                structElem.Add(new XElement(
                    "member",
                    new XElement("name", entry.Key?.ToString() ?? string.Empty),
                    new XElement("value", ToXmlRpcValue(entry.Value))));
            }

            return structElem;
        }

        if (value is IEnumerable enumerable)
        {
            var dataElem = new XElement("data");
            foreach (var item in enumerable)
            {
                dataElem.Add(new XElement("value", ToXmlRpcValue(item)));
            }

            return new XElement("array", dataElem);
        }

        var type = value.GetType();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (props.Length > 0)
        {
            var structElem = new XElement("struct");
            foreach (var prop in props)
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                var propVal = prop.GetValue(value);
                structElem.Add(new XElement(
                    "member",
                    new XElement("name", prop.Name),
                    new XElement("value", ToXmlRpcValue(propVal))));
            }

            return structElem;
        }

        return new XElement("string", value.ToString() ?? string.Empty);
    }

    private IActionResult BuildXmlRpcResponse(XElement valueContent)
    {
        var doc = new XDocument(
            new XElement(
                "methodResponse",
                new XElement(
                    "params",
                    new XElement(
                        "param",
                        new XElement("value", valueContent)))));

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\"?>");
        builder.Append(doc.ToString(SaveOptions.DisableFormatting));

        return this.Content(builder.ToString(), "text/xml; charset=utf-8", Encoding.UTF8);
    }

    private IActionResult BuildXmlRpcFault(int faultCode, string faultString)
    {
        var doc = new XDocument(
            new XElement(
                "methodResponse",
                new XElement(
                    "fault",
                    new XElement(
                        "value",
                        new XElement(
                            "struct",
                            new XElement(
                                "member",
                                new XElement("name", "faultCode"),
                                new XElement("value", new XElement("int", faultCode))),
                            new XElement(
                                "member",
                                new XElement("name", "faultString"),
                                new XElement("value", new XElement("string", faultString ?? string.Empty))))))));

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\"?>");
        builder.Append(doc.ToString(SaveOptions.DisableFormatting));

        return this.Content(builder.ToString(), "text/xml; charset=utf-8", Encoding.UTF8);
    }

    private long GetDriveFreeSpace(string path)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(path) ? "/downloads" : path;
            var fullPath = global::System.IO.Path.GetFullPath(target);
            return this.diskProvider?.GetAvailableSpace(fullPath) ?? 1099511627776L;
        }
        catch
        {
            return 1099511627776L;
        }
    }
}
