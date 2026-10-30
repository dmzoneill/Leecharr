// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

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

[ApiController]
[Route("jsonrpc")]
[Route("rpc")]
[Route("aria2/jsonrpc")]
[Route("aria2/rpc")]
public class Aria2RpcController : ControllerBase
{
    private static readonly string[] SupportedMethods =
    [
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
        "system.listMethods",
    ];

    private static readonly string[] EnabledFeatures =
    [
        "BitTorrent",
        "GZip",
        "HTTPS",
        "MessageDigest",
        "Async DNS",
    ];

    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly string ProcessSessionId = Guid.NewGuid().ToString("N");

    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IDownloadEngine downloadEngine;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public Aria2RpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null,
        ITrackerEntryRepository trackerEntryRepository = null,
        IDownloadEngine downloadEngine = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.trackerEntryRepository = trackerEntryRepository;
        this.downloadEngine = downloadEngine;
    }

    [HttpGet] /* csrf-safe: SameSite=Lax auth cookie, JSON/API-key endpoints */ // nosemgrep
    [HttpPost]
    public async Task<IActionResult> HandleRpc()
    {
        string method = null;
        JsonElement paramsElem = default;
        object id = 1;

        if (HttpMethods.IsGet(this.Request.Method))
        {
            if (this.Request.Query.TryGetValue("method", out var qMethod) && !string.IsNullOrWhiteSpace(qMethod))
            {
                method = qMethod.ToString();
            }

            if (this.Request.Query.TryGetValue("id", out var idVal) && !string.IsNullOrWhiteSpace(idVal))
            {
                if (long.TryParse(idVal, out var numericId))
                {
                    id = numericId;
                }
                else
                {
                    id = idVal.ToString();
                }
            }

            if (this.Request.Query.TryGetValue("params", out var rawParams) && !string.IsNullOrWhiteSpace(rawParams))
            {
                try
                {
                    var rawParamsStr = rawParams.ToString();
                    byte[] jsonBytes;
                    try
                    {
                        jsonBytes = Convert.FromBase64String(rawParamsStr);
                    }
                    catch
                    {
                        jsonBytes = Encoding.UTF8.GetBytes(rawParamsStr);
                    }

                    using var doc = JsonDocument.Parse(jsonBytes);
                    paramsElem = doc.RootElement.Clone();
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to decode or parse base64 params for Aria2 GET RPC");
                }
            }
        }
        else if (HttpMethods.IsPost(this.Request.Method))
        {
            try
            {
                using var reader = new StreamReader(this.Request.Body, Encoding.UTF8);
                var rawBody = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    var trimmed = rawBody.TrimStart();
                    if (trimmed.StartsWith('<'))
                    {
                        XDocument xmlDoc;
                        try
                        {
                            xmlDoc = SafeXmlParser.Parse(rawBody);
                        }
                        catch (XmlException ex)
                        {
                            this.logger.Warn(ex, "Failed to parse Aria2 XML-RPC request");
                            return this.BuildXmlRpcFault(1, "Invalid XML-RPC request");
                        }

                        var xmlMethodName = xmlDoc.Root?.Element("methodName")?.Value ?? string.Empty;

                        var isXmlAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
                        if (!isXmlAuth)
                        {
                            var firstToken = GetFirstXmlRpcToken(xmlDoc);
                            if (!string.IsNullOrWhiteSpace(firstToken) && !string.IsNullOrWhiteSpace(this.configFileProvider?.ApiKey))
                            {
                                if (RpcAuthenticationHelper.FixedTimeEquals(firstToken, this.configFileProvider.ApiKey))
                                {
                                    isXmlAuth = true;
                                }
                            }
                        }

                        if (!isXmlAuth)
                        {
                            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Aria2\"";
                            return this.BuildXmlRpcFault(1, "Unauthorized");
                        }

                        try
                        {
                            return await this.HandleXmlRpcAsync(xmlMethodName, xmlDoc);
                        }
                        catch (Exception ex)
                        {
                            this.logger.Error(ex, "Error handling Aria2 XML-RPC method: {0}", xmlMethodName);
                            return this.BuildXmlRpcFault(1, ex.Message);
                        }
                    }

                    using var doc = JsonDocument.Parse(rawBody);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        var batchResults = new List<object>();
                        foreach (var item in root.EnumerateArray())
                        {
                            batchResults.Add(await this.ProcessSingleJsonRpcAsync(item));
                        }

                        return this.Ok(batchResults);
                    }

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
                this.logger.Debug(ex, "Could not parse Aria2 payload");
            }
        }

        var aria2Secret = ExtractSecretFromParams(paramsElem);
        var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
        if (!isAuth && !string.IsNullOrWhiteSpace(aria2Secret) && !string.IsNullOrWhiteSpace(this.configFileProvider?.ApiKey))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(aria2Secret, this.configFileProvider.ApiKey))
            {
                isAuth = true;
            }
        }

        if (!isAuth)
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Aria2\"";
            return this.ReturnRpcResponse(new { id, jsonrpc = "2.0", error = new { code = 1, message = "Unauthorized" } }, StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(method))
        {
            if (this.Request.Query.TryGetValue("method", out var qMethod) && !string.IsNullOrWhiteSpace(qMethod))
            {
                method = qMethod.ToString();
            }

            if (string.IsNullOrWhiteSpace(method))
            {
                return this.ReturnRpcResponse(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = GetVersionPayload(),
                });
            }
        }

        try
        {
            var res = await this.ExecuteMethodAsync(method, paramsElem);
            return this.ReturnRpcResponse(new
            {
                jsonrpc = "2.0",
                id,
                result = res,
            });
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling Aria2 RPC method: {0}", method);
            return this.ReturnRpcResponse(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = 1, message = ex.Message },
            });
        }
    }

    private static object GetVersionPayload() => new
    {
        version = "1.36.0",
        enabledFeatures = EnabledFeatures,
    };

    private static string ExtractSecretFromParams(JsonElement paramsElem)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            if (paramsElem[0].ValueKind == JsonValueKind.String)
            {
                var firstStr = paramsElem[0].GetString();
                if (firstStr != null && firstStr.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                {
                    return firstStr["token:".Length..];
                }
            }
            else if (paramsElem[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var call in paramsElem[0].EnumerateArray())
                {
                    if (call.ValueKind == JsonValueKind.Object &&
                        call.TryGetProperty("params", out var subParams) &&
                        subParams.ValueKind == JsonValueKind.Array &&
                        subParams.GetArrayLength() > 0 &&
                        subParams[0].ValueKind == JsonValueKind.String)
                    {
                        var subFirstStr = subParams[0].GetString();
                        if (subFirstStr != null && subFirstStr.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                        {
                            return subFirstStr["token:".Length..];
                        }
                    }
                }
            }
            else if (paramsElem[0].ValueKind == JsonValueKind.Object)
            {
                if (paramsElem[0].TryGetProperty("params", out var subParams) &&
                    subParams.ValueKind == JsonValueKind.Array &&
                    subParams.GetArrayLength() > 0 &&
                    subParams[0].ValueKind == JsonValueKind.String)
                {
                    var subFirstStr = subParams[0].GetString();
                    if (subFirstStr != null && subFirstStr.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                    {
                        return subFirstStr["token:".Length..];
                    }
                }
            }
        }

        return string.Empty;
    }

    private IActionResult ReturnRpcResponse(object payload, int statusCode = StatusCodes.Status200OK)
    {
        if (this.Request?.Query != null && this.Request.Query.TryGetValue("callback", out var callback))
        {
            var callbackName = callback.ToString();
            if (JsonpCallbackHelper.IsValidCallbackName(callbackName))
            {
                var json = JsonSerializer.Serialize(payload, CamelCaseJsonOptions);
                return this.Content($"{callbackName}({json});", "application/javascript", Encoding.UTF8);
            }
        }

        if (statusCode == StatusCodes.Status200OK)
        {
            return this.Ok(payload);
        }

        return this.StatusCode(statusCode, payload);
    }

    private async Task<object> ProcessSingleJsonRpcAsync(JsonElement root)
    {
        object id = null;
        string method = null;
        JsonElement paramsElem = default;

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

        var aria2Secret = ExtractSecretFromParams(paramsElem);
        var isAuth = RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider);
        if (!isAuth && !string.IsNullOrWhiteSpace(aria2Secret) && !string.IsNullOrWhiteSpace(this.configFileProvider?.ApiKey))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(aria2Secret, this.configFileProvider.ApiKey))
            {
                isAuth = true;
            }
        }

        if (!isAuth)
        {
            return new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = 1, message = "Unauthorized" },
            };
        }

        if (string.IsNullOrWhiteSpace(method))
        {
            return new
            {
                jsonrpc = "2.0",
                id,
                result = GetVersionPayload(),
            };
        }

        try
        {
            var res = await this.ExecuteMethodAsync(method, paramsElem);
            return new
            {
                jsonrpc = "2.0",
                id,
                result = res,
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling Aria2 RPC method: {0}", method);
            return new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = 1, message = ex.Message },
            };
        }
    }

    private async Task<object> ExecuteMethodAsync(string method, JsonElement parameters)
    {
        var cleanParams = GetCleanParams(parameters);

        return method.ToLowerInvariant() switch
        {
            "aria2.getversion" => GetVersionPayload(),
            "aria2.getsessioninfo" => new { sessionId = ProcessSessionId },
            "aria2.getglobalstat" => this.HandleGetGlobalStat(),
            "aria2.tellactive" => this.HandleTellActive(),
            "aria2.tellwaiting" => this.HandleTellWaiting(cleanParams),
            "aria2.tellstopped" => this.HandleTellStopped(cleanParams),
            "aria2.tellstatus" => this.HandleTellStatus(cleanParams),
            "aria2.addtorrent" => await this.HandleAddTorrentAsync(cleanParams),
            "aria2.adduri" => await this.HandleAddUriAsync(cleanParams),
            "aria2.remove" or "aria2.forceremove" => await this.HandleRemoveAsync(cleanParams),
            "aria2.purgedownloadresult" => await this.HandlePurgeDownloadResultAsync(cleanParams),
            "aria2.removedownloadresult" => await this.HandleRemoveDownloadResultAsync(cleanParams),
            "aria2.pause" or "aria2.forcepause" => await this.HandlePauseAsync(cleanParams),
            "aria2.unpause" or "aria2.forceunpause" => await this.HandleUnpauseAsync(cleanParams),
            "aria2.geturis" => this.HandleGetUris(cleanParams),
            "aria2.getfiles" => this.HandleGetFiles(cleanParams),
            "aria2.getpeers" => this.HandleGetPeers(cleanParams),
            "aria2.getservers" => Array.Empty<object>(),
            "aria2.getglobaloption" => this.GetGlobalOptions(),
            "aria2.getoption" => this.HandleGetOption(cleanParams),
            "aria2.changeposition" => await this.HandleChangePositionAsync(cleanParams),
            "aria2.changeuri" => await this.HandleChangeUriAsync(cleanParams),
            "aria2.changeoption" or "aria2.changeglobaloption" => await this.HandleChangeOptionAsync(cleanParams),
            "aria2.shutdown" or "aria2.forceshutdown" => "OK",
            "system.multicall" => await this.HandleSystemMulticallAsync(cleanParams),
            "system.listmethods" => SupportedMethods,
            _ => throw new InvalidOperationException($"Method {method} is not supported."),
        };
    }

    private object HandleGetGlobalStat()
    {
        var stats = this.GetGlobalStats();
        return new
        {
            downloadSpeed = stats.DownloadSpeed,
            uploadSpeed = stats.UploadSpeed,
            numActive = stats.NumActive,
            numWaiting = stats.NumWaiting,
            numStopped = stats.NumStopped,
        };
    }

    private object HandleTellActive()
    {
        return this.GetActiveTorrents()
            .Select(this.MapTorrentToAria2)
            .ToList();
    }

    private object HandleTellWaiting(List<JsonElement> cleanParams)
    {
        var (offset, num) = ParseOffsetAndNum(cleanParams);
        return this.GetWaitingTorrents(offset, num)
            .Select(this.MapTorrentToAria2)
            .ToList();
    }

    private object HandleTellStopped(List<JsonElement> cleanParams)
    {
        var (offset, num) = ParseOffsetAndNum(cleanParams);
        return this.GetStoppedTorrents(offset, num)
            .Select(this.MapTorrentToAria2)
            .ToList();
    }

    private object HandleTellStatus(List<JsonElement> cleanParams)
    {
        var gid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        var torrent = this.FindByGid(gid);
        if (torrent == null)
        {
            ThrowGidNotFound(gid);
        }

        return this.MapTorrentToAria2(torrent);
    }

    private async Task<object> HandleAddTorrentAsync(List<JsonElement> cleanParams)
    {
        if (cleanParams.Count > 0)
        {
            var b64 = cleanParams[0].GetString();
            var (savePath, isPaused) = ExtractAddOptions(cleanParams);
            return await this.AddTorrentBase64Async(b64, savePath, isPaused);
        }

        ThrowEmptyAdd();
        return string.Empty;
    }

    private async Task<object> HandleAddUriAsync(List<JsonElement> cleanParams)
    {
        if (cleanParams.Count > 0)
        {
            string uri = null;
            if (cleanParams[0].ValueKind == JsonValueKind.Array && cleanParams[0].GetArrayLength() > 0)
            {
                uri = cleanParams[0][0].GetString();
            }
            else if (cleanParams[0].ValueKind == JsonValueKind.String)
            {
                uri = cleanParams[0].GetString();
            }

            if (!string.IsNullOrWhiteSpace(uri))
            {
                var (savePath, isPaused) = ExtractAddOptions(cleanParams);
                return await this.AddUriAsync(uri, savePath, isPaused);
            }
        }

        ThrowEmptyAdd();
        return string.Empty;
    }

    private async Task<object> HandleRemoveAsync(List<JsonElement> cleanParams)
    {
        var removeGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        return await this.RemoveTorrentByGidAsync(removeGid);
    }

    private async Task<object> HandlePurgeDownloadResultAsync(List<JsonElement> cleanParams)
    {
        await this.PurgeDownloadResultsAsync();
        return "OK";
    }

    private async Task<object> HandleRemoveDownloadResultAsync(List<JsonElement> cleanParams)
    {
        var resGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        await this.RemoveDownloadResultByGidAsync(resGid);
        return "OK";
    }

    private async Task<object> HandlePauseAsync(List<JsonElement> cleanParams)
    {
        var pauseGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        return await this.PauseTorrentByGidAsync(pauseGid);
    }

    private async Task<object> HandleUnpauseAsync(List<JsonElement> cleanParams)
    {
        var unpauseGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        return await this.ResumeTorrentByGidAsync(unpauseGid);
    }

    private object HandleGetUris(List<JsonElement> cleanParams)
    {
        var gid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        var torrent = this.FindByGid(gid);
        if (torrent == null)
        {
            return Array.Empty<object>();
        }

        return this.GetDownloadUris(torrent)
            .Select(uri => new
            {
                uri,
                status = "used",
            })
            .ToArray();
    }

    private object HandleGetFiles(List<JsonElement> cleanParams)
    {
        var filesGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        var filesTorrent = this.FindByGid(filesGid);
        if (filesTorrent != null)
        {
            var saveDir = filesTorrent.SavePath ?? this.configService.DownloadDir ?? "/downloads";
            var files = this.torrentFileService.GetFiles(filesTorrent.Id)?.ToList();
            var downloadTask = this.torrentService?.GetDownloadTask(filesTorrent.Id);
            if (files != null && files.Count > 0)
            {
                TorrentFileProgressEnricher.Enrich(filesTorrent, files, downloadTask);
                return files.Select((f, idx) => new
                {
                    index = (idx + 1).ToString(),
                    path = Path.Combine(saveDir, f.Path ?? string.Empty),
                    length = f.Size.ToString(),
                    completedLength = f.BytesCompleted.ToString(),
                    selected = f.Priority > 0 ? "true" : "false",
                    uris = Array.Empty<object>(),
                }).ToList();
            }

            return new object[]
            {
                new
                {
                    index = "1",
                    path = Path.Combine(saveDir, filesTorrent.Name ?? string.Empty),
                    length = filesTorrent.TotalSize.ToString(),
                    completedLength = filesTorrent.Downloaded.ToString(),
                    selected = "true",
                    uris = Array.Empty<object>(),
                },
            };
        }

        return Array.Empty<object>();
    }

    private object HandleGetPeers(List<JsonElement> cleanParams)
    {
        var peersGid = cleanParams.Count > 0 ? cleanParams[0].GetString() : string.Empty;
        var swarmPeers = this.GetPeersForTorrent(peersGid);
        return swarmPeers.Select(p => new
        {
            peerId = p.Client ?? string.Empty,
            ip = p.Ip ?? string.Empty,
            port = p.Port.ToString(),
            bitfield = string.Empty,
            amChoking = p.ClientIsChoked.ToString().ToLowerInvariant(),
            peerChoking = p.IsChoked.ToString().ToLowerInvariant(),
            downloadSpeed = p.DownloadSpeed.ToString(),
            uploadSpeed = p.UploadSpeed.ToString(),
            seeder = (p.Progress >= 1.0).ToString().ToLowerInvariant(),
        }).ToList();
    }

    private object HandleGetOption(List<JsonElement> cleanParams)
    {
        var goGid = cleanParams.Count > 0 && cleanParams[0].ValueKind == JsonValueKind.String ? cleanParams[0].GetString() : null;
        return this.GetTorrentOptions(goGid);
    }

    private async Task<object> HandleChangePositionAsync(List<JsonElement> cleanParams)
    {
        if (cleanParams.Count >= 3)
        {
            var cpGid = cleanParams[0].GetString();
            var cpOffset = cleanParams[1].ValueKind == JsonValueKind.Number
                ? cleanParams[1].GetInt32()
                : (int.TryParse(cleanParams[1].GetString(), out var o) ? o : 0);
            var cpHow = cleanParams[2].GetString();
            return await this.MoveQueuePositionAsync(cpGid, cpOffset, cpHow);
        }

        return 0;
    }

    private async Task<int[]> HandleChangeUriAsync(List<JsonElement> cleanParams)
    {
        if (cleanParams.Count < 1 || cleanParams[0].ValueKind != JsonValueKind.String)
        {
            ThrowGidNotFound(string.Empty);
        }

        var gid = cleanParams[0].GetString();
        var fileIndex = 1;
        if (cleanParams.Count > 1)
        {
            if (!TryReadJsonInt(cleanParams[1], out fileIndex))
            {
                throw new InvalidOperationException("fileIndex is out of range");
            }
        }

        var delUris = cleanParams.Count > 2 ? ReadJsonStringArray(cleanParams[2]) : new List<string>();
        var addUris = cleanParams.Count > 3 ? ReadJsonStringArray(cleanParams[3]) : new List<string>();
        int? position = null;
        if (cleanParams.Count > 4 && TryReadJsonInt(cleanParams[4], out var parsedPosition))
        {
            position = parsedPosition;
        }

        return await this.ApplyChangeUriAsync(gid, fileIndex, delUris, addUris, position);
    }

    private async Task<object> HandleChangeOptionAsync(List<JsonElement> cleanParams)
    {
        var optDictElem = cleanParams.FirstOrDefault(p => p.ValueKind == JsonValueKind.Object);
        var gidElem = cleanParams.FirstOrDefault(p => p.ValueKind == JsonValueKind.String);
        var gidStr = gidElem.ValueKind == JsonValueKind.String ? gidElem.GetString() : null;

        var options = ExtractJsonOptions(optDictElem);
        await this.ApplyOptionsAsync(gidStr, options);
        return "OK";
    }

    private async Task<object> HandleSystemMulticallAsync(List<JsonElement> cleanParams)
    {
        if (cleanParams.Count > 0)
        {
            var calls = cleanParams[0];
            var results = new List<object>();
            if (calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    if (call.TryGetProperty("methodName", out var mn) && call.TryGetProperty("params", out var p))
                    {
                        try
                        {
                            var subRes = await this.ExecuteMethodAsync(mn.GetString() ?? string.Empty, p);
                            results.Add(new object[] { subRes });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new object[] { new { code = 1, message = ex.Message } });
                        }
                    }
                }
            }

            return results;
        }

        return new List<object>();
    }

    private async Task<IActionResult> HandleXmlRpcAsync(string method, XDocument xmlDoc)
    {
        var resultElement = await this.ExecuteXmlRpcMethodAsync(method, xmlDoc);
        return this.BuildXmlRpcResponse(resultElement);
    }

    private async Task<XElement> ExecuteXmlRpcMethodAsync(string method, XDocument xmlDoc)
    {
        var stringParams = GetXmlRpcStringParams(xmlDoc);
        var downloadDir = this.configService.DownloadDir ?? "/downloads";

        switch (method.ToLowerInvariant())
        {
            case "aria2.getversion":
                return new XElement(
                    "struct",
                    new XElement(
                        "member",
                        new XElement("name", "version"),
                        new XElement("value", new XElement("string", "1.36.0"))),
                    new XElement(
                        "member",
                        new XElement("name", "enabledFeatures"),
                        new XElement(
                            "value",
                            new XElement(
                                "array",
                                new XElement(
                                    "data",
                                    EnabledFeatures.Select(f => new XElement("value", new XElement("string", f))))))));

            case "aria2.getsessioninfo":
                return new XElement(
                    "struct",
                    new XElement(
                        "member",
                        new XElement("name", "sessionId"),
                        new XElement("value", new XElement("string", ProcessSessionId))));

            case "aria2.getglobalstat":
                var stats = this.GetGlobalStats();
                return new XElement(
                    "struct",
                    new XElement(
                        "member",
                        new XElement("name", "downloadSpeed"),
                        new XElement("value", new XElement("string", stats.DownloadSpeed))),
                    new XElement(
                        "member",
                        new XElement("name", "uploadSpeed"),
                        new XElement("value", new XElement("string", stats.UploadSpeed))),
                    new XElement(
                        "member",
                        new XElement("name", "numActive"),
                        new XElement("value", new XElement("string", stats.NumActive))),
                    new XElement(
                        "member",
                        new XElement("name", "numWaiting"),
                        new XElement("value", new XElement("string", stats.NumWaiting))),
                    new XElement(
                        "member",
                        new XElement("name", "numStopped"),
                        new XElement("value", new XElement("string", stats.NumStopped))));

            case "aria2.getglobaloption":
                return BuildXmlRpcStruct(this.GetGlobalOptions());

            case "aria2.getoption":
                var optGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                return BuildXmlRpcStruct(this.GetTorrentOptions(optGid));

            case "aria2.tellactive":
                return BuildXmlRpcTorrentArray(this.GetActiveTorrents(), downloadDir, this.torrentFileService);

            case "aria2.tellwaiting":
                var (xwo, xwn) = ParseXmlOffsetAndNum(stringParams);
                return BuildXmlRpcTorrentArray(this.GetWaitingTorrents(xwo, xwn), downloadDir, this.torrentFileService);

            case "aria2.tellstopped":
                var (xso, xsn) = ParseXmlOffsetAndNum(stringParams);
                return BuildXmlRpcTorrentArray(this.GetStoppedTorrents(xso, xsn), downloadDir, this.torrentFileService);

            case "aria2.tellstatus":
                var gid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var found = this.FindByGid(gid);
                if (found == null)
                {
                    ThrowGidNotFound(gid);
                }

                return BuildXmlRpcTorrentStruct(found, downloadDir, this.torrentFileService);

            case "aria2.geturis":
                var urisXmlGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var urisDataElem = new XElement("data");
                var urisTorrent = this.FindByGid(urisXmlGid);
                if (urisTorrent != null)
                {
                    foreach (var trackerUri in this.GetDownloadUris(urisTorrent))
                    {
                        urisDataElem.Add(new XElement(
                            "value",
                            new XElement(
                                "struct",
                                new XElement("member", new XElement("name", "uri"), new XElement("value", new XElement("string", trackerUri))),
                                new XElement("member", new XElement("name", "status"), new XElement("value", new XElement("string", "used"))))));
                    }
                }

                return new XElement("array", urisDataElem);

            case "aria2.getfiles":
                var filesGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var filesTorrent = this.FindByGid(filesGid);
                if (filesTorrent != null)
                {
                    var task = this.torrentService?.GetDownloadTask(filesTorrent.Id);
                    return BuildXmlRpcFilesArray(filesTorrent, downloadDir, this.torrentFileService, task);
                }

                return new XElement("array", new XElement("data"));

            case "aria2.getpeers":
                var peersXmlGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var peersDataElem = new XElement("data");
                foreach (var p in this.GetPeersForTorrent(peersXmlGid))
                {
                    peersDataElem.Add(new XElement(
                        "value",
                        new XElement(
                            "struct",
                            new XElement("member", new XElement("name", "peerId"), new XElement("value", new XElement("string", p.Client ?? string.Empty))),
                            new XElement("member", new XElement("name", "ip"), new XElement("value", new XElement("string", p.Ip ?? string.Empty))),
                            new XElement("member", new XElement("name", "port"), new XElement("value", new XElement("string", p.Port.ToString()))),
                            new XElement("member", new XElement("name", "bitfield"), new XElement("value", new XElement("string", string.Empty))),
                            new XElement("member", new XElement("name", "amChoking"), new XElement("value", new XElement("string", p.ClientIsChoked.ToString().ToLowerInvariant()))),
                            new XElement("member", new XElement("name", "peerChoking"), new XElement("value", new XElement("string", p.IsChoked.ToString().ToLowerInvariant()))),
                            new XElement("member", new XElement("name", "downloadSpeed"), new XElement("value", new XElement("string", p.DownloadSpeed.ToString()))),
                            new XElement("member", new XElement("name", "uploadSpeed"), new XElement("value", new XElement("string", p.UploadSpeed.ToString()))),
                            new XElement("member", new XElement("name", "seeder"), new XElement("value", new XElement("string", (p.Progress >= 1.0).ToString().ToLowerInvariant()))))));
                }

                return new XElement("array", peersDataElem);

            case "aria2.getservers":
                return new XElement("array", new XElement("data"));

            case "aria2.addtorrent":
                if (stringParams.Count > 0)
                {
                    var b64 = stringParams[0];
                    var (savePath, isPaused) = ExtractXmlAddOptions(xmlDoc);
                    var addedGid = await this.AddTorrentBase64Async(b64, savePath, isPaused);
                    return new XElement("string", addedGid);
                }

                ThrowEmptyAdd();
                return new XElement("string", string.Empty);

            case "aria2.adduri":
                if (stringParams.Count > 0)
                {
                    var uri = stringParams[0];
                    var (savePath, isPaused) = ExtractXmlAddOptions(xmlDoc);
                    var addedGid = await this.AddUriAsync(uri, savePath, isPaused);
                    return new XElement("string", addedGid);
                }

                ThrowEmptyAdd();
                return new XElement("string", string.Empty);

            case "aria2.remove":
            case "aria2.forceremove":
                var removeGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var remResult = await this.RemoveTorrentByGidAsync(removeGid);
                return new XElement("string", remResult);

            case "aria2.purgedownloadresult":
                await this.PurgeDownloadResultsAsync();
                return new XElement("string", "OK");

            case "aria2.removedownloadresult":
                var remXmlGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                await this.RemoveDownloadResultByGidAsync(remXmlGid);
                return new XElement("string", "OK");

            case "aria2.pause":
            case "aria2.forcepause":
                var pauseGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var pauseRes = await this.PauseTorrentByGidAsync(pauseGid);
                return new XElement("string", pauseRes);

            case "aria2.unpause":
            case "aria2.forceunpause":
                var unpauseGid = stringParams.Count > 0 ? stringParams[0] : string.Empty;
                var unpauseRes = await this.ResumeTorrentByGidAsync(unpauseGid);
                return new XElement("string", unpauseRes);

            case "aria2.changeposition":
                if (stringParams.Count >= 3)
                {
                    var cpGid = stringParams[0];
                    var cpOffset = int.TryParse(stringParams[1], out var offset) ? offset : 0;
                    var cpHow = stringParams[2];
                    var moveResult = await this.MoveQueuePositionAsync(cpGid, cpOffset, cpHow);
                    return new XElement("int", moveResult);
                }

                return new XElement("int", 0);

            case "aria2.changeuri":
                var changeUriCounts = await this.ChangeUriFromXmlAsync(xmlDoc);
                return new XElement(
                    "array",
                    new XElement(
                        "data",
                        new XElement("value", new XElement("int", changeUriCounts[0])),
                        new XElement("value", new XElement("int", changeUriCounts[1]))));

            case "aria2.changeoption":
            case "aria2.changeglobaloption":
                var optDict = GetXmlRpcStructOptions(xmlDoc);
                var changeGid = stringParams.Count > 0 ? stringParams[0] : null;
                await this.ApplyOptionsAsync(changeGid, optDict);
                return new XElement("string", "OK");

            case "aria2.shutdown":
            case "aria2.forceshutdown":
                return new XElement("string", "OK");

            case "system.multicall":
                var multicallDataElem = new XElement("data");
                var callsArray = xmlDoc?.Root?.Element("params")?.Elements("param")
                    .Select(p => p.Element("value")?.Element("array")?.Element("data"))
                    .FirstOrDefault(d => d != null);

                if (callsArray != null)
                {
                    foreach (var callVal in callsArray.Elements("value"))
                    {
                        var callStruct = callVal.Element("struct");
                        if (callStruct == null)
                        {
                            continue;
                        }

                        string subMethod = null;
                        XElement subParamsElem = null;

                        foreach (var member in callStruct.Elements("member"))
                        {
                            var memberName = member.Element("name")?.Value;
                            if (string.Equals(memberName, "methodName", StringComparison.OrdinalIgnoreCase))
                            {
                                subMethod = member.Element("value")?.Element("string")?.Value ?? member.Element("value")?.Value;
                            }
                            else if (string.Equals(memberName, "params", StringComparison.OrdinalIgnoreCase))
                            {
                                subParamsElem = member.Element("value");
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(subMethod))
                        {
                            var subDoc = new XDocument(
                                new XElement(
                                    "methodCall",
                                    new XElement("methodName", subMethod)));

                            var syntheticParams = new XElement("params");
                            if (subParamsElem != null)
                            {
                                if (subParamsElem.Element("array")?.Element("data") is XElement data)
                                {
                                    foreach (var v in data.Elements("value"))
                                    {
                                        syntheticParams.Add(new XElement("param", new XElement(v)));
                                    }
                                }
                                else
                                {
                                    syntheticParams.Add(new XElement("param", new XElement(subParamsElem)));
                                }
                            }

                            subDoc.Root?.Add(syntheticParams);

                            try
                            {
                                var subResult = await this.ExecuteXmlRpcMethodAsync(subMethod, subDoc);
                                multicallDataElem.Add(
                                    new XElement(
                                        "value",
                                        new XElement(
                                            "array",
                                            new XElement(
                                                "data",
                                                new XElement("value", subResult)))));
                            }
                            catch (Exception ex)
                            {
                                multicallDataElem.Add(
                                    new XElement(
                                        "value",
                                        new XElement(
                                            "struct",
                                            new XElement(
                                                "member",
                                                new XElement("name", "faultCode"),
                                                new XElement("value", new XElement("int", 1))),
                                            new XElement(
                                                "member",
                                                new XElement("name", "faultString"),
                                                new XElement("value", new XElement("string", ex.Message))))));
                            }
                        }
                    }
                }

                return new XElement("array", multicallDataElem);

            case "system.listmethods":
                var methodsDataElem = new XElement("data");
                foreach (var m in SupportedMethods)
                {
                    methodsDataElem.Add(new XElement("value", new XElement("string", m)));
                }

                return new XElement("array", methodsDataElem);

            default:
                return new XElement("string", "OK");
        }
    }

    private (string DownloadSpeed, string UploadSpeed, string NumActive, string NumWaiting, string NumStopped) GetGlobalStats()
    {
        var all = this.torrentService.GetAll().ToList();
        return (
            all.Sum(t => t.DownloadSpeed).ToString(),
            all.Sum(t => t.UploadSpeed).ToString(),
            all.Count(t => IsAria2ActiveStatus(t.Status)).ToString(),
            all.Count(t => IsAria2WaitingStatus(t.Status)).ToString(),
            all.Count(t => IsAria2StoppedStatus(t.Status)).ToString());
    }

    private List<Torrent> GetActiveTorrents()
    {
        return this.torrentService.GetAll()
            .Where(t => IsAria2ActiveStatus(t.Status))
            .ToList();
    }

    private List<Torrent> GetWaitingTorrents(int offset, int num)
    {
        var waitingList = this.torrentService.GetAll()
            .Where(t => IsAria2WaitingStatus(t.Status))
            .ToList();
        return SliceList(waitingList, offset, num);
    }

    private List<Torrent> GetStoppedTorrents(int offset, int num)
    {
        var stoppedList = this.torrentService.GetAll()
            .Where(t => IsAria2StoppedStatus(t.Status))
            .ToList();
        return SliceList(stoppedList, offset, num);
    }

    private static bool IsAria2ActiveStatus(TorrentStatus status) =>
        status is TorrentStatus.Downloading or TorrentStatus.Seeding;

    private static bool IsAria2WaitingStatus(TorrentStatus status) =>
        status is TorrentStatus.Queued or TorrentStatus.Paused or TorrentStatus.Checking or TorrentStatus.QueuedForChecking;

    private static bool IsAria2StoppedStatus(TorrentStatus status) =>
        status is TorrentStatus.Stopped or TorrentStatus.Completed or TorrentStatus.Error or TorrentStatus.Stalled;

    private async Task<string> AddTorrentBase64Async(string base64, string savePath = null, bool isPaused = false)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            ThrowEmptyAdd();
        }

        var bytes = Convert.FromBase64String(base64);
        var parsed = this.torrentFileParser.Parse(bytes);
        var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, savePath, isPaused, bytes);
        return GetGidFromInfoHash(added?.InfoHash);
    }

    private async Task<string> AddUriAsync(string uri, string savePath = null, bool isPaused = false)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            ThrowEmptyAdd();
        }

        if (uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            var added = await this.torrentService.AddFromMagnetAsync(uri, null, savePath, isPaused);
            return GetGidFromInfoHash(added?.InfoHash);
        }

        var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? (this.configFileProvider?.MaxTorrentFileSizeBytes ?? 250L * 1024 * 1024);
        var bytes = await this.safeHttpClientService.DownloadBytesAsync(uri, maxSizeBytes: maxTorrentBytes);
        var parsed = this.torrentFileParser.Parse(bytes);
        var addedTorrent = await this.torrentService.AddFromParsedTorrentAsync(parsed, null, savePath, isPaused, bytes);
        return GetGidFromInfoHash(addedTorrent?.InfoHash);
    }

    private async Task<string> RemoveTorrentByGidAsync(string gid)
    {
        var toRemove = this.FindByGid(gid);
        if (toRemove == null)
        {
            ThrowGidNotFound(gid);
        }

        await this.torrentService.DeleteAsync(toRemove.Id, false);
        return gid ?? "OK";
    }

    private async Task PurgeDownloadResultsAsync()
    {
        var toPurge = this.torrentService.GetAll()
            .Where(IsAria2DownloadResult)
            .ToList();

        foreach (var torrent in toPurge)
        {
            await this.torrentService.DeleteAsync(torrent.Id, false);
        }
    }

    private async Task RemoveDownloadResultByGidAsync(string gid)
    {
        if (string.IsNullOrWhiteSpace(gid))
        {
            return;
        }

        var toRemoveRes = this.FindUniqueTorrentByGid(gid);
        if (toRemoveRes != null && IsAria2DownloadResult(toRemoveRes))
        {
            await this.torrentService.DeleteAsync(toRemoveRes.Id, false);
        }
    }

    private static bool IsAria2DownloadResult(Torrent torrent) =>
        torrent.Status is TorrentStatus.Completed or TorrentStatus.Error
        || (torrent.Status == TorrentStatus.Stopped && (torrent.Progress >= 1.0 || torrent.DateCompleted.HasValue));

    private async Task<string> PauseTorrentByGidAsync(string gid)
    {
        var toPause = this.FindByGid(gid);
        if (toPause == null)
        {
            ThrowGidNotFound(gid);
        }

        await this.torrentService.PauseAsync(toPause.Id);
        return gid ?? "OK";
    }

    private async Task<string> ResumeTorrentByGidAsync(string gid)
    {
        var toUnpause = this.FindByGid(gid);
        if (toUnpause == null)
        {
            ThrowGidNotFound(gid);
        }

        await this.torrentService.ResumeAsync(toUnpause.Id);
        return gid ?? "OK";
    }

    private async Task<int[]> ApplyChangeUriAsync(
        string gid,
        int fileIndex,
        IReadOnlyList<string> delUris,
        IReadOnlyList<string> addUris,
        int? position)
    {
        var torrent = this.FindByGid(gid);
        if (torrent == null)
        {
            ThrowGidNotFound(gid);
        }

        var fileCount = this.GetAria2FileCount(torrent);
        if (fileIndex < 1 || fileIndex > fileCount)
        {
            throw new InvalidOperationException("fileIndex is out of range");
        }

        var uris = this.GetDownloadUris(torrent);
        var before = uris.ToList();
        int? maxCount = this.trackerEntryRepository == null ? 1 : null;
        var (removed, added) = ApplyUriEdits(uris, delUris, addUris, position, maxCount);
        if (removed == 0 && added == 0)
        {
            return new[] { 0, 0 };
        }

        await this.PersistDownloadUrisAsync(torrent, before, uris);
        return new[] { removed, added };
    }

    private async Task<int[]> ChangeUriFromXmlAsync(XDocument xmlDoc)
    {
        var gid = string.Empty;
        var fileIndex = 1;
        var sawFileIndex = false;
        var delUris = new List<string>();
        var addUris = new List<string>();
        var arrayCount = 0;
        int? position = null;

        var paramList = xmlDoc?.Root?.Element("params")?.Elements("param");
        if (paramList != null)
        {
            foreach (var param in paramList)
            {
                var value = param.Element("value");
                if (value == null)
                {
                    continue;
                }

                if (value.Element("array") != null)
                {
                    var items = ReadXmlStringArray(value);
                    if (arrayCount == 0)
                    {
                        delUris = items;
                    }
                    else if (arrayCount == 1)
                    {
                        addUris = items;
                    }

                    arrayCount++;
                    continue;
                }

                var scalar = ReadXmlScalar(value);
                if (string.IsNullOrWhiteSpace(scalar)
                    || scalar.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isInteger = value.Element("int") != null
                    || value.Element("i4") != null
                    || value.Element("i8") != null;
                if (!isInteger && string.IsNullOrEmpty(gid))
                {
                    gid = scalar;
                    continue;
                }

                if (int.TryParse(scalar, out var number))
                {
                    if (!sawFileIndex)
                    {
                        fileIndex = number;
                        sawFileIndex = true;
                    }
                    else
                    {
                        position = number;
                    }
                }
            }
        }

        return await this.ApplyChangeUriAsync(gid, fileIndex, delUris, addUris, position);
    }

    private async Task PersistDownloadUrisAsync(Torrent torrent, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        this.SyncTrackerEntries(torrent.Id, after);

        var nextPrimary = after.Count > 0 ? after[0] : null;
        var previousPrimary = string.IsNullOrWhiteSpace(torrent.TrackerUrl) ? null : torrent.TrackerUrl;
        if (!string.Equals(previousPrimary, nextPrimary, StringComparison.Ordinal))
        {
            torrent.TrackerUrl = nextPrimary;
            await this.torrentService.UpdateAsync(torrent);
        }

        if (this.downloadEngine == null)
        {
            return;
        }

        var removedUris = before.Where(uri => !after.Any(candidate => string.Equals(candidate, uri, StringComparison.Ordinal))).ToList();
        var addedUris = after.Where(uri => !before.Any(candidate => string.Equals(candidate, uri, StringComparison.Ordinal))).ToList();
        if (removedUris.Count > 0)
        {
            await this.downloadEngine.RemoveTrackersAsync(torrent.Id, removedUris);
        }

        if (addedUris.Count > 0)
        {
            await this.downloadEngine.AddTrackersAsync(torrent.Id, addedUris);
        }
    }

    private void SyncTrackerEntries(int torrentId, IReadOnlyList<string> uris)
    {
        if (this.trackerEntryRepository == null)
        {
            return;
        }

        var existing = this.trackerEntryRepository.GetByTorrentId(torrentId)?.ToList() ?? new List<TrackerEntry>();
        var desired = new HashSet<string>(uris, StringComparer.Ordinal);
        foreach (var entry in existing)
        {
            var url = entry.Url?.Trim() ?? string.Empty;
            if (!desired.Contains(url))
            {
                this.trackerEntryRepository.Delete(entry.Id);
            }
        }

        for (var index = 0; index < uris.Count; index++)
        {
            var url = uris[index];
            var match = existing.FirstOrDefault(entry => string.Equals(entry.Url?.Trim(), url, StringComparison.Ordinal));
            if (match == null)
            {
                this.trackerEntryRepository.Insert(new TrackerEntry
                {
                    TorrentId = torrentId,
                    Url = url,
                    Tier = index,
                    Enabled = true,
                    AnnounceInterval = 1800,
                    NextAnnounce = DateTime.UtcNow.AddSeconds(1800),
                });
                continue;
            }

            if (match.Tier != index)
            {
                match.Tier = index;
                this.trackerEntryRepository.Update(match);
            }
        }
    }

    private List<string> GetDownloadUris(Torrent torrent)
    {
        var uris = new List<string>();
        if (this.trackerEntryRepository != null)
        {
            var entries = this.trackerEntryRepository.GetByTorrentId(torrent.Id);
            if (entries != null)
            {
                foreach (var entry in entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
                    .OrderBy(entry => entry.Tier)
                    .ThenBy(entry => entry.Id))
                {
                    var url = entry.Url.Trim();
                    if (!uris.Exists(existing => string.Equals(existing, url, StringComparison.Ordinal)))
                    {
                        uris.Add(url);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            var primary = torrent.TrackerUrl.Trim();
            if (!uris.Exists(existing => string.Equals(existing, primary, StringComparison.Ordinal)))
            {
                uris.Insert(0, primary);
            }
        }

        return uris;
    }

    private int GetAria2FileCount(Torrent torrent)
    {
        var files = this.torrentFileService?.GetFiles(torrent.Id);
        var count = 0;
        if (files != null)
        {
            foreach (var unused in files)
            {
                count++;
            }
        }

        return count > 0 ? count : 1;
    }

    private static (int Removed, int Added) ApplyUriEdits(
        List<string> uris,
        IReadOnlyList<string> delUris,
        IReadOnlyList<string> addUris,
        int? position,
        int? maxCount)
    {
        var removed = 0;
        if (delUris != null)
        {
            foreach (var del in delUris)
            {
                if (string.IsNullOrEmpty(del))
                {
                    continue;
                }

                var index = uris.FindIndex(uri => string.Equals(uri, del, StringComparison.Ordinal));
                if (index >= 0)
                {
                    uris.RemoveAt(index);
                    removed++;
                }
            }
        }

        var added = 0;
        var insertAt = position ?? uris.Count;
        if (insertAt < 0)
        {
            insertAt = 0;
        }

        if (insertAt > uris.Count)
        {
            insertAt = uris.Count;
        }

        if (addUris != null)
        {
            foreach (var add in addUris)
            {
                if (string.IsNullOrEmpty(add))
                {
                    continue;
                }

                if (uris.Exists(uri => string.Equals(uri, add, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (maxCount.HasValue && uris.Count >= maxCount.Value)
                {
                    continue;
                }

                var at = Math.Min(insertAt, uris.Count);
                uris.Insert(at, add);
                insertAt = at + 1;
                added++;
            }
        }

        return (removed, added);
    }

    private static bool TryReadJsonInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static List<string> ReadJsonStringArray(JsonElement element)
    {
        var list = new List<string>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = item.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                list.Add(text);
            }
        }

        return list;
    }

    private static List<string> ReadXmlStringArray(XElement value)
    {
        var list = new List<string>();
        var data = value?.Element("array")?.Element("data");
        if (data == null)
        {
            return list;
        }

        foreach (var item in data.Elements("value"))
        {
            var text = ReadXmlScalar(item);
            if (!string.IsNullOrEmpty(text))
            {
                list.Add(text);
            }
        }

        return list;
    }

    private static string ReadXmlScalar(XElement value)
    {
        if (value == null)
        {
            return null;
        }

        var typed = value.Element("string")
            ?? value.Element("int")
            ?? value.Element("i4")
            ?? value.Element("i8");
        if (typed != null)
        {
            return typed.Value;
        }

        if (!value.Elements().Any())
        {
            return value.Value;
        }

        return null;
    }

    private List<PeerInfo> GetPeersForTorrent(string gid)
    {
        var torrent = this.FindByGid(gid);
        if (torrent == null)
        {
            return new List<PeerInfo>();
        }

        var downloadTask = this.torrentService?.GetDownloadTask(torrent.Id);
        return (downloadTask?.GetPeers() ?? Array.Empty<PeerInfo>()).ToList();
    }

    private Dictionary<string, string> GetTorrentOptions(string gid)
    {
        var defaultDir = this.configService.DownloadDir ?? "/downloads";
        if (!string.IsNullOrWhiteSpace(gid))
        {
            var torrent = this.FindByGid(gid);
            if (torrent != null)
            {
                var optDir = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : defaultDir;
                return new Dictionary<string, string>
                {
                    { "dir", optDir },
                    { "max-download-limit", (torrent.DownloadLimit * 1024).ToString() },
                    { "max-upload-limit", (torrent.UploadLimit * 1024).ToString() },
                };
            }
        }

        return new Dictionary<string, string>
        {
            { "dir", defaultDir },
            { "max-download-limit", "0" },
            { "max-upload-limit", "0" },
        };
    }

    private Dictionary<string, string> GetGlobalOptions()
    {
        return new Dictionary<string, string>
        {
            { "dir", this.configService.DownloadDir ?? "/downloads" },
            { "max-overall-download-limit", (this.configService.MaxDownloadSpeedKbps * 1024).ToString() },
            { "max-overall-upload-limit", (this.configService.MaxUploadSpeedKbps * 1024).ToString() },
            { "max-download-limit", "0" },
            { "max-upload-limit", "0" },
        };
    }

    private async Task<int> MoveQueuePositionAsync(string gid, int offset, string how)
    {
        var allTorrents = this.torrentService.GetAll()
            .OrderBy(x => x.QueuePosition > 0 ? x.QueuePosition : x.Id)
            .ToList();

        if (string.IsNullOrWhiteSpace(gid))
        {
            return 0;
        }

        var clean = gid.Trim();
        var t = allTorrents.FirstOrDefault(x => x.InfoHash.StartsWith(clean, StringComparison.OrdinalIgnoreCase) || x.InfoHash.Equals(clean, StringComparison.OrdinalIgnoreCase));
        if (t == null)
        {
            return 0;
        }

        var count = allTorrents.Count;
        var currentIndex = allTorrents.FindIndex(x => x.Id == t.Id);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var normHow = how?.ToUpperInvariant() ?? "POS_CUR";
        int targetIndex;
        switch (normHow)
        {
            case "POS_SET":
                targetIndex = offset;
                break;
            case "POS_CUR":
                targetIndex = currentIndex + offset;
                break;
            case "POS_END":
                targetIndex = (count - 1) + offset;
                break;
            default:
                targetIndex = currentIndex + offset;
                break;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, count - 1));

        await this.torrentService.MoveQueueAsync(t.Id, targetIndex.ToString());
        return targetIndex;
    }

    private async Task ApplyOptionsAsync(string gid, IReadOnlyDictionary<string, string> options)
    {
        if (options == null || options.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(gid) && this.torrentService != null)
        {
            var t = this.FindByGid(gid);
            if (t != null)
            {
                var modified = false;
                if (options.TryGetValue("dir", out var dirStr) && !string.IsNullOrWhiteSpace(dirStr))
                {
                    if (!string.Equals(t.SavePath, dirStr, StringComparison.Ordinal))
                    {
                        await this.torrentService.SetLocationAsync(t.Id, dirStr, moveFiles: true);
                        t.SavePath = dirStr;
                        modified = true;
                    }
                }

                if (options.TryGetValue("max-download-limit", out var tdlStr) && int.TryParse(tdlStr, out var tdlBps))
                {
                    t.DownloadLimit = Aria2BpsToKbps(tdlBps);
                    modified = true;
                }

                if (options.TryGetValue("max-upload-limit", out var tulStr) && int.TryParse(tulStr, out var tulBps))
                {
                    t.UploadLimit = Aria2BpsToKbps(tulBps);
                    modified = true;
                }

                if (modified)
                {
                    await this.torrentService.UpdateAsync(t);
                }

                if (options.TryGetValue("select-file", out var sfStr) && this.torrentFileService != null && !string.IsNullOrWhiteSpace(sfStr))
                {
                    var files = this.torrentFileService.GetFiles(t.Id)?.ToList();
                    if (files != null && files.Count > 0)
                    {
                        var selectedIndices = ParseAria2FileIndices(sfStr);
                        for (var fIdx = 0; fIdx < files.Count; fIdx++)
                        {
                            var prio = selectedIndices.Contains(fIdx + 1) ? 1 : 0;
                            await this.torrentFileService.SetPriorityAsync(files[fIdx].Id, prio);
                        }
                    }
                }
            }
        }

        var updateDict = new Dictionary<string, object>();
        if (options.TryGetValue("max-overall-download-limit", out var dlOptStr) && int.TryParse(dlOptStr, out var dlBps))
        {
            updateDict["MaxDownloadSpeedKbps"] = Aria2BpsToKbps(dlBps);
        }

        if (options.TryGetValue("max-overall-upload-limit", out var ulOptStr) && int.TryParse(ulOptStr, out var ulBps))
        {
            updateDict["MaxUploadSpeedKbps"] = Aria2BpsToKbps(ulBps);
        }

        if (updateDict.Count > 0 && this.configService != null)
        {
            this.configService.SaveConfigDictionary(updateDict);
        }
    }

    private static (string SavePath, bool IsPaused) ExtractAddOptions(List<JsonElement> cleanParams)
    {
        string savePath = null;
        var isPaused = false;

        for (var i = 1; i < cleanParams.Count; i++)
        {
            if (cleanParams[i].ValueKind == JsonValueKind.Object)
            {
                var opts = cleanParams[i];
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

                break;
            }
        }

        return (savePath, isPaused);
    }

    private static (string SavePath, bool IsPaused) ExtractXmlAddOptions(XDocument xmlDoc)
    {
        var options = GetXmlRpcStructOptions(xmlDoc);
        options.TryGetValue("dir", out var savePath);
        var isPaused = options.TryGetValue("pause", out var pauseVal) &&
            (string.Equals(pauseVal, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(pauseVal, "1", StringComparison.OrdinalIgnoreCase));
        return (savePath, isPaused);
    }

    private static Dictionary<string, string> ExtractJsonOptions(JsonElement element)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    dict[prop.Name] = prop.Value.GetString();
                }
                else if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    dict[prop.Name] = prop.Value.GetRawText();
                }
                else if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    dict[prop.Name] = prop.Value.GetBoolean().ToString().ToLowerInvariant();
                }
            }
        }

        return dict;
    }

    private static (int Offset, int Num) ParseOffsetAndNum(List<JsonElement> cleanParams)
    {
        var offset = 0;
        var num = int.MaxValue;
        if (cleanParams.Count > 0)
        {
            if (cleanParams[0].ValueKind == JsonValueKind.Number && cleanParams[0].TryGetInt32(out var o))
            {
                offset = o;
            }
            else if (cleanParams[0].ValueKind == JsonValueKind.String && int.TryParse(cleanParams[0].GetString(), out var oStr))
            {
                offset = oStr;
            }
        }

        if (cleanParams.Count > 1)
        {
            if (cleanParams[1].ValueKind == JsonValueKind.Number && cleanParams[1].TryGetInt32(out var n))
            {
                num = n;
            }
            else if (cleanParams[1].ValueKind == JsonValueKind.String && int.TryParse(cleanParams[1].GetString(), out var nStr))
            {
                num = nStr;
            }
        }

        return (offset, num);
    }

    private static (int Offset, int Num) ParseXmlOffsetAndNum(List<string> stringParams)
    {
        var offset = stringParams.Count > 0 && int.TryParse(stringParams[0], out var o) ? o : 0;
        var num = stringParams.Count > 1 && int.TryParse(stringParams[1], out var n) ? n : int.MaxValue;
        return (offset, num);
    }

    private static XElement BuildXmlRpcStruct(IReadOnlyDictionary<string, string> dict)
    {
        var elem = new XElement("struct");
        foreach (var (k, v) in dict)
        {
            elem.Add(new XElement(
                "member",
                new XElement("name", k),
                new XElement("value", new XElement("string", v))));
        }

        return elem;
    }

    private static List<T> SliceList<T>(List<T> list, int offset, int num)
    {
        if (list == null || list.Count == 0 || num <= 0)
        {
            return new List<T>();
        }

        var total = list.Count;
        int startIndex;

        if (offset >= 0)
        {
            startIndex = offset;
        }
        else
        {
            startIndex = total + offset;
        }

        if (startIndex < 0)
        {
            startIndex = 0;
        }

        if (startIndex >= total)
        {
            return new List<T>();
        }

        var count = Math.Min(num, total - startIndex);
        return list.GetRange(startIndex, count);
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

    private static string GetFirstXmlRpcToken(XDocument xmlDoc)
    {
        var firstParam = xmlDoc?.Root?.Element("params")?.Elements("param").FirstOrDefault();
        if (firstParam != null)
        {
            var val = firstParam.Element("value");
            var strVal = val?.Element("string")?.Value ?? val?.Value;
            if (!string.IsNullOrWhiteSpace(strVal) && strVal.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
            {
                return strVal["token:".Length..];
            }

            var callsData = val?.Element("array")?.Element("data");
            if (callsData != null)
            {
                foreach (var callVal in callsData.Elements("value"))
                {
                    var callStruct = callVal.Element("struct");
                    if (callStruct != null)
                    {
                        foreach (var member in callStruct.Elements("member"))
                        {
                            if (string.Equals(member.Element("name")?.Value, "params", StringComparison.OrdinalIgnoreCase))
                            {
                                var subData = member.Element("value")?.Element("array")?.Element("data");
                                var firstSubVal = subData?.Elements("value").FirstOrDefault();
                                var subStr = firstSubVal?.Element("string")?.Value ?? firstSubVal?.Value;
                                if (!string.IsNullOrWhiteSpace(subStr) && subStr.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                                {
                                    return subStr["token:".Length..];
                                }
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private static List<string> GetXmlRpcStringParams(XDocument xmlDoc)
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
            if (val == null)
            {
                continue;
            }

            if (val.Element("array") is XElement arrayElem)
            {
                var data = arrayElem.Element("data");
                if (data != null)
                {
                    foreach (var item in data.Elements("value"))
                    {
                        var itemStr = item.Element("string")?.Value ?? item.Value;
                        if (!string.IsNullOrWhiteSpace(itemStr))
                        {
                            if (itemStr.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            list.Add(itemStr);
                        }
                    }
                }

                continue;
            }

            var strVal = val.Element("string")?.Value ?? val.Value;
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

    private static Dictionary<string, string> GetXmlRpcStructOptions(XDocument xmlDoc)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var structElem = xmlDoc?.Root?.Element("params")?.Elements("param")
            .Select(p => p.Element("value")?.Element("struct"))
            .FirstOrDefault(s => s != null);
        if (structElem != null)
        {
            foreach (var member in structElem.Elements("member"))
            {
                var name = member.Element("name")?.Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var val = member.Element("value")?.Element("string")?.Value
                        ?? member.Element("value")?.Element("int")?.Value
                        ?? member.Element("value")?.Element("i4")?.Value
                        ?? member.Element("value")?.Value;
                    if (val != null)
                    {
                        dict[name] = val;
                    }
                }
            }
        }

        return dict;
    }

    private static XElement BuildXmlRpcFilesArray(Torrent t, string downloadDir, ITorrentFileService torrentFileService, IDownloadTask downloadTask = null)
    {
        var dir = t.SavePath ?? downloadDir ?? "/downloads";
        var name = t.Name ?? string.Empty;

        var filesDataElem = new XElement("data");
        var files = torrentFileService?.GetFiles(t.Id)?.ToList();
        if (files != null && files.Count > 0)
        {
            TorrentFileProgressEnricher.Enrich(t, files, downloadTask);
            for (var i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var filePath = string.IsNullOrWhiteSpace(f.Path) ? name : f.Path;
                filesDataElem.Add(new XElement(
                    "value",
                    new XElement(
                        "struct",
                        new XElement("member", new XElement("name", "index"), new XElement("value", new XElement("string", (i + 1).ToString()))),
                        new XElement("member", new XElement("name", "path"), new XElement("value", new XElement("string", Path.Combine(dir, filePath)))),
                        new XElement("member", new XElement("name", "length"), new XElement("value", new XElement("string", f.Size.ToString()))),
                        new XElement("member", new XElement("name", "completedLength"), new XElement("value", new XElement("string", f.BytesCompleted.ToString()))),
                        new XElement("member", new XElement("name", "selected"), new XElement("value", new XElement("string", (f.Priority > 0).ToString().ToLowerInvariant()))),
                        new XElement("member", new XElement("name", "uris"), new XElement("value", new XElement("array", new XElement("data")))))));
            }
        }
        else
        {
            filesDataElem.Add(new XElement(
                "value",
                new XElement(
                    "struct",
                    new XElement("member", new XElement("name", "index"), new XElement("value", new XElement("string", "1"))),
                    new XElement("member", new XElement("name", "path"), new XElement("value", new XElement("string", Path.Combine(dir, name)))),
                    new XElement("member", new XElement("name", "length"), new XElement("value", new XElement("string", t.TotalSize.ToString()))),
                    new XElement("member", new XElement("name", "completedLength"), new XElement("value", new XElement("string", t.Downloaded.ToString()))),
                    new XElement("member", new XElement("name", "selected"), new XElement("value", new XElement("string", "true"))),
                    new XElement("member", new XElement("name", "uris"), new XElement("value", new XElement("array", new XElement("data")))))));
        }

        return new XElement("array", filesDataElem);
    }

    private static string MapToAria2Status(Torrent t)
    {
        return t.Status switch
        {
            TorrentStatus.Downloading => "active",
            TorrentStatus.Seeding => "active",
            TorrentStatus.Paused => "paused",
            TorrentStatus.Stopped => t.Progress >= 1.0 || t.DateCompleted.HasValue ? "complete" : "paused",
            TorrentStatus.Completed => "complete",
            TorrentStatus.Error => "error",
            TorrentStatus.Stalled => "paused",
            _ => "waiting",
        };
    }

    private static XElement BuildXmlRpcTorrentStruct(Torrent t, string downloadDir, ITorrentFileService torrentFileService)
    {
        var gid = GetGidFromInfoHash(t.InfoHash);
        var status = MapToAria2Status(t);
        var dir = t.SavePath ?? downloadDir ?? "/downloads";
        var name = t.Name ?? string.Empty;

        return new XElement(
            "struct",
            new XElement("member", new XElement("name", "gid"), new XElement("value", new XElement("string", gid))),
            new XElement("member", new XElement("name", "status"), new XElement("value", new XElement("string", status))),
            new XElement("member", new XElement("name", "totalLength"), new XElement("value", new XElement("string", t.TotalSize.ToString()))),
            new XElement("member", new XElement("name", "completedLength"), new XElement("value", new XElement("string", t.Downloaded.ToString()))),
            new XElement("member", new XElement("name", "uploadLength"), new XElement("value", new XElement("string", t.Uploaded.ToString()))),
            new XElement("member", new XElement("name", "downloadSpeed"), new XElement("value", new XElement("string", t.DownloadSpeed.ToString()))),
            new XElement("member", new XElement("name", "uploadSpeed"), new XElement("value", new XElement("string", t.UploadSpeed.ToString()))),
            new XElement("member", new XElement("name", "infoHash"), new XElement("value", new XElement("string", t.InfoHash.ToLowerInvariant()))),
            new XElement("member", new XElement("name", "numSeeders"), new XElement("value", new XElement("string", t.Seeders.ToString()))),
            new XElement("member", new XElement("name", "connections"), new XElement("value", new XElement("string", (t.Leechers + t.Seeders).ToString()))),
            new XElement("member", new XElement("name", "dir"), new XElement("value", new XElement("string", dir))),
            new XElement(
                "member",
                new XElement("name", "bittorrent"),
                new XElement(
                    "value",
                    new XElement(
                        "struct",
                        new XElement(
                            "member",
                            new XElement("name", "info"),
                            new XElement(
                                "value",
                                new XElement(
                                    "struct",
                                    new XElement("member", new XElement("name", "name"), new XElement("value", new XElement("string", name)))))),
                        new XElement("member", new XElement("name", "mode"), new XElement("value", new XElement("string", "multi")))))),
            new XElement(
                "member",
                new XElement("name", "files"),
                new XElement(
                    "value",
                    BuildXmlRpcFilesArray(t, downloadDir, torrentFileService))));
    }

    private static XElement BuildXmlRpcTorrentArray(IEnumerable<Torrent> torrents, string downloadDir, ITorrentFileService torrentFileService)
    {
        var dataElem = new XElement("data");
        foreach (var t in torrents)
        {
            dataElem.Add(new XElement("value", BuildXmlRpcTorrentStruct(t, downloadDir, torrentFileService)));
        }

        return new XElement("array", dataElem);
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

        return this.Content(doc.ToString(SaveOptions.DisableFormatting), "text/xml", Encoding.UTF8);
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
                                new XElement("value", new XElement("string", faultString))))))));

        return this.Content(doc.ToString(SaveOptions.DisableFormatting), "text/xml", Encoding.UTF8);
    }

    private Torrent FindUniqueTorrentByGid(string gid)
    {
        var matches = this.FindAllByGid(gid);
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"GID {gid.Trim()} is not unique.");
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private List<Torrent> FindAllByGid(string gid)
    {
        if (string.IsNullOrWhiteSpace(gid))
        {
            return new List<Torrent>();
        }

        var clean = gid.Trim();
        return this.torrentService.GetAll()
            .Where(t => !string.IsNullOrWhiteSpace(t.InfoHash)
                        && (t.InfoHash.Equals(clean, StringComparison.OrdinalIgnoreCase)
                            || t.InfoHash.StartsWith(clean, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private Torrent FindByGid(string gid)
    {
        return this.FindAllByGid(gid).FirstOrDefault();
    }

    private static int Aria2BpsToKbps(int bps)
    {
        if (bps <= 0)
        {
            return 0;
        }

        return (bps + 1023) / 1024;
    }

    private static void ThrowEmptyAdd() =>
        throw new InvalidOperationException("No files to download.");

    private static void ThrowGidNotFound(string gid) =>
        throw new InvalidOperationException($"GID {gid?.Trim() ?? string.Empty} is not found");

    private static string GetGidFromInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            throw new InvalidOperationException("Failed to add download.");
        }

        return infoHash.Length >= 16 ? infoHash[..16] : infoHash;
    }

    private Dictionary<string, object> MapTorrentToAria2(Torrent t)
    {
        var gid = GetGidFromInfoHash(t.InfoHash);
        var status = MapToAria2Status(t);

        var defaultDir = this.configService.DownloadDir ?? "/downloads";
        var rawFiles = this.torrentFileService.GetFiles(t.Id)?.ToList();
        object[] filesArray;
        if (rawFiles is { Count: > 0 })
        {
            var task = this.torrentService?.GetDownloadTask(t.Id);
            TorrentFileProgressEnricher.Enrich(t, rawFiles, task);
            filesArray = rawFiles.Select((f, idx) => (object)new
            {
                index = (idx + 1).ToString(),
                path = Path.Combine(t.SavePath ?? defaultDir, f.Path ?? string.Empty),
                length = f.Size.ToString(),
                completedLength = f.BytesCompleted.ToString(),
                selected = f.Priority > 0 ? "true" : "false",
                uris = Array.Empty<object>(),
            }).ToArray();
        }
        else
        {
            filesArray = new object[]
            {
                new
                {
                    index = "1",
                    path = Path.Combine(t.SavePath ?? defaultDir, t.Name ?? string.Empty),
                    length = t.TotalSize.ToString(),
                    completedLength = t.Downloaded.ToString(),
                    selected = "true",
                    uris = Array.Empty<object>(),
                },
            };
        }

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
            { "dir", t.SavePath ?? (this.configService.DownloadDir ?? "/downloads") },
            {
                "bittorrent", new Dictionary<string, object>
                {
                    { "info", new Dictionary<string, string> { { "name", t.Name ?? string.Empty } } },
                    { "mode", "multi" },
                }
            },
            {
                "files", filesArray
            },
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
