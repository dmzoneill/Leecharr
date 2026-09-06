// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Deluge;

[ApiController]
[Route("json")]
public class DelugeJsonRpcController : ControllerBase
{
    private static readonly RpcSessionStore AuthenticatedSessions = new();

    private static readonly JsonSerializerOptions DelugeJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITorrentService torrentService;
    private readonly ITorrentFileService torrentFileService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DelugeJsonRpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null,
        IDiskProvider diskProvider = null)
    {
        this.torrentService = torrentService;
        this.torrentFileService = torrentFileService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.diskProvider = diskProvider;
    }

    private bool IsDelugeAuthenticated()
    {
        if (this.configFileProvider != null && !this.configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            return true;
        }

        if (this.Request.Cookies.TryGetValue("deluge-session", out var sid) && !string.IsNullOrWhiteSpace(sid))
        {
            if (AuthenticatedSessions.IsValid(sid))
            {
                return true;
            }
        }

        return false;
    }

    private IActionResult DelugeResult(object value)
    {
        return new JsonResult(value, DelugeJsonOptions);
    }

    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody] JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            var responses = new List<object>();
            foreach (var item in root.EnumerateArray())
            {
                var singleResult = await this.ProcessSingleRpcAsync(item);
                if (singleResult is JsonResult jsonResult)
                {
                    responses.Add(jsonResult.Value);
                }
                else if (singleResult is ObjectResult objectResult)
                {
                    responses.Add(objectResult.Value);
                }
                else
                {
                    responses.Add(new { result = (object)null, error = "Unknown RPC result", id = (object)null });
                }
            }

            return this.DelugeResult(responses);
        }

        return await this.ProcessSingleRpcAsync(root);
    }

    private async Task<IActionResult> ProcessSingleRpcAsync(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var methodProp) || methodProp.ValueKind != JsonValueKind.String)
        {
            return this.DelugeResult(new { result = (object)null, error = "Invalid RPC request", id = (object)null });
        }

        var method = methodProp.GetString() ?? string.Empty;
        object id = 1;
        if (root.TryGetProperty("id", out var idElem))
        {
            if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt64(out var numId))
            {
                id = numId;
            }
            else if (idElem.ValueKind == JsonValueKind.String)
            {
                id = idElem.GetString();
            }
        }

        var paramsElem = root.TryGetProperty("params", out var p) ? p : default;

        try
        {
            var lowerMethod = method.ToLowerInvariant();

            if (lowerMethod == "auth.login")
            {
                var providedPassword = string.Empty;
                if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 &&
                    paramsElem[0].ValueKind == JsonValueKind.String)
                {
                    providedPassword = paramsElem[0].GetString();
                }

                var loginSuccess = false;
                if (this.configFileProvider == null || !this.configFileProvider.AuthenticationEnabled)
                {
                    loginSuccess = true;
                }
                else if (!string.IsNullOrWhiteSpace(this.configFileProvider.ApiKey) &&
                         RpcAuthenticationHelper.FixedTimeEquals(providedPassword, this.configFileProvider.ApiKey))
                {
                    loginSuccess = true;
                }

                if (loginSuccess)
                {
                    var sid = Guid.NewGuid().ToString("N");
                    AuthenticatedSessions.SetSession(sid, DateTime.UtcNow.AddDays(7));
                    this.Response.Cookies.Append("deluge-session", sid, new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/",
                    });

                    return this.DelugeResult(new { result = true, error = (object)null, id });
                }

                return this.DelugeResult(new { result = false, error = (object)null, id });
            }

            if (lowerMethod == "auth.check_session")
            {
                var isAuth = this.IsDelugeAuthenticated();
                return this.DelugeResult(new { result = isAuth, error = (object)null, id });
            }

            if (lowerMethod == "auth.delete_session")
            {
                if (this.Request.Cookies.TryGetValue("deluge-session", out var sid) && !string.IsNullOrWhiteSpace(sid))
                {
                    AuthenticatedSessions.RemoveSession(sid);
                    this.Response.Cookies.Delete("deluge-session");
                }

                return this.DelugeResult(new { result = true, error = (object)null, id });
            }

            if (!this.IsDelugeAuthenticated())
            {
                return this.StatusCode(StatusCodes.Status401Unauthorized, new { result = (object)null, error = new { message = "Not authenticated", code = 1 }, id });
            }

            switch (lowerMethod)
            {
                case "web.connected":
                case "web.connect":
                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "system.listmethods":
                case "system.list_methods":
                case "daemon.get_method_list":
                case "system.get_methods":
                    return this.DelugeResult(new
                    {
                        result = new[]
                        {
                            "auth.login",
                            "auth.check_session",
                            "auth.delete_session",
                            "web.connected",
                            "web.connect",
                            "web.get_hosts",
                            "web.get_host_status",
                            "web.update_ui",
                            "web.get_plugins",
                            "web.get_installed_plugins",
                            "web.get_config",
                            "web.get_torrents_status",
                            "core.get_version",
                            "daemon.get_version",
                            "daemon.info",
                            "system.listMethods",
                            "system.list_methods",
                            "system.get_methods",
                            "core.get_config",
                            "core.get_config_values",
                            "core.get_config_value",
                            "core.get_session_status",
                            "core.get_free_space",
                            "core.get_path_free_space",
                            "core.get_torrents_status",
                            "core.get_torrent_status",
                            "core.add_torrent_file",
                            "core.add_torrent_magnet",
                            "core.add_torrent_url",
                            "core.pause_torrent",
                            "core.pause_torrents",
                            "core.resume_torrent",
                            "core.resume_torrents",
                            "core.remove_torrent",
                            "core.remove_torrents",
                            "core.force_recheck",
                            "core.set_torrent_options",
                            "core.move_storage",
                            "core.get_filter_tree",
                            "core.get_enabled_plugins",
                            "core.get_available_plugins",
                            "label.get_labels",
                            "label.set_torrent",
                        },
                        error = (object)null,
                        id,
                    });

                case "daemon.get_version":
                case "daemon.info":
                case "core.get_version":
                case "web.get_version":
                    return this.DelugeResult(new { result = "2.1.1", error = (object)null, id });

                case "label.get_labels":
                    var labels = this.categoryService.GetAll().Select(c => c.Name).ToArray();
                    return this.DelugeResult(new { result = labels, error = (object)null, id });

                case "label.add":
                case "label.add_label":
                    var newLabel = GetFirstStringParam(paramsElem);
                    if (!string.IsNullOrWhiteSpace(newLabel))
                    {
                        var existing = this.categoryService.GetByName(newLabel);
                        if (existing == null)
                        {
                            this.categoryService.Add(new Category
                            {
                                Name = newLabel,
                                SavePath = global::System.IO.Path.Combine(this.configService.DownloadDir ?? "/downloads", newLabel),
                            });
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "label.remove":
                    var labelToRemove = GetFirstStringParam(paramsElem);
                    if (!string.IsNullOrEmpty(labelToRemove))
                    {
                        var cat = this.categoryService.GetByName(labelToRemove);
                        if (cat != null)
                        {
                            this.categoryService.Delete(cat.Id);
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "label.get_options":
                    {
                        var labelOptName = GetFirstStringParam(paramsElem);
                        var targetCat = !string.IsNullOrWhiteSpace(labelOptName) ? this.categoryService.GetByName(labelOptName) : null;
                        var labelOpts = new Dictionary<string, object>
                        {
                            ["apply_max"] = false,
                            ["max_download_speed"] = targetCat?.DefaultDownloadLimit ?? -1,
                            ["max_upload_speed"] = targetCat?.DefaultUploadLimit ?? -1,
                            ["apply_queue"] = false,
                            ["stop_at_ratio"] = (targetCat?.TargetRatio ?? 0) > 0,
                            ["stop_ratio"] = targetCat?.TargetRatio ?? 2.0,
                            ["remove_at_ratio"] = false,
                            ["apply_move_completed"] = !string.IsNullOrWhiteSpace(targetCat?.SavePath),
                            ["move_completed_path"] = targetCat?.SavePath ?? string.Empty,
                        };
                        return this.DelugeResult(new { result = labelOpts, error = (object)null, id });
                    }

                case "label.set_options":
                    {
                        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                        {
                            var lName = paramsElem[0].GetString();
                            var lOptions = paramsElem[1];
                            if (!string.IsNullOrWhiteSpace(lName) && lOptions.ValueKind == JsonValueKind.Object)
                            {
                                var cat = this.categoryService.GetByName(lName) ?? new Category { Name = lName };
                                if (lOptions.TryGetProperty("move_completed_path", out var mcpProp) && mcpProp.ValueKind == JsonValueKind.String)
                                {
                                    cat.SavePath = mcpProp.GetString();
                                }

                                if (lOptions.TryGetProperty("stop_ratio", out var srProp) && srProp.ValueKind == JsonValueKind.Number)
                                {
                                    cat.TargetRatio = srProp.GetDouble();
                                }

                                if (cat.Id > 0)
                                {
                                    this.categoryService.Update(cat);
                                }
                                else
                                {
                                    this.categoryService.Add(cat);
                                }
                            }
                        }

                        return this.DelugeResult(new { result = true, error = (object)null, id });
                    }

                case "label.set_torrent":
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var torrentHash = paramsElem[0].GetString();
                        var labelName = paramsElem[1].GetString();
                        if (!string.IsNullOrEmpty(torrentHash))
                        {
                            var torrent = this.torrentService.GetByInfoHash(torrentHash);
                            if (torrent != null)
                            {
                                torrent.Category = labelName;
                                await this.torrentService.UpdateAsync(torrent);
                            }
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.get_enabled_plugins":
                case "web.get_plugins":
                case "web.get_installed_plugins":
                case "core.get_available_plugins":
                    return this.DelugeResult(new { result = new[] { "Label", "Extractor", "Execute", "AutoAdd", "Blocklist", "Scheduler", "Stats" }, error = (object)null, id });

                case "web.get_hosts":
                    return this.DelugeResult(new { result = new object[] { new object[] { "1", "127.0.0.1", 58846, "Connected" } }, error = (object)null, id });

                case "web.get_host_status":
                    return this.DelugeResult(new { result = new object[] { "1", "Connected", "2.1.1" }, error = (object)null, id });

                case "web.update_ui":
                    var allTorrentsForUi = this.torrentService.GetAll().ToList();
                    var filteredTorrents = allTorrentsForUi;

                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 1)
                    {
                        var filterElem = paramsElem[1];
                        if (filterElem.ValueKind == JsonValueKind.Object)
                        {
                            if (filterElem.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                            {
                                var targetLabel = labelProp.GetString();
                                if (!string.IsNullOrWhiteSpace(targetLabel) && !string.Equals(targetLabel, "All", StringComparison.OrdinalIgnoreCase))
                                {
                                    filteredTorrents = filteredTorrents.Where(t => string.Equals(t.Category, targetLabel, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase)).ToList();
                                }
                            }
                        }
                    }

                    HashSet<string> uiKeys = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Array)
                    {
                        uiKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var elem in paramsElem[0].EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                uiKeys.Add(elem.GetString());
                            }
                        }
                    }

                    var torrentDict = new Dictionary<string, Dictionary<string, object>>();
                    foreach (var t in filteredTorrents)
                    {
                        torrentDict[t.InfoHash.ToLowerInvariant()] = this.MapTorrentToDelugeStatus(t, uiKeys);
                    }

                    return this.DelugeResult(new
                    {
                        result = new
                        {
                            connected = true,
                            torrents = torrentDict,
                            filters = new
                            {
                                state = new object[]
                                {
                                    new object[] { "All", allTorrentsForUi.Count },
                                    new object[] { "Downloading", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Downloading) },
                                    new object[] { "Seeding", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Seeding) },
                                    new object[] { "Active", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding) },
                                    new object[] { "Paused", allTorrentsForUi.Count(t => t.Status == TorrentStatus.Paused) },
                                },
                                label = this.categoryService.GetAll().Select(c => new object[] { c.Name, allTorrentsForUi.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase)) }).ToArray(),
                            },
                            stats = new
                            {
                                max_download = this.configService.MaxDownloadSpeedKbps,
                                max_upload = this.configService.MaxUploadSpeedKbps,
                                num_connections = allTorrentsForUi.Sum(t => t.Leechers + t.Seeders),
                                upload_rate = allTorrentsForUi.Sum(t => t.UploadSpeed),
                                download_rate = allTorrentsForUi.Sum(t => t.DownloadSpeed),
                                free_space = this.GetDriveFreeSpace(this.configService.DownloadDir)
                            },
                        },
                        error = (object)null,
                        id,
                    });

                case "core.get_config":
                case "web.get_config":
                    return this.DelugeResult(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "download_location", this.configService.DownloadDir ?? "/downloads" },
                            { "move_completed", false },
                            { "move_completed_path", this.configService.DownloadDir ?? "/downloads" },
                            { "max_connections_global", this.configService.MaxGlobalConnections },
                            { "max_download_speed", (double)this.configService.MaxDownloadSpeedKbps },
                            { "max_upload_speed", (double)this.configService.MaxUploadSpeedKbps },
                            { "max_active_limit", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
                            { "max_active_downloading", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
                            { "max_active_seeding", this.configService.MaxActiveUploads > 0 ? this.configService.MaxActiveUploads : 5 },
                            { "compact_allocation", false },
                            { "prioritize_first_last_pieces", true },
                        },
                        error = (object)null,
                        id,
                    });

                case "core.get_config_values":
                    var fullConfig = new Dictionary<string, object>
                    {
                        { "download_location", this.configService.DownloadDir ?? "/downloads" },
                        { "move_completed", false },
                        { "move_completed_path", this.configService.DownloadDir ?? "/downloads" },
                        { "max_connections_global", this.configService.MaxGlobalConnections },
                        { "max_download_speed", (double)this.configService.MaxDownloadSpeedKbps },
                        { "max_upload_speed", (double)this.configService.MaxUploadSpeedKbps },
                        { "max_active_limit", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
                        { "max_active_downloading", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
                        { "max_active_seeding", this.configService.MaxActiveUploads > 0 ? this.configService.MaxActiveUploads : 5 },
                        { "compact_allocation", false },
                        { "prioritize_first_last_pieces", true },
                    };

                    var requestedConfig = new Dictionary<string, object>();
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Array)
                    {
                        foreach (var keyElem in paramsElem[0].EnumerateArray())
                        {
                            var k = keyElem.GetString();
                            if (!string.IsNullOrEmpty(k))
                            {
                                requestedConfig[k] = fullConfig.TryGetValue(k, out var val) ? val : null;
                            }
                        }
                    }

                    return this.DelugeResult(new { result = requestedConfig, error = (object)null, id });

                case "core.get_config_value":
                    var singleCfgKey = GetFirstStringParam(paramsElem);
                    var singleFullConfig = new Dictionary<string, object>
                    {
                        { "download_location", this.configService.DownloadDir ?? "/downloads" },
                        { "move_completed", false },
                        { "move_completed_path", this.configService.DownloadDir ?? "/downloads" },
                        { "max_connections_global", this.configService.MaxGlobalConnections },
                        { "max_download_speed", (double)this.configService.MaxDownloadSpeedKbps },
                        { "max_upload_speed", (double)this.configService.MaxUploadSpeedKbps },
                        { "max_active_limit", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
                        { "max_active_downloading", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
                        { "max_active_seeding", this.configService.MaxActiveUploads > 0 ? this.configService.MaxActiveUploads : 5 },
                        { "compact_allocation", false },
                        { "prioritize_first_last_pieces", true },
                    };
                    singleFullConfig.TryGetValue(singleCfgKey ?? string.Empty, out var foundVal);
                    return this.DelugeResult(new { result = foundVal, error = (object)null, id });

                case "core.get_session_status":
                    var allT = this.torrentService.GetAll().ToList();
                    return this.DelugeResult(new
                    {
                        result = new Dictionary<string, object>
                        {
                            { "download_rate", allT.Sum(t => t.DownloadSpeed) },
                            { "upload_rate", allT.Sum(t => t.UploadSpeed) },
                            { "num_peers", allT.Sum(t => t.Seeders + t.Leechers) },
                            { "payload_download_rate", allT.Sum(t => t.DownloadSpeed) },
                            { "payload_upload_rate", allT.Sum(t => t.UploadSpeed) },
                            { "total_download", allT.Sum(t => t.Downloaded) },
                            { "total_upload", allT.Sum(t => t.Uploaded) },
                        },
                        error = (object)null,
                        id,
                    });

                case "core.get_free_space":
                case "core.get_path_free_space":
                    var rawPath = GetFirstStringParam(paramsElem);
                    var targetPath = !string.IsNullOrWhiteSpace(rawPath) ? rawPath : (this.configService.DownloadDir ?? "/downloads");
                    return this.DelugeResult(new { result = this.GetDriveFreeSpace(targetPath), error = (object)null, id });

                case "core.get_torrents_status":
                case "web.get_torrents_status":
                    var torrents = this.torrentService.GetAll().ToList();

                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Object)
                    {
                        var filterObj = paramsElem[0];
                        if (filterObj.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                        {
                            var targetLabel = labelProp.GetString();
                            if (!string.IsNullOrWhiteSpace(targetLabel))
                            {
                                torrents = torrents.Where(t => string.Equals(t.Category, targetLabel, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase)).ToList();
                            }
                        }

                        if (filterObj.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
                        {
                            var stateStr = stateProp.GetString()?.ToLowerInvariant();
                            if (!string.IsNullOrWhiteSpace(stateStr) && stateStr != "all")
                            {
                                torrents = torrents.Where(t => t.Status.ToString().ToLowerInvariant() == stateStr).ToList();
                            }
                        }
                    }

                    HashSet<string> requestedKeys = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 1 && paramsElem[1].ValueKind == JsonValueKind.Array)
                    {
                        requestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var elem in paramsElem[1].EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                requestedKeys.Add(elem.GetString());
                            }
                        }
                    }
                    else if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Array)
                    {
                        requestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var elem in paramsElem[0].EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                requestedKeys.Add(elem.GetString());
                            }
                        }
                    }

                    var resultDict = new Dictionary<string, Dictionary<string, object>>();

                    foreach (var torrent in torrents)
                    {
                        resultDict[torrent.InfoHash.ToLowerInvariant()] = this.MapTorrentToDelugeStatus(torrent, requestedKeys);
                    }

                    return this.DelugeResult(new { result = resultDict, error = (object)null, id });

                case "web.get_torrent_status":
                case "core.get_torrent_status":
                    var targetHash = GetFirstStringParam(paramsElem);
                    var found = this.torrentService.GetByInfoHash(targetHash);
                    if (found == null)
                    {
                        return this.DelugeResult(new { result = (object)null, error = "Torrent not found", id });
                    }

                    HashSet<string> singleTorrentKeys = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 1 && paramsElem[1].ValueKind == JsonValueKind.Array)
                    {
                        singleTorrentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var elem in paramsElem[1].EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                singleTorrentKeys.Add(elem.GetString());
                            }
                        }
                    }

                    return this.DelugeResult(new { result = this.MapTorrentToDelugeStatus(found, singleTorrentKeys), error = (object)null, id });

                case "core.add_torrent_file":
                    string addedHash = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var b64 = paramsElem[1].GetString();
                        if (!string.IsNullOrWhiteSpace(b64))
                        {
                            var bytes = Convert.FromBase64String(b64);
                            var parsed = this.torrentFileParser.Parse(bytes);
                            var isPaused = false;
                            string savePath = null;
                            string category = null;
                            double? targetRatio = null;

                            if (paramsElem.GetArrayLength() >= 3 && paramsElem[2].ValueKind == JsonValueKind.Object)
                            {
                                var opts = paramsElem[2];
                                if (opts.TryGetProperty("add_paused", out var ap))
                                {
                                    isPaused = ap.GetBoolean();
                                }

                                if (opts.TryGetProperty("download_location", out var dl))
                                {
                                    savePath = dl.GetString();
                                }
                                else if (opts.TryGetProperty("move_completed_path", out var mcp))
                                {
                                    savePath = mcp.GetString();
                                }

                                if (opts.TryGetProperty("label", out var lbl))
                                {
                                    category = lbl.GetString();
                                }

                                if (opts.TryGetProperty("stop_ratio", out var sr) && sr.ValueKind == JsonValueKind.Number)
                                {
                                    targetRatio = sr.GetDouble();
                                }
                            }

                            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                            if (added != null && targetRatio.HasValue && targetRatio.Value > 0)
                            {
                                added.TargetRatio = targetRatio.Value;
                                await this.torrentService.UpdateAsync(added);
                            }

                            addedHash = added?.InfoHash;
                        }
                    }

                    return this.DelugeResult(new { result = addedHash, error = (object)null, id });

                case "core.add_torrent_magnet":
                    string magnetHash = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
                    {
                        var magnetUri = paramsElem[0].GetString();
                        var isPaused = false;
                        string savePath = null;
                        string category = null;
                        double? targetRatio = null;

                        if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = paramsElem[1];
                            if (opts.TryGetProperty("add_paused", out var ap))
                            {
                                isPaused = ap.GetBoolean();
                            }

                            if (opts.TryGetProperty("download_location", out var dl))
                            {
                                savePath = dl.GetString();
                            }
                            else if (opts.TryGetProperty("move_completed_path", out var mcp))
                            {
                                savePath = mcp.GetString();
                            }

                            if (opts.TryGetProperty("label", out var lbl))
                            {
                                category = lbl.GetString();
                            }

                            if (opts.TryGetProperty("stop_ratio", out var sr) && sr.ValueKind == JsonValueKind.Number)
                            {
                                targetRatio = sr.GetDouble();
                            }
                        }

                        var added = await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, isPaused);
                        if (added != null && targetRatio.HasValue && targetRatio.Value > 0)
                        {
                            added.TargetRatio = targetRatio.Value;
                            await this.torrentService.UpdateAsync(added);
                        }

                        magnetHash = added?.InfoHash;
                    }

                    return this.DelugeResult(new { result = magnetHash, error = (object)null, id });

                case "core.add_torrent_url":
                    string urlHash = null;
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
                    {
                        var url = paramsElem[0].GetString();
                        var isPaused = false;
                        string savePath = null;
                        string category = null;

                        if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.Object)
                        {
                            var opts = paramsElem[1];
                            if (opts.TryGetProperty("add_paused", out var ap))
                            {
                                isPaused = ap.GetBoolean();
                            }

                            if (opts.TryGetProperty("download_location", out var dl))
                            {
                                savePath = dl.GetString();
                            }

                            if (opts.TryGetProperty("label", out var lbl))
                            {
                                category = lbl.GetString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                            {
                                var added = await this.torrentService.AddFromMagnetAsync(url, category, savePath, isPaused);
                                urlHash = added?.InfoHash;
                            }
                            else
                            {
                                var bytes = await this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: 10 * 1024 * 1024);
                                var parsed = this.torrentFileParser.Parse(bytes);
                                var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                                urlHash = added?.InfoHash;
                            }
                        }
                    }

                    return this.DelugeResult(new { result = urlHash, error = (object)null, id });

                case "core.pause_torrent":
                case "core.pause_torrents":
                    var pauseHashes = ExtractHashes(paramsElem);
                    foreach (var hash in pauseHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.PauseAsync(t.Id);
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.resume_torrent":
                case "core.resume_torrents":
                    var resumeHashes = ExtractHashes(paramsElem);
                    foreach (var hash in resumeHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.ResumeAsync(t.Id);
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.remove_torrent":
                case "core.remove_torrents":
                    var removeHashes = ExtractHashes(paramsElem);
                    var deleteData = GetSecondBoolParam(paramsElem);
                    foreach (var hash in removeHashes)
                    {
                        var toRemove = this.torrentService.GetByInfoHash(hash);
                        if (toRemove != null)
                        {
                            await this.torrentService.DeleteAsync(toRemove.Id, deleteData);
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.force_recheck":
                    var recheckHashes = ExtractHashes(paramsElem);
                    foreach (var hash in recheckHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.ForceRecheckAsync(t.Id);
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.move_storage":
                    List<string> moveHashes = null;
                    string dest = null;

                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        moveHashes = ExtractHashes(paramsElem[0]);
                        dest = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;
                    }
                    else if (paramsElem.ValueKind == JsonValueKind.Object)
                    {
                        if (paramsElem.TryGetProperty("torrent_ids", out var tids))
                        {
                            moveHashes = ExtractHashes(tids);
                        }
                        else if (paramsElem.TryGetProperty("torrent_id", out var tid))
                        {
                            moveHashes = ExtractHashes(tid);
                        }

                        if (paramsElem.TryGetProperty("dest", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                        {
                            dest = dProp.GetString();
                        }
                        else if (paramsElem.TryGetProperty("destination", out var destProp) && destProp.ValueKind == JsonValueKind.String)
                        {
                            dest = destProp.GetString();
                        }
                    }

                    if (moveHashes != null && !string.IsNullOrWhiteSpace(dest))
                    {
                        foreach (var hash in moveHashes)
                        {
                            var t = this.torrentService.GetByInfoHash(hash) ??
                                (int.TryParse(hash, out var tid) ? this.torrentService.Get(tid) : null);
                            if (t != null)
                            {
                                await this.torrentService.SetLocationAsync(t.Id, dest, moveFiles: true);
                            }
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.set_torrent_options":
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var optHashes = ExtractHashes(paramsElem[0]);
                        var opts = paramsElem[1];
                        foreach (var hash in optHashes)
                        {
                            var t = this.torrentService.GetByInfoHash(hash) ??
                                (int.TryParse(hash, out var tid) ? this.torrentService.Get(tid) : null);
                            if (t != null)
                            {
                                string newPath = null;
                                if (opts.TryGetProperty("download_location", out var dl))
                                {
                                    newPath = dl.GetString();
                                }
                                else if (opts.TryGetProperty("move_completed_path", out var mcp))
                                {
                                    newPath = mcp.GetString();
                                }

                                if (!string.IsNullOrWhiteSpace(newPath))
                                {
                                    await this.torrentService.SetLocationAsync(t.Id, newPath, moveFiles: true);
                                    t.SavePath = newPath;
                                }

                                var hasOtherUpdates = false;

                                if (opts.TryGetProperty("max_download_speed", out var mds))
                                {
                                    if (mds.ValueKind == JsonValueKind.Number && mds.TryGetDouble(out var dlVal))
                                    {
                                        t.DownloadLimit = dlVal > 0 ? (int)Math.Round(dlVal) : 0;
                                        hasOtherUpdates = true;
                                    }
                                    else if (mds.ValueKind == JsonValueKind.Null)
                                    {
                                        t.DownloadLimit = 0;
                                        hasOtherUpdates = true;
                                    }
                                }

                                if (opts.TryGetProperty("max_upload_speed", out var mus))
                                {
                                    if (mus.ValueKind == JsonValueKind.Number && mus.TryGetDouble(out var ulVal))
                                    {
                                        t.UploadLimit = ulVal > 0 ? (int)Math.Round(ulVal) : 0;
                                        hasOtherUpdates = true;
                                    }
                                    else if (mus.ValueKind == JsonValueKind.Null)
                                    {
                                        t.UploadLimit = 0;
                                        hasOtherUpdates = true;
                                    }
                                }

                                if (opts.TryGetProperty("stop_ratio", out var sr) && sr.ValueKind == JsonValueKind.Number)
                                {
                                    t.TargetRatio = sr.GetDouble();
                                    hasOtherUpdates = true;
                                }

                                if (opts.TryGetProperty("file_priorities", out var fp) && fp.ValueKind == JsonValueKind.Array)
                                {
                                    var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                    var fIdx = 0;
                                    foreach (var prioElem in fp.EnumerateArray())
                                    {
                                        if (fIdx < files.Count && prioElem.TryGetInt32(out var prio))
                                        {
                                            await this.torrentFileService.SetPriorityAsync(files[fIdx].Id, prio);
                                        }

                                        fIdx++;
                                    }
                                }

                                if (hasOtherUpdates)
                                {
                                    await this.torrentService.UpdateAsync(t);
                                }
                            }
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.set_torrent_file_priorities":
                    if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
                    {
                        var hash = paramsElem[0].GetString();
                        var priosElem = paramsElem[1];
                        if (!string.IsNullOrWhiteSpace(hash) && priosElem.ValueKind == JsonValueKind.Array)
                        {
                            var t = this.torrentService.GetByInfoHash(hash);
                            if (t != null)
                            {
                                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                                var fIdx = 0;
                                foreach (var prioElem in priosElem.EnumerateArray())
                                {
                                    if (fIdx < files.Count && prioElem.TryGetInt32(out var prio))
                                    {
                                        await this.torrentFileService.SetPriorityAsync(files[fIdx].Id, prio);
                                    }

                                    fIdx++;
                                }
                            }
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "web.disconnect":
                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.queue_top":
                    var topHashes = ExtractHashes(paramsElem);
                    foreach (var hash in topHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "top");
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.queue_up":
                    var upHashes = ExtractHashes(paramsElem);
                    foreach (var hash in upHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "up");
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.queue_down":
                    var downHashes = ExtractHashes(paramsElem);
                    foreach (var hash in downHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "down");
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.queue_bottom":
                    var bottomHashes = ExtractHashes(paramsElem);
                    foreach (var hash in bottomHashes)
                    {
                        var t = this.torrentService.GetByInfoHash(hash);
                        if (t != null)
                        {
                            await this.torrentService.MoveQueueAsync(t.Id, "bottom");
                        }
                    }

                    return this.DelugeResult(new { result = true, error = (object)null, id });

                case "core.get_filter_tree":
                    var allTorrents = this.torrentService.GetAll().ToList();
                    var stateCounts = new Dictionary<string, int>
                    {
                        { "All", allTorrents.Count },
                        { "Active", allTorrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding) },
                        { "Downloading", allTorrents.Count(t => t.Status == TorrentStatus.Downloading) },
                        { "Seeding", allTorrents.Count(t => t.Status == TorrentStatus.Seeding) },
                        { "Paused", allTorrents.Count(t => t.Status == TorrentStatus.Paused) },
                    };

                    var filterTree = new Dictionary<string, object>
                    {
                        { "state", stateCounts.Select(kvp => new object[] { kvp.Key, kvp.Value }).ToList() },
                        { "label", this.categoryService.GetAll().Select(c => new object[] { c.Name, allTorrents.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase)) }).ToList() },
                    };

                    return this.DelugeResult(new { result = filterTree, error = (object)null, id });

                default:
                    this.logger.Debug("Unhandled Deluge RPC method: {0}", method);
                    return this.DelugeResult(new
                    {
                        result = (object)null,
                        error = new { message = $"Unknown method: {method}", code = 1 },
                        id,
                    });
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling Deluge RPC method: {0}", method);
            return this.DelugeResult(new { result = (object)null, error = ex.Message, id });
        }
    }

    private static string GetFirstStringParam(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
        {
            var first = parameters[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return first.GetString();
            }
        }
        else if (parameters.ValueKind == JsonValueKind.String)
        {
            return parameters.GetString();
        }

        return null;
    }

    private static bool GetSecondBoolParam(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 1)
        {
            var second = parameters[1];
            if (second.ValueKind == JsonValueKind.True || second.ValueKind == JsonValueKind.False)
            {
                return second.GetBoolean();
            }
        }

        return false;
    }

    private static List<string> ExtractHashes(JsonElement parameters)
    {
        var hashes = new List<string>();
        if (parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in parameters.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    hashes.Add(item.GetString());
                }
                else if (item.ValueKind == JsonValueKind.Number)
                {
                    hashes.Add(item.ToString());
                }
                else if (item.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sub in item.EnumerateArray())
                    {
                        if (sub.ValueKind == JsonValueKind.String)
                        {
                            hashes.Add(sub.GetString());
                        }
                        else if (sub.ValueKind == JsonValueKind.Number)
                        {
                            hashes.Add(sub.ToString());
                        }
                    }
                }
            }
        }
        else if (parameters.ValueKind == JsonValueKind.String)
        {
            hashes.Add(parameters.GetString());
        }
        else if (parameters.ValueKind == JsonValueKind.Number)
        {
            hashes.Add(parameters.ToString());
        }

        return hashes;
    }

    private Dictionary<string, object> MapTorrentToDelugeStatus(Torrent t, ISet<string> requestedKeys = null)
    {
        var stateStr = t.Status switch
        {
            TorrentStatus.Downloading => "Downloading",
            TorrentStatus.Seeding => "Seeding",
            TorrentStatus.Paused => "Paused",
            TorrentStatus.Queued => "Queued",
            TorrentStatus.Checking => "Checking",
            TorrentStatus.Error => "Error",
            _ => "Paused",
        };

        var needsFiles = requestedKeys == null || requestedKeys.Count == 0 ||
            requestedKeys.Contains("files") || requestedKeys.Contains("file_priorities") || requestedKeys.Contains("file_progress") || requestedKeys.Contains("num_files");

        List<Dictionary<string, object>> filesList;
        List<int> filePriorities;
        List<double> fileProgress;
        int numFiles;

        if (needsFiles)
        {
            var files = this.torrentFileService.GetFiles(t.Id).ToList();
            var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
            TorrentFileProgressEnricher.Enrich(t, files, downloadTask);
            numFiles = files.Count;
            filesList = files.Select((f, idx) => new Dictionary<string, object>
            {
                { "index", idx },
                { "path", f.Path },
                { "size", f.Size },
                { "offset", f.PieceOffset },
            }).ToList();

            filePriorities = files.Select(f => f.Priority).ToList();
            fileProgress = files.Select(f => f.Progress).ToList();
        }
        else
        {
            filesList = new List<Dictionary<string, object>>();
            filePriorities = new List<int>();
            fileProgress = new List<double>();
            numFiles = 0;
        }

        var status = new Dictionary<string, object>
        {
            { "name", t.Name },
            { "hash", t.InfoHash },
            { "state", stateStr },
            { "progress", t.Progress * 100.0 },
            { "total_size", t.TotalSize },
            { "total_done", t.Downloaded },
            { "total_uploaded", t.Uploaded },
            { "total_payload_download", t.Downloaded },
            { "total_payload_upload", t.Uploaded },
            { "download_payload_rate", t.DownloadSpeed },
            { "upload_payload_rate", t.UploadSpeed },
            { "eta", t.Eta },
            { "ratio", t.Ratio },
            { "num_seeds", t.Seeders },
            { "total_seeds", t.Seeders },
            { "num_peers", t.Seeders + t.Leechers },
            { "total_peers", t.Seeders + t.Leechers },
            { "num_files", numFiles },
            { "files", filesList },
            { "file_priorities", filePriorities },
            { "file_progress", fileProgress },
            { "save_path", t.SavePath ?? string.Empty },
            { "label", t.Category ?? string.Empty },
            { "is_finished", t.Status == TorrentStatus.Seeding || t.Progress >= 1.0 },
            { "is_seed", t.Status == TorrentStatus.Seeding },
            { "paused", t.Status == TorrentStatus.Paused },
            { "time_added", new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() },
            { "all_time_download", t.Downloaded },
            { "active_time", (long)(DateTime.UtcNow - t.DateAdded).TotalSeconds },
            { "seeding_time", t.SeedingTimeSeconds },
            { "message", t.Status == TorrentStatus.Error ? "Error" : "OK" },
            { "is_auto_managed", true },
            { "stop_at_ratio", t.TargetRatio > 0 },
            { "remove_at_ratio", false },
            { "stop_ratio", t.TargetRatio },
            { "max_download_speed", t.DownloadLimit <= 0 ? -1.0 : (double)t.DownloadLimit },
            { "max_upload_speed", t.UploadLimit <= 0 ? -1.0 : (double)t.UploadLimit },
            { "private", t.IsPrivate },
            { "is_private", t.IsPrivate },
        };

        if (requestedKeys != null && requestedKeys.Count > 0)
        {
            var filtered = new Dictionary<string, object>();
            foreach (var key in requestedKeys)
            {
                if (status.TryGetValue(key, out var val))
                {
                    filtered[key] = val;
                }
            }

            return filtered;
        }

        return status;
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
