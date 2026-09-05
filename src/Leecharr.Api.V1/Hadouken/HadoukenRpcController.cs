// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Hadouken;

public class HadoukenRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;
}

[ApiController]
public class HadoukenRpcController : ControllerBase
{
    private static readonly RpcSessionStore AuthenticatedSessions = new();
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ITorrentFileService torrentFileService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public HadoukenRpcController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        IConfigService configService,
        ITorrentFileService torrentFileService = null,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null)
    {
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.configService = configService;
        this.torrentFileService = torrentFileService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
    }

    private bool IsHadoukenAuthenticated()
    {
        if (this.configFileProvider != null && !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        var token = this.Request.Headers["X-Hadouken-Token"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            token = this.Request.Query["token"].ToString();
        }

        if (!string.IsNullOrEmpty(token) && AuthenticatedSessions.IsValid(token))
        {
            return true;
        }

        return false;
    }

    [HttpPost]
    [Route("api/hadouken")]
    [Route("api/rpc")]
    [Route("hadouken/api")]
    [Route("hadouken/rpc")]
    [Route("hadouken")]
    public async Task<IActionResult> HandleRpc([FromBody] HadoukenRpcRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return this.Ok(new { result = (object)null, error = "Invalid request", id = (object)1 });
        }

        var id = request.Id ?? 1;

        try
        {
            var lowerMethod = request.Method.ToLowerInvariant();

            if (lowerMethod == "auth.generate_token" || lowerMethod == "auth.login")
            {
                var password = string.Empty;
                if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0 &&
                    request.Params[0].ValueKind == JsonValueKind.String)
                {
                    password = request.Params[0].GetString();
                }

                var success = false;
                if (this.configFileProvider == null || !this.configFileProvider.AuthenticationEnabled)
                {
                    success = true;
                }
                else if (!string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey) &&
                         string.Equals(password, this.configFileProvider.ApiKey, StringComparison.Ordinal))
                {
                    success = true;
                }

                if (success)
                {
                    var token = Guid.NewGuid().ToString("N");
                    AuthenticatedSessions.SetSession(token, DateTime.UtcNow.AddDays(7));
                    return this.Ok(new { result = token, error = (object)null, id });
                }

                return this.Ok(new { result = (object)null, error = "Invalid credentials", id });
            }

            if (lowerMethod == "auth.logout")
            {
                var token = this.Request.Headers["X-Hadouken-Token"].ToString();
                if (string.IsNullOrEmpty(token))
                {
                    token = this.Request.Query["token"].ToString();
                }

                if (string.IsNullOrEmpty(token) && request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0 &&
                    request.Params[0].ValueKind == JsonValueKind.String)
                {
                    token = request.Params[0].GetString();
                }

                if (!string.IsNullOrEmpty(token))
                {
                    AuthenticatedSessions.RemoveSession(token);
                }

                return this.Ok(new { result = true, error = (object)null, id });
            }

            if (!this.IsHadoukenAuthenticated())
            {
                return this.StatusCode(StatusCodes.Status401Unauthorized, new { result = (object)null, error = "Unauthorized", id });
            }

            switch (lowerMethod)
            {
                case "core.getsysteminfo":
                case "core.get_system_info":
                    return this.Ok(new
                    {
                        result = new
                        {
                            committish = "5.3.0",
                            branch = "master",
                            versions = new Dictionary<string, string>
                            {
                                { "hadouken", "5.3.0" },
                                { "libtorrent", "1.2.14" }
                            },
                        },
                        error = (object)null,
                        id,
                    });

                case "webui.getsettings":
                case "webui.get_settings":
                    return this.Ok(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "bittorrent.default_save_path", this.configService.DownloadDir ?? "/downloads" },
                        },
                        error = (object)null,
                        id,
                    });

                case "webui.list":
                    var allTorrents = this.torrentService.GetAll().ToList();
                    var torrentRows = new List<object[]>();
                    foreach (var t in allTorrents)
                    {
                        var statusFlag = 128; // Loaded

                        if (t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding)
                        {
                            statusFlag = 1 | 128;
                        }
                        else if (t.Status == TorrentStatus.Paused)
                        {
                            statusFlag = 32 | 128;
                        }
                        else if (t.Status == TorrentStatus.Stopped)
                        {
                            statusFlag = 128;
                        }
                        else if (t.Status == TorrentStatus.Checking)
                        {
                            statusFlag = 2 | 128;
                        }
                        else if (t.Status == TorrentStatus.Error)
                        {
                            statusFlag = 16 | 128;
                        }
                        else if (t.Status == TorrentStatus.Queued)
                        {
                            statusFlag = 64 | 128;
                        }

                        var addedUnix = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds();
                        var completedUnix = t.DateCompleted.HasValue ? new DateTimeOffset(t.DateCompleted.Value).ToUnixTimeSeconds() : 0;
                        var modifiedUnix = t.LastActive.HasValue ? new DateTimeOffset(t.LastActive.Value).ToUnixTimeSeconds() : addedUnix;

                        torrentRows.Add(new object[]
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

                    return this.Ok(new
                    {
                        result = new
                        {
                            torrents = torrentRows,
                            torrentc = "1",
                        },
                        error = (object)null,
                        id,
                    });

                case "webui.addtorrent":
                case "webui.add_torrent":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var type = request.Params[0].GetString();
                        var data = request.Params[1].GetString();
                        string savePath = null;
                        string category = null;
                        var isPaused = false;

                        if (request.Params.GetArrayLength() >= 3 && request.Params[2].ValueKind == JsonValueKind.Object)
                        {
                            var opts = request.Params[2];
                            if (opts.TryGetProperty("save_path", out var spProp))
                            {
                                savePath = spProp.GetString();
                            }

                            if (opts.TryGetProperty("label", out var lblProp))
                            {
                                category = lblProp.GetString();
                            }

                            if (opts.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array && tagsProp.GetArrayLength() > 0)
                            {
                                category = tagsProp[0].GetString();
                            }

                            if (opts.TryGetProperty("paused", out var pProp))
                            {
                                isPaused = pProp.GetBoolean();
                            }
                        }

                        if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(data))
                        {
                            var bytes = Convert.FromBase64String(data);
                            var parsed = this.torrentFileParser.Parse(bytes);
                            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                            return this.Ok(new { result = added?.InfoHash, error = (object)null, id });
                        }
                        else if (string.Equals(type, "url", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(data))
                        {
                            if (data.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await this.torrentService.AddFromMagnetAsync(data, category, savePath, isPaused);
                                return this.Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                            else
                            {
                                var bytes = await this.safeHttpClientService.DownloadBytesAsync(data, maxSizeBytes: 10 * 1024 * 1024);
                                var parsed = this.torrentFileParser.Parse(bytes);
                                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                                return this.Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                        }
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                case "webui.perform":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var action = request.Params[0].GetString()?.ToLowerInvariant();
                        var hashesElem = request.Params[1];
                        var targetHashes = new List<string>();
                        if (hashesElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var h in hashesElem.EnumerateArray())
                            {
                                if (h.ValueKind == JsonValueKind.String)
                                {
                                    targetHashes.Add(h.GetString());
                                }
                            }
                        }
                        else if (hashesElem.ValueKind == JsonValueKind.String)
                        {
                            targetHashes.Add(hashesElem.GetString());
                        }

                        foreach (var targetHash in targetHashes)
                        {
                            var t = this.torrentService.GetByInfoHash(targetHash);
                            if (t != null)
                            {
                                switch (action)
                                {
                                    case "pause":
                                        await this.torrentService.PauseAsync(t.Id);
                                        break;
                                    case "resume":
                                    case "start":
                                        await this.torrentService.ResumeAsync(t.Id);
                                        break;
                                    case "recheck":
                                        await this.torrentService.ForceRecheckAsync(t.Id);
                                        break;
                                    case "remove":
                                        await this.torrentService.DeleteAsync(t.Id, false);
                                        break;
                                    case "removedata":
                                        await this.torrentService.DeleteAsync(t.Id, true);
                                        break;
                                }
                            }
                        }
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                case "torrents.get_files":
                case "webui.getfiles":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0 && this.torrentFileService != null)
                    {
                        var reqHash = request.Params[0].GetString();
                        if (!string.IsNullOrWhiteSpace(reqHash))
                        {
                            var t = this.torrentService.GetByInfoHash(reqHash);
                            if (t != null)
                            {
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                var downloadTask = this.torrentService.GetDownloadTask(t.Id);
                                TorrentFileProgressEnricher.Enrich(t, files, downloadTask);

                                var fileRes = files.Select((f, idx) => new
                                {
                                    index = idx,
                                    path = f.Path,
                                    size = f.Size,
                                    progress = f.Progress,
                                    priority = f.Priority,
                                });

                                return this.Ok(new { result = fileRes, error = (object)null, id });
                            }
                        }
                    }

                    return this.Ok(new { result = new object[] { }, error = (object)null, id });

                case "core.getversion":
                case "hadouken.getversion":
                    return this.Ok(new { result = "5.3.0", error = (object)null, id });

                case "torrents.adduri":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 0)
                    {
                        var uri = request.Params[0].GetString();
                        string savePath = null;
                        string category = null;
                        var isPaused = false;

                        if (request.Params.GetArrayLength() > 1 && request.Params[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = request.Params[1];
                            if (opts.TryGetProperty("save_path", out var spProp))
                            {
                                savePath = spProp.GetString();
                            }

                            if (opts.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array && tagsProp.GetArrayLength() > 0)
                            {
                                category = tagsProp[0].GetString();
                            }

                            if (opts.TryGetProperty("paused", out var pProp))
                            {
                                isPaused = pProp.GetBoolean();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            if (uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await this.torrentService.AddFromMagnetAsync(uri, category, savePath, isPaused);
                                return this.Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                            else
                            {
                                var bytes = await this.safeHttpClientService.DownloadBytesAsync(uri, maxSizeBytes: 10 * 1024 * 1024);
                                var parsed = this.torrentFileParser.Parse(bytes);
                                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                                return this.Ok(new { result = added?.InfoHash, error = (object)null, id });
                            }
                        }
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                case "torrents.pause":
                    var hashToPause = GetFirstStringParam(request.Params);
                    var tPause = this.torrentService.GetByInfoHash(hashToPause);
                    if (tPause != null)
                    {
                        await this.torrentService.PauseAsync(tPause.Id);
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                case "torrents.resume":
                    var hashToResume = GetFirstStringParam(request.Params);
                    var tResume = this.torrentService.GetByInfoHash(hashToResume);
                    if (tResume != null)
                    {
                        await this.torrentService.ResumeAsync(tResume.Id);
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                case "torrents.delete":
                    var hashToDelete = GetFirstStringParam(request.Params);
                    var tDelete = this.torrentService.GetByInfoHash(hashToDelete);
                    if (tDelete != null)
                    {
                        var deleteData = false;
                        if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() > 1)
                        {
                            var p1 = request.Params[1];
                            if (p1.ValueKind == JsonValueKind.True)
                            {
                                deleteData = true;
                            }
                            else if (p1.ValueKind == JsonValueKind.Object && p1.TryGetProperty("delete_data", out var ddProp) && ddProp.ValueKind == JsonValueKind.True)
                            {
                                deleteData = true;
                            }
                        }

                        await this.torrentService.DeleteAsync(tDelete.Id, deleteData);
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                case "torrents.set_props":
                case "torrents.setprops":
                    if (request.Params.ValueKind == JsonValueKind.Array && request.Params.GetArrayLength() >= 2)
                    {
                        var targetHash = request.Params[0].GetString();
                        var tProps = this.torrentService.GetByInfoHash(targetHash);
                        if (tProps != null && request.Params[1].ValueKind == JsonValueKind.Object)
                        {
                            var propsObj = request.Params[1];
                            if (propsObj.TryGetProperty("tags", out var tProp) && tProp.ValueKind == JsonValueKind.Array && tProp.GetArrayLength() > 0)
                            {
                                tProps.Category = tProp[0].GetString();
                            }

                            if (propsObj.TryGetProperty("download_limit", out var dlProp))
                            {
                                tProps.DownloadLimit = dlProp.GetInt32();
                            }

                            if (propsObj.TryGetProperty("upload_limit", out var ulProp))
                            {
                                tProps.UploadLimit = ulProp.GetInt32();
                            }

                            await this.torrentService.UpdateAsync(tProps);
                        }
                    }

                    return this.Ok(new { result = true, error = (object)null, id });

                default:
                    this.logger.Debug("Unhandled Hadouken RPC method: {0}", request.Method);
                    return this.Ok(new { result = true, error = (object)null, id });
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error in Hadouken RPC: {0}", request.Method);
            return this.Ok(new { result = (object)null, error = ex.Message, id });
        }
    }

    private static string GetFirstStringParam(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
        {
            return parameters[0].GetString() ?? string.Empty;
        }

        if (parameters.ValueKind == JsonValueKind.String)
        {
            return parameters.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
