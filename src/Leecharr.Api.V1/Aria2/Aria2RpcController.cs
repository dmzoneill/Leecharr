using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Aria2;

public class Aria2RpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;
}

[AllowAnonymous]
[ApiController]
[Route("jsonrpc")]
[Route("rpc")]
[Route("aria2/jsonrpc")]
[Route("aria2/rpc")]
public class Aria2RpcController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public Aria2RpcController(
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
    [HttpPost]
    public async Task<IActionResult> HandleRpc()
    {
        string method = null;
        JsonElement paramsElem = default;
        object id = 1;

        if (HttpMethods.IsPost(Request.Method))
        {
            try
            {
                using var reader = new global::System.IO.StreamReader(Request.Body, global::System.Text.Encoding.UTF8);
                var rawBody = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    var trimmed = rawBody.TrimStart();
                    if (trimmed.StartsWith("<", StringComparison.Ordinal))
                    {
                        var xmlDoc = global::System.Xml.Linq.XDocument.Parse(rawBody);
                        var xmlMethodName = xmlDoc.Root?.Element("methodName")?.Value ?? string.Empty;
                        return await HandleXmlRpcAsync(xmlMethodName, xmlDoc);
                    }

                    using var doc = JsonDocument.Parse(rawBody);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("method", out var mElem))
                    {
                        method = mElem.GetString();
                    }

                    if (root.TryGetProperty("params", out var pElem))
                    {
                        paramsElem = pElem.Clone();
                    }

                    if (root.TryGetProperty("id", out var idElem))
                    {
                        if (idElem.ValueKind == JsonValueKind.String)
                        {
                            id = idElem.GetString();
                        }
                        else if (idElem.ValueKind == JsonValueKind.Number)
                        {
                            id = idElem.GetInt64();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not parse Aria2 payload");
            }
        }

        if (string.IsNullOrWhiteSpace(method))
        {
            method = Request.Query["method"].ToString();
            if (string.IsNullOrWhiteSpace(method))
            {
                return Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        version = "1.36.0",
                        enabledFeatures = new[] { "BitTorrent", "GZip", "HTTPS", "MessageDigest", "Async DNS" }
                    }
                });
            }
        }

        try
        {
            var res = await ExecuteMethodAsync(method, paramsElem);
            return Ok(new
            {
                jsonrpc = "2.0",
                id,
                result = res
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Aria2 RPC method: {0}", method);
            return Ok(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = 1, message = ex.Message }
            });
        }
    }

    private async Task<object> ExecuteMethodAsync(string method, JsonElement parameters)
    {
        var cleanParams = GetCleanParams(parameters);

        switch (method.ToLowerInvariant())
        {
            case "aria2.getversion":
                return new
                {
                    version = "1.36.0",
                    enabledFeatures = new[] { "BitTorrent", "GZip", "HTTPS", "MessageDigest", "Async DNS" }
                };

            case "aria2.getglobalstat":
                var allT = _torrentService.GetAll().ToList();
                return new
                {
                    downloadSpeed = allT.Sum(t => t.DownloadSpeed).ToString(),
                    uploadSpeed = allT.Sum(t => t.UploadSpeed).ToString(),
                    numActive = allT.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding).ToString(),
                    numWaiting = allT.Count(t => t.Status == TorrentStatus.Queued).ToString(),
                    numStopped = allT.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped).ToString()
                };

            case "aria2.tellactive":
                return _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding)
                    .Select(MapTorrentToAria2)
                    .ToList();

            case "aria2.tellwaiting":
                return _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Queued)
                    .Select(MapTorrentToAria2)
                    .ToList();

            case "aria2.tellstopped":
                return _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped)
                    .Select(MapTorrentToAria2)
                    .ToList();

            case "aria2.tellstatus":
                var gid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
                var torrent = FindByGid(gid);
                return torrent != null ? MapTorrentToAria2(torrent) : new object();

            case "aria2.addtorrent":
                if (cleanParams.Count > 0)
                {
                    var b64 = cleanParams[0].GetString();
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        var savePath = (string)null;
                        var isPaused = false;
                        if (cleanParams.Count > 1 && cleanParams[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = cleanParams[1];
                            if (opts.TryGetProperty("dir", out var dirProp))
                            {
                                savePath = dirProp.GetString();
                            }

                            if (opts.TryGetProperty("pause", out var pauseProp))
                            {
                                if (pauseProp.ValueKind == JsonValueKind.True || (pauseProp.ValueKind == JsonValueKind.String && pauseProp.GetString() == "true"))
                                {
                                    isPaused = true;
                                }
                            }
                        }
                        else if (cleanParams.Count > 2 && cleanParams[2].ValueKind == JsonValueKind.Object)
                        {
                            var opts = cleanParams[2];
                            if (opts.TryGetProperty("dir", out var dirProp))
                            {
                                savePath = dirProp.GetString();
                            }

                            if (opts.TryGetProperty("pause", out var pauseProp))
                            {
                                if (pauseProp.ValueKind == JsonValueKind.True || (pauseProp.ValueKind == JsonValueKind.String && pauseProp.GetString() == "true"))
                                {
                                    isPaused = true;
                                }
                            }
                        }

                        var bytes = Convert.FromBase64String(b64);
                        var parsed = _torrentFileParser.Parse(bytes);
                        var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, savePath, isPaused, bytes);
                        return added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16];
                    }
                }

                return Guid.NewGuid().ToString("N")[..16];

            case "aria2.adduri":
                if (cleanParams.Count > 0)
                {
                    var urisArray = cleanParams[0];
                    if (urisArray.ValueKind == JsonValueKind.Array && urisArray.GetArrayLength() > 0)
                    {
                        var uri = urisArray[0].GetString();
                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            var savePath = (string)null;
                            var isPaused = false;
                            if (cleanParams.Count > 1 && cleanParams[1].ValueKind == JsonValueKind.Object)
                            {
                                var opts = cleanParams[1];
                                if (opts.TryGetProperty("dir", out var dirProp))
                                {
                                    savePath = dirProp.GetString();
                                }

                                if (opts.TryGetProperty("pause", out var pauseProp))
                                {
                                    if (pauseProp.ValueKind == JsonValueKind.True || (pauseProp.ValueKind == JsonValueKind.String && pauseProp.GetString() == "true"))
                                    {
                                        isPaused = true;
                                    }
                                }
                            }

                            if (uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await _torrentService.AddFromMagnetAsync(uri, null, savePath, isPaused);
                                return added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16];
                            }
                            else
                            {
                                using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                                var bytes = await client.GetByteArrayAsync(uri);
                                var parsed = _torrentFileParser.Parse(bytes);
                                var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, savePath, isPaused, bytes);
                                return added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16];
                            }
                        }
                    }
                }

                return Guid.NewGuid().ToString("N")[..16];

            case "aria2.remove":
            case "aria2.forceremove":
            case "aria2.removedownloadresult":
                var removeGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
                var toRemove = FindByGid(removeGid);
                if (toRemove != null)
                {
                    await _torrentService.DeleteAsync(toRemove.Id, false);
                }

                return removeGid ?? "OK";

            case "aria2.pause":
            case "aria2.forcepause":
                var pauseGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
                var toPause = FindByGid(pauseGid);
                if (toPause != null)
                {
                    await _torrentService.PauseAsync(toPause.Id);
                }

                return pauseGid ?? "OK";

            case "aria2.unpause":
            case "aria2.forceunpause":
                var unpauseGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
                var toUnpause = FindByGid(unpauseGid);
                if (toUnpause != null)
                {
                    await _torrentService.ResumeAsync(toUnpause.Id);
                }

                return unpauseGid ?? "OK";

            case "aria2.getfiles":
                var filesGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
                var filesTorrent = FindByGid(filesGid);
                if (filesTorrent != null)
                {
                    var files = _torrentFileService.GetFiles(filesTorrent.Id);
                    return files.Select((f, idx) => new
                    {
                        index = (idx + 1).ToString(),
                        path = f.Path,
                        length = f.Size.ToString(),
                        completedLength = (filesTorrent.Progress * f.Size).ToString("F0"),
                        selected = "true"
                    }).ToList();
                }

                return Array.Empty<object>();

            case "aria2.getglobaloption":
            case "aria2.getoption":
                return new Dictionary<string, string>
                {
                    { "dir", _configService.DownloadDir ?? "/downloads" },
                    { "max-overall-download-limit", (_configService.MaxDownloadSpeedKbps * 1024).ToString() },
                    { "max-overall-upload-limit", (_configService.MaxUploadSpeedKbps * 1024).ToString() },
                    { "max-download-limit", "0" },
                    { "max-upload-limit", "0" }
                };

            case "aria2.changeposition":
                var cpParams = GetCleanParams(parameters);
                if (cpParams.Count >= 3)
                {
                    var cpGid = cpParams[0].GetString();
                    var cpOffset = cpParams[1].GetInt32();
                    var cpHow = cpParams[2].GetString()?.ToLowerInvariant();
                    var t = _torrentService.GetByInfoHash(cpGid);
                    if (t != null)
                    {
                        var dir = cpHow == "pos_set" && cpOffset == 0 ? "top" :
                                  cpHow == "pos_end" ? "bottom" :
                                  cpOffset < 0 ? "up" : "down";
                        await _torrentService.MoveQueueAsync(t.Id, dir);
                        return 1;
                    }
                }

                return 0;

            case "aria2.changeoption":
            case "aria2.changeglobaloption":
                var coParams = GetCleanParams(parameters);
                var optDictElem = coParams.FirstOrDefault(p => p.ValueKind == JsonValueKind.Object);
                var gidStr = coParams.FirstOrDefault(p => p.ValueKind == JsonValueKind.String).GetString();

                if (!string.IsNullOrWhiteSpace(gidStr) && _torrentService != null)
                {
                    var t = _torrentService.GetByInfoHash(gidStr);
                    if (t != null && optDictElem.ValueKind == JsonValueKind.Object)
                    {
                        if (optDictElem.TryGetProperty("max-download-limit", out var tdl) && int.TryParse(tdl.GetString(), out var tdlBps))
                        {
                            t.DownloadLimit = tdlBps / 1024;
                        }

                        if (optDictElem.TryGetProperty("max-upload-limit", out var tul) && int.TryParse(tul.GetString(), out var tulBps))
                        {
                            t.UploadLimit = tulBps / 1024;
                        }

                        await _torrentService.UpdateAsync(t);

                        if (optDictElem.TryGetProperty("select-file", out var sfElem) && _torrentFileService != null)
                        {
                            var sfStr = sfElem.GetString();
                            if (!string.IsNullOrWhiteSpace(sfStr))
                            {
                                var files = _torrentFileService.GetFiles(t.Id).ToList();
                                var selectedIndices = ParseAria2FileIndices(sfStr);
                                for (var fIdx = 0; fIdx < files.Count; fIdx++)
                                {
                                    // 1-based indices in aria2
                                    var prio = selectedIndices.Contains(fIdx + 1) ? 1 : 0;
                                    await _torrentFileService.SetPriorityAsync(files[fIdx].Id, prio);
                                }
                            }
                        }
                    }
                }

                if (optDictElem.ValueKind == JsonValueKind.Object)
                {
                    var updateDict = new Dictionary<string, object>();
                    if (optDictElem.TryGetProperty("max-overall-download-limit", out var dlOpt) && int.TryParse(dlOpt.GetString(), out var dlBps))
                    {
                        updateDict["MaxDownloadSpeedKbps"] = dlBps / 1024;
                    }

                    if (optDictElem.TryGetProperty("max-overall-upload-limit", out var ulOpt) && int.TryParse(ulOpt.GetString(), out var ulBps))
                    {
                        updateDict["MaxUploadSpeedKbps"] = ulBps / 1024;
                    }

                    if (updateDict.Count > 0)
                    {
                        _configService.SaveConfigDictionary(updateDict);
                    }
                }

                return "OK";

            case "system.multicall":
                if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
                {
                    var calls = parameters[0];
                    var results = new List<object>();
                    if (calls.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var call in calls.EnumerateArray())
                        {
                            if (call.TryGetProperty("methodName", out var mn) && call.TryGetProperty("params", out var p))
                            {
                                var subRes = await ExecuteMethodAsync(mn.GetString() ?? string.Empty, p);
                                results.Add(new object[] { subRes });
                            }
                        }
                    }

                    return results;
                }

                return new List<object>();

            case "system.listmethods":
                return new[]
                {
                    "aria2.addUri",
                    "aria2.addTorrent",
                    "aria2.remove",
                    "aria2.forceRemove",
                    "aria2.pause",
                    "aria2.forcePause",
                    "aria2.unpause",
                    "aria2.forceUnpause",
                    "aria2.tellStatus",
                    "aria2.getUris",
                    "aria2.getFiles",
                    "aria2.getPeers",
                    "aria2.getServers",
                    "aria2.tellActive",
                    "aria2.tellWaiting",
                    "aria2.tellStopped",
                    "aria2.changePosition",
                    "aria2.changeUri",
                    "aria2.getOption",
                    "aria2.changeOption",
                    "aria2.getGlobalOption",
                    "aria2.changeGlobalOption",
                    "aria2.getGlobalStat",
                    "aria2.purgeDownloadResult",
                    "aria2.removeDownloadResult",
                    "aria2.getVersion",
                    "aria2.getSessionInfo",
                    "aria2.shutdown",
                    "aria2.forceShutdown",
                    "system.multicall",
                    "system.listMethods"
                };

            default:
                _logger.Debug("Unhandled Aria2 RPC method: {0}", method);
                return "OK";
        }
    }

    private static List<JsonElement> GetCleanParams(JsonElement parameters)
    {
        var list = new List<JsonElement>();
        if (parameters.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in parameters.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString()?.StartsWith("token:", StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            list.Add(item);
        }

        return list;
    }

    private static List<string> GetXmlRpcStringParams(global::System.Xml.Linq.XDocument xmlDoc)
    {
        var list = new List<string>();
        var paramsElem = xmlDoc?.Root?.Element("params");
        if (paramsElem == null)
        {
            return list;
        }

        foreach (var p in paramsElem.Elements("param"))
        {
            var val = p.Element("value");
            var strVal = val?.Element("string")?.Value ?? val?.Value;
            if (!string.IsNullOrWhiteSpace(strVal))
            {
                if (strVal.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(strVal);
            }
        }

        return list;
    }

    private async Task<IActionResult> HandleXmlRpcAsync(string method, global::System.Xml.Linq.XDocument xmlDoc)
    {
        var stringParams = GetXmlRpcStringParams(xmlDoc);
        var downloadDir = _configService.DownloadDir ?? "/downloads";

        string customDir = null;
        var structElem = xmlDoc?.Root?.Element("params")?.Elements("param")
            .Select(p => p.Element("value")?.Element("struct"))
            .FirstOrDefault(s => s != null);
        if (structElem != null)
        {
            foreach (var member in structElem.Elements("member"))
            {
                var name = member.Element("name")?.Value;
                if (string.Equals(name, "dir", StringComparison.OrdinalIgnoreCase))
                {
                    customDir = member.Element("value")?.Element("string")?.Value ?? member.Element("value")?.Value;
                }
            }
        }

        switch (method.ToLowerInvariant())
        {
            case "aria2.getversion":
                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("struct",
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "version"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "1.36.0"))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "enabledFeatures"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("array", new global::System.Xml.Linq.XElement("data",
                            new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "BitTorrent")),
                            new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "GZip")),
                            new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "HTTPS")),
                            new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "MessageDigest")),
                            new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "Async DNS"))))))));

            case "aria2.getglobalstat":
                var all = _torrentService.GetAll().ToList();
                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("struct",
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "downloadSpeed"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", all.Sum(t => t.DownloadSpeed).ToString()))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "uploadSpeed"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", all.Sum(t => t.UploadSpeed).ToString()))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "numActive"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", all.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding).ToString()))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "numWaiting"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", all.Count(t => t.Status == TorrentStatus.Queued).ToString()))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "numStopped"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", all.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped).ToString())))));

            case "aria2.getglobaloption":
            case "aria2.getoption":
                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("struct",
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "dir"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", downloadDir))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "max-overall-download-limit"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "0"))),
                    new global::System.Xml.Linq.XElement("member",
                        new global::System.Xml.Linq.XElement("name", "max-overall-upload-limit"),
                        new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "0")))));

            case "aria2.tellactive":
                var activeList = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding)
                    .ToList();
                return BuildXmlRpcResponse(BuildXmlRpcTorrentArray(activeList, downloadDir, _torrentFileService));

            case "aria2.tellwaiting":
                var waitingList = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Queued)
                    .ToList();
                return BuildXmlRpcResponse(BuildXmlRpcTorrentArray(waitingList, downloadDir, _torrentFileService));

            case "aria2.tellstopped":
                var stoppedList = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped)
                    .ToList();
                return BuildXmlRpcResponse(BuildXmlRpcTorrentArray(stoppedList, downloadDir, _torrentFileService));

            case "aria2.tellstatus":
                var gid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var found = FindByGid(gid);
                if (found != null)
                {
                    return BuildXmlRpcResponse(BuildXmlRpcTorrentStruct(found, downloadDir, _torrentFileService));
                }

                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("struct"));

            case "aria2.addtorrent":
                if (stringParams.Count > 0)
                {
                    var b64 = stringParams[0];
                    var bytes = Convert.FromBase64String(b64);
                    var parsed = _torrentFileParser.Parse(bytes);
                    var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, customDir, false, bytes);
                    return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16]));
                }

                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", Guid.NewGuid().ToString("N")[..16]));

            case "aria2.adduri":
                if (stringParams.Count > 0)
                {
                    var uri = stringParams[0];
                    if (uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        var added = await _torrentService.AddFromMagnetAsync(uri, null, customDir, false);
                        return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16]));
                    }
                    else
                    {
                        using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                        var bytes = await client.GetByteArrayAsync(uri);
                        var parsed = _torrentFileParser.Parse(bytes);
                        var added = await _torrentService.AddFromParsedTorrentAsync(parsed, null, customDir, false, bytes);
                        return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", added?.InfoHash ?? Guid.NewGuid().ToString("N")[..16]));
                    }
                }

                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", Guid.NewGuid().ToString("N")[..16]));

            case "aria2.remove":
            case "aria2.forceremove":
            case "aria2.removedownloadresult":
                var removeGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var toRemove = FindByGid(removeGid);
                if (toRemove != null)
                {
                    await _torrentService.DeleteAsync(toRemove.Id, false);
                }

                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", removeGid));

            case "aria2.pause":
            case "aria2.forcepause":
                var pauseGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var toPause = FindByGid(pauseGid);
                if (toPause != null)
                {
                    await _torrentService.PauseAsync(toPause.Id);
                }

                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", pauseGid));

            case "aria2.unpause":
            case "aria2.forceunpause":
                var unpauseGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var toUnpause = FindByGid(unpauseGid);
                if (toUnpause != null)
                {
                    await _torrentService.ResumeAsync(toUnpause.Id);
                }

                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", unpauseGid));

            default:
                return BuildXmlRpcResponse(new global::System.Xml.Linq.XElement("string", "OK"));
        }
    }

    private static global::System.Xml.Linq.XElement BuildXmlRpcTorrentStruct(Torrent t, string downloadDir, ITorrentFileService torrentFileService)
    {
        var gid = t.InfoHash.Length >= 16 ? t.InfoHash[..16] : t.InfoHash;
        var status = t.Status switch
        {
            TorrentStatus.Downloading => "active",
            TorrentStatus.Seeding => "active",
            TorrentStatus.Paused => "paused",
            TorrentStatus.Stopped => "complete",
            TorrentStatus.Error => "error",
            _ => "waiting"
        };
        var dir = t.SavePath ?? downloadDir ?? "/downloads";
        var name = t.Name ?? string.Empty;

        var filesDataElem = new global::System.Xml.Linq.XElement("data");
        var files = torrentFileService?.GetFiles(t.Id)?.ToList();
        if (files != null && files.Count > 0)
        {
            for (var i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var filePath = string.IsNullOrWhiteSpace(f.Path) ? name : f.Path;
                filesDataElem.Add(new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("struct",
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "index"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", (i + 1).ToString()))),
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "path"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", global::System.IO.Path.Combine(dir, filePath)))),
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "length"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", f.Size.ToString()))),
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "completedLength"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", ((long)(f.Size * f.Progress)).ToString()))),
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "selected"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", (f.Priority > 0).ToString().ToLowerInvariant()))))));
            }
        }
        else
        {
            filesDataElem.Add(new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("struct",
                new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "index"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "1"))),
                new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "path"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", global::System.IO.Path.Combine(dir, name)))),
                new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "length"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.TotalSize.ToString()))),
                new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "completedLength"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.Downloaded.ToString()))),
                new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "selected"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "true"))))));
        }

        return new global::System.Xml.Linq.XElement("struct",
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "gid"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", gid))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "status"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", status))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "totalLength"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.TotalSize.ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "completedLength"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.Downloaded.ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "uploadLength"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.Uploaded.ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "downloadSpeed"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.DownloadSpeed.ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "uploadSpeed"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.UploadSpeed.ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "infoHash"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.InfoHash.ToLowerInvariant()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "numSeeders"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", t.Seeders.ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "connections"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", (t.Leechers + t.Seeders).ToString()))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "dir"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", dir))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "bittorrent"), new global::System.Xml.Linq.XElement("value",
                new global::System.Xml.Linq.XElement("struct",
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "info"), new global::System.Xml.Linq.XElement("value",
                        new global::System.Xml.Linq.XElement("struct",
                            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "name"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", name)))))),
                    new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "mode"), new global::System.Xml.Linq.XElement("value", new global::System.Xml.Linq.XElement("string", "multi")))))),
            new global::System.Xml.Linq.XElement("member", new global::System.Xml.Linq.XElement("name", "files"), new global::System.Xml.Linq.XElement("value",
                new global::System.Xml.Linq.XElement("array", filesDataElem))));
    }

    private static global::System.Xml.Linq.XElement BuildXmlRpcTorrentArray(IEnumerable<Torrent> torrents, string downloadDir, ITorrentFileService torrentFileService)
    {
        var dataElem = new global::System.Xml.Linq.XElement("data");
        foreach (var t in torrents)
        {
            dataElem.Add(new global::System.Xml.Linq.XElement("value", BuildXmlRpcTorrentStruct(t, downloadDir, torrentFileService)));
        }

        return new global::System.Xml.Linq.XElement("array", dataElem);
    }

    private IActionResult BuildXmlRpcResponse(global::System.Xml.Linq.XElement valueContent)
    {
        var doc = new global::System.Xml.Linq.XDocument(
            new global::System.Xml.Linq.XElement("methodResponse",
                new global::System.Xml.Linq.XElement("params",
                    new global::System.Xml.Linq.XElement("param",
                        new global::System.Xml.Linq.XElement("value", valueContent)))));

        return Content(doc.ToString(global::System.Xml.Linq.SaveOptions.DisableFormatting), "text/xml", global::System.Text.Encoding.UTF8);
    }

    private Torrent FindByGid(string gid)
    {
        if (string.IsNullOrWhiteSpace(gid))
        {
            return null;
        }

        var clean = gid.Trim();
        var all = _torrentService.GetAll().ToList();
        return all.FirstOrDefault(t => t.InfoHash.StartsWith(clean, StringComparison.OrdinalIgnoreCase) || t.InfoHash.Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, object> MapTorrentToAria2(Torrent t)
    {
        var gid = t.InfoHash.Length >= 16 ? t.InfoHash[..16] : t.InfoHash;
        var status = t.Status switch
        {
            TorrentStatus.Downloading => "active",
            TorrentStatus.Seeding => "active",
            TorrentStatus.Paused => "paused",
            TorrentStatus.Stopped => "complete",
            TorrentStatus.Error => "error",
            _ => "waiting"
        };

        return new Dictionary<string, object>
        {
            { "gid", gid },
            { "status", status },
            { "totalLength", t.TotalSize.ToString() },
            { "completedLength", t.Downloaded.ToString() },
            { "uploadLength", t.Uploaded.ToString() },
            { "downloadSpeed", t.DownloadSpeed.ToString() },
            { "uploadSpeed", t.UploadSpeed.ToString() },
            { "infoHash", t.InfoHash.ToLowerInvariant() },
            { "numSeeders", t.Seeders.ToString() },
            { "connections", (t.Leechers + t.Seeders).ToString() },
            { "dir", t.SavePath ?? (_configService.DownloadDir ?? "/downloads") },
            {
                "bittorrent", new Dictionary<string, object>
                {
                    { "info", new Dictionary<string, string> { { "name", t.Name ?? string.Empty } } },
                    { "mode", "multi" }
                }
            },
            {
                "files", (_torrentFileService.GetFiles(t.Id)?.ToList() is { Count: > 0 } torrentFiles)
                    ? torrentFiles.Select((f, idx) => (object)new
                    {
                        index = (idx + 1).ToString(),
                        path = global::System.IO.Path.Combine(t.SavePath ?? "/downloads", f.Path ?? string.Empty),
                        length = f.Size.ToString(),
                        completedLength = (f.Progress >= 1.0 ? f.Size : (long)(f.Size * f.Progress)).ToString(),
                        selected = f.Priority > 0 ? "true" : "false"
                    }).ToArray()
                    : new object[]
                    {
                        new
                        {
                            index = "1",
                            path = global::System.IO.Path.Combine(t.SavePath ?? "/downloads", t.Name ?? string.Empty),
                            length = t.TotalSize.ToString(),
                            completedLength = t.Downloaded.ToString(),
                            selected = "true"
                        }
                    }
            }
        };
    }

    private static HashSet<int> ParseAria2FileIndices(string selectFile)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(selectFile))
        {
            return result;
        }

        var parts = selectFile.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Contains('-'))
            {
                var range = p.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
                {
                    for (var i = start; i <= end; i++)
                    {
                        result.Add(i);
                    }
                }
            }
            else if (int.TryParse(p, out var single))
            {
                result.Add(single);
            }
        }

        return result;
    }
}
