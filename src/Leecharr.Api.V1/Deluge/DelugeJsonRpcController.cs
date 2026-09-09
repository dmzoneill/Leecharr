// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

        if (this.HttpContext?.Items.TryGetValue("deluge-session", out var itemSid) == true && itemSid is string s && AuthenticatedSessions.IsValid(s))
        {
            return true;
        }

        if (this.Request?.Cookies.TryGetValue("_session_id", out var sid1) == true && !string.IsNullOrWhiteSpace(sid1))
        {
            if (AuthenticatedSessions.IsValid(sid1))
            {
                return true;
            }
        }

        if (this.Request?.Cookies.TryGetValue("deluge-session", out var sid2) == true && !string.IsNullOrWhiteSpace(sid2))
        {
            if (AuthenticatedSessions.IsValid(sid2))
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
        if (root.ValueKind != JsonValueKind.Object)
        {
            return this.DelugeResult(new { result = (object)null, error = new { message = "Invalid JSON-RPC format", code = 1 }, id = (object)null });
        }

        var methodElem = root.TryGetProperty("method", out var m) ? m : default;
        var method = methodElem.ValueKind == JsonValueKind.String ? methodElem.GetString() : string.Empty;

        object id = null;
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
                return this.HandleAuthLogin(paramsElem, id);
            }

            if (lowerMethod == "auth.check_session")
            {
                return this.HandleAuthCheckSession(id);
            }

            if (lowerMethod == "auth.delete_session")
            {
                return this.HandleAuthDeleteSession(id);
            }

            if (!this.IsDelugeAuthenticated())
            {
                return this.DelugeResult(new { result = (object)null, error = new { message = "Not authenticated", code = 1 }, id });
            }

            if (lowerMethod.StartsWith("core."))
            {
                return await this.DispatchCoreRpcAsync(lowerMethod, paramsElem, id);
            }

            if (lowerMethod.StartsWith("web."))
            {
                return await this.DispatchWebRpcAsync(lowerMethod, paramsElem, id);
            }

            if (lowerMethod.StartsWith("daemon.") || lowerMethod.StartsWith("system."))
            {
                return await this.DispatchDaemonRpcAsync(lowerMethod, paramsElem, id);
            }

            if (lowerMethod.StartsWith("label."))
            {
                return await this.DispatchLabelRpcAsync(lowerMethod, paramsElem, id);
            }

            return this.HandleUnknownMethod(method, id);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling Deluge RPC method: {0}", method);
            return this.DelugeResult(new { result = (object)null, error = ex.Message, id });
        }
    }

    private async Task<IActionResult> DispatchCoreRpcAsync(string method, JsonElement args, object id)
    {
        return method switch
        {
            "core.get_version" => this.HandleGetVersion(id),
            "core.get_enabled_plugins" or "core.get_available_plugins" => this.HandleGetPlugins(id),
            "core.enable_plugin" or "core.disable_plugin" => this.HandleTogglePlugin(id),
            "core.get_config" => this.HandleCoreGetConfig(id),
            "core.get_config_values" => this.HandleCoreGetConfigValues(args, id),
            "core.get_config_value" => this.HandleCoreGetConfigValue(args, id),
            "core.set_config" or "core.set_config_values" => this.HandleCoreSetConfig(args, id),
            "core.get_session_status" => this.HandleCoreGetSessionStatus(id),
            "core.get_free_space" or "core.get_path_free_space" or "core.get_free_space_bytes" => this.HandleCoreGetFreeSpace(args, id),
            "core.get_torrents_status" => this.HandleGetTorrentsStatus(args, id, isWeb: false),
            "core.get_torrent_status" => this.HandleGetTorrentStatus(args, id),
            "core.add_torrent_file" or "core.add_torrent_file_async" => await this.HandleCoreAddTorrentFileAsync(args, id),
            "core.add_torrent_magnet" => await this.HandleCoreAddTorrentMagnetAsync(args, id),
            "core.add_torrent_url" => await this.HandleCoreAddTorrentUrlAsync(args, id),
            "core.pause_torrent" or "core.pause_torrents" or "core.pause_all_torrents" => await this.HandleCorePauseTorrentsAsync(method, args, id),
            "core.resume_torrent" or "core.resume_torrents" or "core.resume_all_torrents" => await this.HandleCoreResumeTorrentsAsync(method, args, id),
            "core.remove_torrent" or "core.remove_torrents" => await this.HandleCoreRemoveTorrentsAsync(method, args, id),
            "core.force_recheck" => await this.HandleCoreForceRecheckAsync(args, id),
            "core.force_reannounce" or "core.reannounce" => await this.HandleCoreForceReannounceAsync(args, id),
            "core.move_storage" => await this.HandleCoreMoveStorageAsync(args, id),
            "core.set_torrent_options" => await this.HandleCoreSetTorrentOptionsAsync(args, id),
            "core.set_torrent_file_priorities" => await this.HandleCoreSetTorrentFilePrioritiesAsync(args, id),
            "core.rename_files" => await this.HandleCoreRenameFilesAsync(args, id),
            "core.queue_top" or "core.queue_up" or "core.queue_down" or "core.queue_bottom" => await this.HandleCoreQueueAsync(method, args, id),
            "core.get_filter_tree" => this.HandleGetFilterTree(id),
            _ => this.HandleUnknownMethod(method, id),
        };
    }

    private async Task<IActionResult> DispatchWebRpcAsync(string method, JsonElement args, object id)
    {
        return method switch
        {
            "web.connected" or "web.connect" => this.HandleWebConnected(id),
            "web.get_version" => this.HandleGetVersion(id),
            "web.get_plugins" or "web.get_installed_plugins" => this.HandleGetPlugins(id),
            "web.get_hosts" => this.HandleWebGetHosts(id),
            "web.get_host_status" => this.HandleWebGetHostStatus(id),
            "web.update_ui" => this.HandleWebUpdateUi(args, id),
            "web.get_config" => this.HandleCoreGetConfig(id),
            "web.set_config" => this.HandleCoreSetConfig(args, id),
            "web.get_torrents_status" => this.HandleGetTorrentsStatus(args, id, isWeb: true),
            "web.get_torrent_status" => this.HandleGetTorrentStatus(args, id),
            "web.upload_torrent" => await this.HandleWebUploadTorrentAsync(args, id),
            "web.get_torrent_info" => await this.HandleWebGetTorrentInfoAsync(args, id),
            "web.add_torrents" => await this.HandleWebAddTorrentsAsync(args, id),
            "web.disconnect" => this.HandleWebDisconnect(id),
            "web.get_filter_tree" => this.HandleGetFilterTree(id),
            _ => this.HandleUnknownMethod(method, id),
        };
    }

    private Task<IActionResult> DispatchDaemonRpcAsync(string method, JsonElement args, object id)
    {
        var result = method switch
        {
            "system.listmethods" or "system.list_methods" or "daemon.get_method_list" or "system.get_methods" => this.HandleSystemListMethods(id),
            "daemon.get_version" or "daemon.info" => this.HandleGetVersion(id),
            _ => this.HandleUnknownMethod(method, id),
        };

        return Task.FromResult(result);
    }

    private async Task<IActionResult> DispatchLabelRpcAsync(string method, JsonElement args, object id)
    {
        return method switch
        {
            "label.get_labels" => this.HandleLabelGetLabels(id),
            "label.get_torrents" => this.HandleLabelGetTorrents(args, id),
            "label.add" or "label.add_label" => this.HandleLabelAdd(args, id),
            "label.remove" => this.HandleLabelRemove(args, id),
            "label.get_options" => this.HandleLabelGetOptions(args, id),
            "label.set_options" => this.HandleLabelSetOptions(args, id),
            "label.set_torrent" => await this.HandleLabelSetTorrentAsync(args, id),
            _ => this.HandleUnknownMethod(method, id),
        };
    }

    private IActionResult HandleAuthLogin(JsonElement paramsElem, object id)
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
            if (this.HttpContext != null)
            {
                this.HttpContext.Items["deluge-session"] = sid;
            }

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            };
            this.Response?.Cookies.Append("_session_id", sid, cookieOptions);
            this.Response?.Cookies.Append("deluge-session", sid, cookieOptions);

            return this.DelugeResult(new { result = true, error = (object)null, id });
        }

        return this.DelugeResult(new { result = false, error = (object)null, id });
    }

    private IActionResult HandleAuthCheckSession(object id)
    {
        var isAuth = this.IsDelugeAuthenticated();
        return this.DelugeResult(new { result = isAuth, error = (object)null, id });
    }

    private IActionResult HandleAuthDeleteSession(object id)
    {
        if (this.HttpContext?.Items.TryGetValue("deluge-session", out var itemSid) == true && itemSid is string s)
        {
            AuthenticatedSessions.RemoveSession(s);
            this.HttpContext.Items.Remove("deluge-session");
        }

        if (this.Request?.Cookies.TryGetValue("_session_id", out var sid1) == true && !string.IsNullOrWhiteSpace(sid1))
        {
            AuthenticatedSessions.RemoveSession(sid1);
        }

        if (this.Request?.Cookies.TryGetValue("deluge-session", out var sid2) == true && !string.IsNullOrWhiteSpace(sid2))
        {
            AuthenticatedSessions.RemoveSession(sid2);
        }

        this.Response?.Cookies.Delete("_session_id");
        this.Response?.Cookies.Delete("deluge-session");

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleWebConnected(object id)
    {
        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleSystemListMethods(object id)
    {
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
                            "web.set_config",
                            "web.get_torrents_status",
                            "web.upload_torrent",
                            "web.get_torrent_info",
                            "web.add_torrents",
                            "core.get_version",
                            "daemon.get_version",
                            "daemon.info",
                            "system.listMethods",
                            "system.list_methods",
                            "system.get_methods",
                            "core.get_config",
                            "core.get_config_values",
                            "core.get_config_value",
                            "core.set_config",
                            "core.set_config_values",
                            "core.get_session_status",
                            "core.get_free_space",
                            "core.get_path_free_space",
                            "core.get_free_space_bytes",
                            "core.get_torrents_status",
                            "core.get_torrent_status",
                            "core.add_torrent_file",
                            "core.add_torrent_magnet",
                            "core.add_torrent_url",
                            "core.pause_torrent",
                            "core.pause_torrents",
                            "core.pause_all_torrents",
                            "core.resume_torrent",
                            "core.resume_torrents",
                            "core.resume_all_torrents",
                            "core.remove_torrent",
                            "core.remove_torrents",
                            "core.force_recheck",
                            "core.force_reannounce",
                            "core.set_torrent_options",
                            "core.set_torrent_file_priorities",
                            "core.rename_files",
                            "core.move_storage",
                            "core.queue_top",
                            "core.queue_up",
                            "core.queue_down",
                            "core.queue_bottom",
                            "core.get_filter_tree",
                            "web.get_filter_tree",
                            "core.get_enabled_plugins",
                            "core.get_available_plugins",
                            "core.enable_plugin",
                            "core.disable_plugin",
                            "label.get_labels",
                            "label.get_torrents",
                            "label.set_torrent",
                            "label.add",
                            "label.add_label",
                            "label.remove",
                            "label.get_options",
                            "label.set_options",
                        },
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleGetVersion(object id)
    {
        return this.DelugeResult(new { result = "2.1.1", error = (object)null, id });
    }

    private IActionResult HandleLabelGetLabels(object id)
    {
        var labels = this.categoryService.GetAll().Select(c => c.Name).ToArray();
        return this.DelugeResult(new { result = labels, error = (object)null, id });
    }

    private IActionResult HandleLabelGetTorrents(JsonElement paramsElem, object id)
    {
        {
            var targetLabel = GetFirstStringParam(paramsElem);
            var allLabelTorrents = this.torrentService.GetAll();
            IEnumerable<Torrent> matching;
            if (targetLabel == null || string.Equals(targetLabel, "All", StringComparison.OrdinalIgnoreCase))
            {
                matching = allLabelTorrents;
            }
            else if (string.IsNullOrEmpty(targetLabel) || string.Equals(targetLabel, "no_label", StringComparison.OrdinalIgnoreCase) || string.Equals(targetLabel, "None", StringComparison.OrdinalIgnoreCase))
            {
                matching = allLabelTorrents.Where(t => string.IsNullOrWhiteSpace(t.Category) && string.IsNullOrWhiteSpace(t.Label));
            }
            else
            {
                matching = allLabelTorrents.Where(t => string.Equals(t.Category, targetLabel, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase));
            }

            var torrentHashes = matching.Select(t => t.InfoHash.ToLowerInvariant()).ToArray();
            return this.DelugeResult(new { result = torrentHashes, error = (object)null, id });
        }
    }

    private IActionResult HandleLabelAdd(JsonElement paramsElem, object id)
    {
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
    }

    private IActionResult HandleLabelRemove(JsonElement paramsElem, object id)
    {
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
    }

    private IActionResult HandleLabelGetOptions(JsonElement paramsElem, object id)
    {
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
    }

    private IActionResult HandleLabelSetOptions(JsonElement paramsElem, object id)
    {
        {
            if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
            {
                var lName = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
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
    }

    private async Task<IActionResult> HandleLabelSetTorrentAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var torrentHash = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            var labelName = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;
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
    }

    private IActionResult HandleGetPlugins(object id)
    {
        return this.DelugeResult(new { result = new[] { "Label", "Extractor", "Execute", "AutoAdd", "Blocklist", "Scheduler", "Stats" }, error = (object)null, id });
    }

    private IActionResult HandleTogglePlugin(object id)
    {
        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleWebGetHosts(object id)
    {
        return this.DelugeResult(new { result = new object[] { new object[] { "1", "127.0.0.1", 58846, "Connected" } }, error = (object)null, id });
    }

    private IActionResult HandleWebGetHostStatus(object id)
    {
        return this.DelugeResult(new { result = new object[] { "1", "Connected", "2.1.1" }, error = (object)null, id });
    }

    private IActionResult HandleWebUpdateUi(JsonElement paramsElem, object id)
    {
        var allTorrentsForUi = this.torrentService.GetAll().ToList();
        var (uiFilterObj, uiKeys) = ParseStatusParams(paramsElem, isWebUpdateUi: true);
        var filteredTorrents = FilterTorrents(allTorrentsForUi, uiFilterObj);

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
                filters = this.BuildFilterTree(allTorrentsForUi),
                stats = new
                {
                    max_download = this.configService.MaxDownloadSpeedKbps,
                    max_upload = this.configService.MaxUploadSpeedKbps,
                    num_connections = allTorrentsForUi.Sum(t => t.Leechers + t.Seeders),
                    upload_rate = allTorrentsForUi.Sum(t => t.UploadSpeed),
                    download_rate = allTorrentsForUi.Sum(t => t.DownloadSpeed),
                    free_space = this.GetDriveFreeSpace(this.configService.DownloadDir),
                },
            },
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleCoreGetConfig(object id)
    {
        return this.DelugeResult(new
        {
            result = this.GetDelugeConfigDictionary(),
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleCoreGetConfigValues(JsonElement paramsElem, object id)
    {
        var fullConfig = this.GetDelugeConfigDictionary();
        var requestedConfig = new Dictionary<string, object>();
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var keyElem in paramsElem[0].EnumerateArray())
            {
                if (keyElem.ValueKind == JsonValueKind.String)
                {
                    var k = keyElem.GetString();
                    if (!string.IsNullOrEmpty(k))
                    {
                        requestedConfig[k] = fullConfig.TryGetValue(k, out var val) ? val : null;
                    }
                }
            }
        }

        return this.DelugeResult(new { result = requestedConfig, error = (object)null, id });
    }

    private IActionResult HandleCoreGetConfigValue(JsonElement paramsElem, object id)
    {
        var singleCfgKey = GetFirstStringParam(paramsElem);
        var singleFullConfig = this.GetDelugeConfigDictionary();
        singleFullConfig.TryGetValue(singleCfgKey ?? string.Empty, out var foundVal);
        return this.DelugeResult(new { result = foundVal, error = (object)null, id });
    }

    private IActionResult HandleCoreSetConfig(JsonElement paramsElem, object id)
    {
        if (this.configService != null)
        {
            var cfgUpdates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            JsonElement cfgElem = default;

            if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
            {
                cfgElem = paramsElem[0];
            }
            else if (paramsElem.ValueKind == JsonValueKind.Object)
            {
                cfgElem = paramsElem;
            }

            if (cfgElem.ValueKind == JsonValueKind.Object)
            {
                if (cfgElem.TryGetProperty("max_download_speed", out var dlProp) && dlProp.ValueKind == JsonValueKind.Number && dlProp.TryGetDouble(out var dlVal))
                {
                    cfgUpdates["MaxDownloadSpeedKbps"] = (int)Math.Round(dlVal);
                }

                if (cfgElem.TryGetProperty("max_upload_speed", out var ulProp) && ulProp.ValueKind == JsonValueKind.Number && ulProp.TryGetDouble(out var ulVal))
                {
                    cfgUpdates["MaxUploadSpeedKbps"] = (int)Math.Round(ulVal);
                }

                if (cfgElem.TryGetProperty("download_location", out var dlLocProp) && dlLocProp.ValueKind == JsonValueKind.String)
                {
                    cfgUpdates["DownloadDir"] = dlLocProp.GetString();
                }
                else if (cfgElem.TryGetProperty("move_completed_path", out var mcpProp) && mcpProp.ValueKind == JsonValueKind.String)
                {
                    cfgUpdates["DownloadDir"] = mcpProp.GetString();
                }

                if (cfgElem.TryGetProperty("max_connections_global", out var mcgProp) && mcgProp.ValueKind == JsonValueKind.Number && mcgProp.TryGetInt32(out var mcgVal))
                {
                    cfgUpdates["MaxGlobalConnections"] = mcgVal;
                }

                if (cfgElem.TryGetProperty("max_connections_per_torrent", out var mcptProp) && mcptProp.ValueKind == JsonValueKind.Number && mcptProp.TryGetInt32(out var mcptVal))
                {
                    cfgUpdates["MaxPerTorrentConnections"] = mcptVal;
                }

                if (cfgElem.TryGetProperty("max_upload_slots_global", out var musgProp) && musgProp.ValueKind == JsonValueKind.Number && musgProp.TryGetInt32(out var musgVal))
                {
                    cfgUpdates["MaxUploadSlots"] = musgVal;
                }

                if (cfgElem.TryGetProperty("max_active_limit", out var malProp) && malProp.ValueKind == JsonValueKind.Number && malProp.TryGetInt32(out var malVal))
                {
                    cfgUpdates["MaxActiveTorrents"] = malVal;
                    cfgUpdates["MaxActiveDownloads"] = malVal;
                }
                else if (cfgElem.TryGetProperty("max_active_downloading", out var madProp) && madProp.ValueKind == JsonValueKind.Number && madProp.TryGetInt32(out var madVal))
                {
                    cfgUpdates["MaxActiveDownloads"] = madVal;
                }

                if (cfgElem.TryGetProperty("max_active_seeding", out var masProp) && masProp.ValueKind == JsonValueKind.Number && masProp.TryGetInt32(out var masVal))
                {
                    cfgUpdates["MaxActiveUploads"] = masVal;
                }

                if (cfgElem.TryGetProperty("listen_ports", out var lpProp))
                {
                    if (lpProp.ValueKind == JsonValueKind.Array && lpProp.GetArrayLength() > 0 && lpProp[0].TryGetInt32(out var portVal))
                    {
                        cfgUpdates["ListeningPort"] = portVal;
                    }
                    else if (lpProp.ValueKind == JsonValueKind.Number && lpProp.TryGetInt32(out var pVal))
                    {
                        cfgUpdates["ListeningPort"] = pVal;
                    }
                }
                else if (cfgElem.TryGetProperty("listen_port", out var lpSingle) && lpSingle.ValueKind == JsonValueKind.Number && lpSingle.TryGetInt32(out var pSingleVal))
                {
                    cfgUpdates["ListeningPort"] = pSingleVal;
                }

                if (cfgElem.TryGetProperty("dht", out var dhtProp))
                {
                    cfgUpdates["EnableDht"] = SafeGetBoolean(dhtProp);
                }

                if (cfgElem.TryGetProperty("upnp", out var upnpProp))
                {
                    cfgUpdates["UpnpEnabled"] = SafeGetBoolean(upnpProp);
                }

                if (cfgElem.TryGetProperty("natpmp", out var natpmpProp))
                {
                    cfgUpdates["UpnpEnabled"] = SafeGetBoolean(natpmpProp);
                }

                if (cfgElem.TryGetProperty("lsd", out var lsdProp))
                {
                    cfgUpdates["EnableLpd"] = SafeGetBoolean(lsdProp);
                }

                if (cfgElem.TryGetProperty("listen_interface", out var liProp) && liProp.ValueKind == JsonValueKind.String)
                {
                    cfgUpdates["BindInterface"] = liProp.GetString();
                }

                if (cfgElem.TryGetProperty("random_port", out var rpProp))
                {
                    cfgUpdates["PeerPortRandomOnStart"] = SafeGetBoolean(rpProp);
                }

                if (cfgElem.TryGetProperty("stop_seed_ratio", out var ssrProp) && ssrProp.ValueKind == JsonValueKind.Number && ssrProp.TryGetDouble(out var ssrVal))
                {
                    cfgUpdates["GlobalSeedRatioLimit"] = ssrVal;
                }

                if (cfgElem.TryGetProperty("seed_time_limit", out var stlProp) && stlProp.ValueKind == JsonValueKind.Number && stlProp.TryGetInt32(out var stlVal))
                {
                    cfgUpdates["IdleSeedingLimitMinutes"] = stlVal / 60;
                }

                if (cfgElem.TryGetProperty("dont_count_slow_torrents", out var dcstProp))
                {
                    cfgUpdates["IgnoreSlowTorrents"] = SafeGetBoolean(dcstProp);
                }

                if (cfgElem.TryGetProperty("enc_in_policy", out var encInProp) && encInProp.ValueKind == JsonValueKind.Number && encInProp.TryGetInt32(out var encVal))
                {
                    cfgUpdates["EncryptionMode"] = encVal == 0 ? "Forced" : (encVal == 2 ? "Disabled" : "Enabled");
                }
                else if (cfgElem.TryGetProperty("enc_out_policy", out var encOutProp) && encOutProp.ValueKind == JsonValueKind.Number && encOutProp.TryGetInt32(out var encOutVal))
                {
                    cfgUpdates["EncryptionMode"] = encOutVal == 0 ? "Forced" : (encOutVal == 2 ? "Disabled" : "Enabled");
                }

                if (cfgUpdates.Count > 0)
                {
                    this.configService.SaveConfigDictionary(cfgUpdates);
                }
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreGetSessionStatus(object id)
    {
        var allT = this.torrentService.GetAll().ToList();
        return this.DelugeResult(new
        {
            result = new Dictionary<string, object>
                        {
                            { "download_rate", allT.Sum(t => t.DownloadSpeed) },
                            { "upload_rate", allT.Sum(t => t.UploadSpeed) },
                            { "num_peers", allT.Sum(t => t.Leechers) },
                            { "total_peers", allT.Sum(t => t.Seeders + t.Leechers) },
                            { "payload_download_rate", allT.Sum(t => t.DownloadSpeed) },
                            { "payload_upload_rate", allT.Sum(t => t.UploadSpeed) },
                            { "total_download", allT.Sum(t => t.Downloaded) },
                            { "total_upload", allT.Sum(t => t.Uploaded) },
                        },
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleCoreGetFreeSpace(JsonElement paramsElem, object id)
    {
        var rawPath = GetFirstStringParam(paramsElem);
        var targetPath = !string.IsNullOrWhiteSpace(rawPath) ? rawPath : (this.configService.DownloadDir ?? "/downloads");
        return this.DelugeResult(new { result = this.GetDriveFreeSpace(targetPath), error = (object)null, id });
    }

    private IActionResult HandleGetTorrentsStatus(JsonElement paramsElem, object id, bool isWeb)
    {
        var (filterObj, requestedKeys) = ParseStatusParams(paramsElem, isWebUpdateUi: false);
        var torrents = this.torrentService.GetAll();
        var filteredTorrents = FilterTorrents(torrents, filterObj);
        var resultDict = new Dictionary<string, object>();
        foreach (var t in filteredTorrents)
        {
            resultDict[t.InfoHash.ToLowerInvariant()] = this.MapTorrentToDelugeStatus(t, requestedKeys);
        }

        if (isWeb)
        {
            return this.DelugeResult(new { result = new { torrents = resultDict }, error = (object)null, id });
        }

        return this.DelugeResult(new { result = resultDict, error = (object)null, id });
    }

    private IActionResult HandleGetTorrentStatus(JsonElement paramsElem, object id)
    {
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
    }

    private async Task<IActionResult> HandleCoreAddTorrentFileAsync(JsonElement paramsElem, object id)
    {
        string addedHash = null;
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var b64 = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;
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
                        isPaused = SafeGetBoolean(ap);
                    }

                    if (opts.TryGetProperty("download_location", out var dl) && dl.ValueKind == JsonValueKind.String)
                    {
                        savePath = dl.GetString();
                    }
                    else if (opts.TryGetProperty("move_completed_path", out var mcp) && mcp.ValueKind == JsonValueKind.String)
                    {
                        savePath = mcp.GetString();
                    }

                    if (opts.TryGetProperty("label", out var lbl) && lbl.ValueKind == JsonValueKind.String)
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
    }

    private async Task<IActionResult> HandleCoreAddTorrentMagnetAsync(JsonElement paramsElem, object id)
    {
        string magnetHash = null;
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
        {
            var magnetUri = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            var isPaused = false;
            string savePath = null;
            string category = null;
            double? targetRatio = null;

            if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.Object)
            {
                var opts = paramsElem[1];
                if (opts.TryGetProperty("add_paused", out var ap))
                {
                    isPaused = SafeGetBoolean(ap);
                }

                if (opts.TryGetProperty("download_location", out var dl) && dl.ValueKind == JsonValueKind.String)
                {
                    savePath = dl.GetString();
                }
                else if (opts.TryGetProperty("move_completed_path", out var mcp) && mcp.ValueKind == JsonValueKind.String)
                {
                    savePath = mcp.GetString();
                }

                if (opts.TryGetProperty("label", out var lbl) && lbl.ValueKind == JsonValueKind.String)
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
    }

    private async Task<IActionResult> HandleCoreAddTorrentUrlAsync(JsonElement paramsElem, object id)
    {
        string urlHash = null;
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
        {
            var url = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            var isPaused = false;
            string savePath = null;
            string category = null;

            if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.Object)
            {
                var opts = paramsElem[1];
                if (opts.TryGetProperty("add_paused", out var ap))
                {
                    isPaused = SafeGetBoolean(ap);
                }

                if (opts.TryGetProperty("download_location", out var dl) && dl.ValueKind == JsonValueKind.String)
                {
                    savePath = dl.GetString();
                }
                else if (opts.TryGetProperty("move_completed_path", out var mcp) && mcp.ValueKind == JsonValueKind.String)
                {
                    savePath = mcp.GetString();
                }

                if (opts.TryGetProperty("label", out var lbl) && lbl.ValueKind == JsonValueKind.String)
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
                    var maxTorrentBytes = this.configService?.MaxTorrentFileSizeBytes ?? (this.configFileProvider?.MaxTorrentFileSizeBytes ?? 250L * 1024 * 1024);
                    var bytes = await this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: maxTorrentBytes);
                    var parsed = this.torrentFileParser.Parse(bytes);
                    var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, category, savePath, isPaused, bytes);
                    urlHash = added?.InfoHash;
                }
            }
        }

        return this.DelugeResult(new { result = urlHash, error = (object)null, id });
    }

    private async Task<IActionResult> HandleWebUploadTorrentAsync(JsonElement paramsElem, object id)
    {
        string tempTorrentPath = null;
        if (paramsElem.ValueKind == JsonValueKind.Array)
        {
            string b64 = null;
            if (paramsElem.GetArrayLength() >= 2 && paramsElem[1].ValueKind == JsonValueKind.String)
            {
                b64 = paramsElem[1].GetString();
            }
            else if (paramsElem.GetArrayLength() >= 1 && paramsElem[0].ValueKind == JsonValueKind.String)
            {
                b64 = paramsElem[0].GetString();
            }

            if (!string.IsNullOrWhiteSpace(b64))
            {
                var bytes = Convert.FromBase64String(b64);
                tempTorrentPath = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), $"deluge_upload_{Guid.NewGuid():N}.torrent");
                await global::System.IO.File.WriteAllBytesAsync(tempTorrentPath, bytes);
            }
        }

        return this.DelugeResult(new { result = tempTorrentPath, error = (object)null, id });
    }

    private async Task<IActionResult> HandleWebGetTorrentInfoAsync(JsonElement paramsElem, object id)
    {
        object torrentInfoResult = null;
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 1)
        {
            var filePath = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            if (!string.IsNullOrWhiteSpace(filePath) && global::System.IO.File.Exists(filePath))
            {
                var bytes = await global::System.IO.File.ReadAllBytesAsync(filePath);
                var parsed = this.torrentFileParser.Parse(bytes);
                if (parsed != null)
                {
                    var filesList = parsed.Files?.Select(f => (object)new
                    {
                        path = f.Path,
                        size = f.Size,
                        offset = 0L,
                    }).ToList() ?? new List<object>();

                    torrentInfoResult = new Dictionary<string, object>
                    {
                        ["name"] = parsed.Name ?? string.Empty,
                        ["info_hash"] = parsed.InfoHash ?? string.Empty,
                        ["total_size"] = parsed.TotalSize,
                        ["comment"] = parsed.Comment ?? string.Empty,
                        ["private"] = parsed.IsPrivate,
                        ["files_tree"] = new Dictionary<string, object>(),
                        ["files"] = filesList,
                    };
                }
            }
        }

        return this.DelugeResult(new { result = torrentInfoResult, error = (object)null, id });
    }

    private async Task<IActionResult> HandleWebAddTorrentsAsync(JsonElement paramsElem, object id)
    {
        var addTorrentsSuccess = true;
        if (paramsElem.ValueKind == JsonValueKind.Array)
        {
            var torrentItems = new List<JsonElement>();
            if (paramsElem.GetArrayLength() >= 1 && paramsElem[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in paramsElem[0].EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.Object)
                    {
                        torrentItems.Add(elem);
                    }
                }
            }
            else
            {
                foreach (var elem in paramsElem.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.Object)
                    {
                        torrentItems.Add(elem);
                    }
                }
            }

            foreach (var item in torrentItems)
            {
                string torrentPath = null;
                if (item.TryGetProperty("path", out var pProp) && pProp.ValueKind == JsonValueKind.String)
                {
                    torrentPath = pProp.GetString();
                }

                if (!string.IsNullOrWhiteSpace(torrentPath) && global::System.IO.File.Exists(torrentPath))
                {
                    var bytes = await global::System.IO.File.ReadAllBytesAsync(torrentPath);
                    var parsed = this.torrentFileParser.Parse(bytes);

                    var isPaused = false;
                    string savePath = null;
                    string category = null;
                    double? targetRatio = null;

                    if (item.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Object)
                    {
                        if (opts.TryGetProperty("add_paused", out var ap))
                        {
                            isPaused = SafeGetBoolean(ap);
                        }

                        if (opts.TryGetProperty("download_location", out var dl) && dl.ValueKind == JsonValueKind.String)
                        {
                            savePath = dl.GetString();
                        }
                        else if (opts.TryGetProperty("move_completed_path", out var mcp) && mcp.ValueKind == JsonValueKind.String)
                        {
                            savePath = mcp.GetString();
                        }

                        if (opts.TryGetProperty("label", out var lbl) && lbl.ValueKind == JsonValueKind.String)
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

                    try
                    {
                        global::System.IO.File.Delete(torrentPath);
                    }
                    catch
                    {
                        // Ignore cleanup error
                    }
                }
            }
        }

        return this.DelugeResult(new { result = addTorrentsSuccess, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCorePauseTorrentsAsync(string lowerMethod, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashes(paramsElem);
        if (lowerMethod == "core.pause_all_torrents" || hashes.Count == 0)
        {
            var allT = this.torrentService.GetAll();
            foreach (var t in allT)
            {
                await this.torrentService.PauseAsync(t.Id);
            }
        }
        else
        {
            foreach (var h in hashes)
            {
                var t = this.torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    await this.torrentService.PauseAsync(t.Id);
                }
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreResumeTorrentsAsync(string lowerMethod, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashes(paramsElem);
        if (lowerMethod == "core.resume_all_torrents" || hashes.Count == 0)
        {
            var allT = this.torrentService.GetAll();
            foreach (var t in allT)
            {
                await this.torrentService.ResumeAsync(t.Id);
            }
        }
        else
        {
            foreach (var h in hashes)
            {
                var t = this.torrentService.GetByInfoHash(h);
                if (t != null)
                {
                    await this.torrentService.ResumeAsync(t.Id);
                }
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreRemoveTorrentsAsync(string lowerMethod, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashes(paramsElem);
        var removeData = GetSecondBoolParam(paramsElem);
        foreach (var h in hashes)
        {
            var t = this.torrentService.GetByInfoHash(h);
            if (t != null)
            {
                await this.torrentService.DeleteAsync(t.Id, removeData);
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreForceRecheckAsync(JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashes(paramsElem);
        foreach (var h in hashes)
        {
            var t = this.torrentService.GetByInfoHash(h);
            if (t != null)
            {
                await this.torrentService.ForceRecheckAsync(t.Id);
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreForceReannounceAsync(JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashes(paramsElem);
        foreach (var h in hashes)
        {
            var t = this.torrentService.GetByInfoHash(h);
            if (t != null)
            {
                await this.torrentService.ForceAnnounceAsync(t.Id);
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreMoveStorageAsync(JsonElement paramsElem, object id)
    {
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
    }

    private async Task<IActionResult> HandleCoreSetTorrentOptionsAsync(JsonElement paramsElem, object id)
    {
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
                    if (opts.TryGetProperty("download_location", out var dl) && dl.ValueKind == JsonValueKind.String)
                    {
                        newPath = dl.GetString();
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
                                await this.torrentFileService.SetPriorityAsync(files[fIdx].Id, FromDelugePriority(prio));
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
    }

    private async Task<IActionResult> HandleCoreSetTorrentFilePrioritiesAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var hash = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
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
                            await this.torrentFileService.SetPriorityAsync(files[fIdx].Id, FromDelugePriority(prio));
                        }

                        fIdx++;
                    }
                }
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreRenameFilesAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var torrentIdentifier = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : paramsElem[0].ToString();
            var t = this.torrentService.GetByInfoHash(torrentIdentifier) ??
                (int.TryParse(torrentIdentifier, out var tid) ? this.torrentService.Get(tid) : null);

            if (t != null && paramsElem[1].ValueKind == JsonValueKind.Array)
            {
                var files = this.torrentFileService.GetFiles(t.Id).ToList();
                foreach (var fileEntry in paramsElem[1].EnumerateArray())
                {
                    var fileIndex = -1;
                    string newPath = null;

                    if (fileEntry.ValueKind == JsonValueKind.Array && fileEntry.GetArrayLength() >= 2)
                    {
                        if (fileEntry[0].TryGetInt32(out var idx))
                        {
                            fileIndex = idx;
                        }

                        if (fileEntry[1].ValueKind == JsonValueKind.String)
                        {
                            newPath = fileEntry[1].GetString();
                        }
                    }
                    else if (fileEntry.ValueKind == JsonValueKind.Object)
                    {
                        if (fileEntry.TryGetProperty("index", out var idxProp) && idxProp.TryGetInt32(out var idx))
                        {
                            fileIndex = idx;
                        }

                        if (fileEntry.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                        {
                            newPath = pathProp.GetString();
                        }
                        else if (fileEntry.TryGetProperty("new_path", out var npProp) && npProp.ValueKind == JsonValueKind.String)
                        {
                            newPath = npProp.GetString();
                        }
                    }

                    if (fileIndex >= 0 && fileIndex < files.Count && !string.IsNullOrWhiteSpace(newPath))
                    {
                        await this.torrentService.RenameFileAsync(t.Id, files[fileIndex].Path, newPath);
                    }
                }
            }
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleWebDisconnect(object id)
    {
        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleCoreQueueAsync(string lowerMethod, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashes(paramsElem);
        var dir = lowerMethod switch
        {
            "core.queue_top" => "top",
            "core.queue_up" => "up",
            "core.queue_down" => "down",
            "core.queue_bottom" => "bottom",
            _ => "top",
        };

        var matchingTorrents = hashes
            .Select(h => this.torrentService.GetByInfoHash(h))
            .Where(t => t != null)
            .ToList();

        if (dir == "top")
        {
            matchingTorrents = matchingTorrents.AsEnumerable().Reverse().ToList();
        }
        else if (dir == "up")
        {
            matchingTorrents = matchingTorrents.OrderBy(t => t.QueuePosition).ToList();
        }
        else if (dir == "down")
        {
            matchingTorrents = matchingTorrents.OrderByDescending(t => t.QueuePosition).ToList();
        }

        foreach (var t in matchingTorrents)
        {
            await this.torrentService.MoveQueueAsync(t.Id, dir);
        }

        return this.DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleGetFilterTree(object id)
    {
        var allTorrents = this.torrentService.GetAll().ToList();
        var filterTree = this.BuildFilterTree(allTorrents);
        return this.DelugeResult(new { result = filterTree, error = (object)null, id });
    }

    private IActionResult HandleUnknownMethod(string method, object id)
    {
        this.logger.Debug("Unhandled Deluge RPC method: {0}", method);
        return this.DelugeResult(new
        {
            result = (object)null,
            error = new { message = $"Unknown method: {method}", code = 1 },
            id,
        });
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
            return SafeGetBoolean(parameters[1]);
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

            filePriorities = files.Select(f => ToDelugePriority(f.Priority)).ToList();
            fileProgress = files.Select(f => f.Progress).ToList();
        }
        else
        {
            filesList = new List<Dictionary<string, object>>();
            filePriorities = new List<int>();
            fileProgress = new List<double>();
            numFiles = 0;
        }

        var rawSavePath = t.SavePath ?? string.Empty;
        var savePath = rawSavePath;
        if (!string.IsNullOrWhiteSpace(rawSavePath) && !string.IsNullOrWhiteSpace(t.Name))
        {
            var trimmedSave = rawSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.HasExtension(trimmedSave))
            {
                var parent = Path.GetDirectoryName(trimmedSave);
                savePath = !string.IsNullOrWhiteSpace(parent) ? parent : trimmedSave;
            }
            else
            {
                var dirName = Path.GetFileName(trimmedSave);
                if (string.Equals(dirName, t.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(trimmedSave);
                    savePath = !string.IsNullOrWhiteSpace(parent) ? parent : trimmedSave;
                }
            }
        }

        var needsPieces = requestedKeys == null || requestedKeys.Count == 0 || requestedKeys.Contains("pieces");
        List<int> piecesList;
        if (needsPieces)
        {
            var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
            var bitfield = downloadTask?.PieceBitfield;
            if (bitfield != null && bitfield.Length > 0)
            {
                piecesList = bitfield.Select(b => b ? 1 : 0).ToList();
            }
            else
            {
                var pieceVal = t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding ? 1 : 0;
                piecesList = t.PieceCount > 0 ? Enumerable.Repeat(pieceVal, t.PieceCount).ToList() : new List<int>();
            }
        }
        else
        {
            piecesList = new List<int>();
        }

        var status = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "name", t.Name },
            { "total_size", t.TotalSize },
            { "total_done", (long)(t.TotalSize * t.Progress) },
            { "total_wanted", t.TotalSize },
            { "total_remaining", (long)(t.TotalSize * (1.0 - t.Progress)) },
            { "total_payload_download", t.Downloaded },
            { "total_payload_upload", t.Uploaded },
            { "total_uploaded", t.Uploaded },
            { "progress", t.Progress * 100.0 },
            { "state", stateStr },
            { "download_payload_rate", (long)t.DownloadSpeed },
            { "upload_payload_rate", (long)t.UploadSpeed },
            { "eta", t.Eta },
            { "ratio", t.Ratio },
            { "num_seeds", t.Seeders },
            { "total_seeds", t.Seeders },
            { "num_peers", t.Leechers },
            { "total_peers", t.Seeders + t.Leechers },
            { "seeds_peers_ratio", t.Leechers > 0 ? (double)t.Seeders / t.Leechers : (t.Seeders > 0 ? -1.0 : 0.0) },
            { "tracker", t.TrackerUrl ?? string.Empty },
            { "tracker_host", GetTrackerHost(t) },
            { "trackers", !string.IsNullOrWhiteSpace(t.TrackerUrl) ? new List<Dictionary<string, object>> { new() { { "url", t.TrackerUrl }, { "tier", 0 } } } : new List<Dictionary<string, object>>() },
            { "tracker_status", !string.IsNullOrWhiteSpace(t.ErrorMessage) ? t.ErrorMessage : (!string.IsNullOrWhiteSpace(t.TrackerUrl) ? $"{t.TrackerUrl}: Announce OK" : "Announce OK") },
            { "next_announce", 1800 },
            { "finished_time", t.DateCompleted.HasValue ? new DateTimeOffset(t.DateCompleted.Value).ToUnixTimeSeconds() : (t.Status == TorrentStatus.Seeding || t.Progress >= 1.0 ? new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() : 0L) },
            { "time_since_transfer", t.LastActive.HasValue ? (long)Math.Max(0, (DateTime.UtcNow - t.LastActive.Value).TotalSeconds) : 0L },
            { "num_pieces", t.PieceCount },
            { "piece_length", t.PieceLength },
            { "pieces", piecesList },
            { "distributed_copies", t.Progress >= 1.0 ? 1.0 : (double)t.Progress },
            { "num_files", numFiles },
            { "files", filesList },
            { "file_priorities", filePriorities },
            { "file_progress", fileProgress },
            { "save_path", savePath },
            { "download_location", savePath },
            { "label", t.Category ?? string.Empty },
            { "queue_position", t.QueuePosition },
            { "queue", t.QueuePosition },
            { "comment", t.Comment ?? string.Empty },
            { "creator", t.CreatedBy ?? string.Empty },
            { "owner", "admin" },
            { "storage_mode", "sparse" },
            { "move_completed", false },
            { "move_completed_path", savePath },
            { "prioritize_first_last_pieces", false },
            { "sequential_download", t.SequentialDownload },
            { "max_connections", -1 },
            { "max_upload_slots", -1 },
            { "is_finished", t.Status == TorrentStatus.Seeding || t.Progress >= 1.0 },
            { "is_seed", t.Status == TorrentStatus.Seeding },
            { "paused", t.Status == TorrentStatus.Paused },
            { "time_added", new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds() },
            { "hash", t.InfoHash },
            { "all_time_download", t.Downloaded },
            { "active_time", (long)(DateTime.UtcNow - t.DateAdded).TotalSeconds },
            { "seeding_time", t.SeedingTimeSeconds },
            { "message", t.Status == TorrentStatus.Error ? "Error" : "OK" },
            { "is_auto_managed", true },
            { "auto_managed", true },
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
            var target = string.IsNullOrWhiteSpace(path) ? (this.configService?.DownloadDir ?? "/downloads") : path;
            var fullPath = global::System.IO.Path.GetFullPath(target);
            return this.diskProvider?.GetAvailableSpace(fullPath)
                ?? this.diskProvider?.GetAvailableSpace(target)
                ?? 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private Dictionary<string, object> BuildFilterTree(List<Torrent> allTorrents)
    {
        var stateList = new List<object[]>
        {
            new object[] { "All", allTorrents.Count },
            new object[] { "Active", allTorrents.Count(t => t.DownloadSpeed > 0 || t.UploadSpeed > 0) },
            new object[] { "Downloading", allTorrents.Count(t => t.Status == TorrentStatus.Downloading) },
            new object[] { "Seeding", allTorrents.Count(t => t.Status == TorrentStatus.Seeding) },
            new object[] { "Paused", allTorrents.Count(t => t.Status == TorrentStatus.Paused) },
            new object[] { "Checking", allTorrents.Count(t => t.Status == TorrentStatus.Checking) },
            new object[] { "Queued", allTorrents.Count(t => t.Status == TorrentStatus.Queued) },
            new object[] { "Error", allTorrents.Count(t => t.Status == TorrentStatus.Error) },
        };

        var trackerHosts = allTorrents
            .Select(GetTrackerHost)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .Select(g => new object[] { g.Key, g.Count() })
            .ToList();

        var unlabelledCount = allTorrents.Count(t => string.IsNullOrWhiteSpace(t.Category) && string.IsNullOrWhiteSpace(t.Label));
        var labels = new List<object[]>
        {
            new object[] { "All", allTorrents.Count },
            new object[] { "None", unlabelledCount },
        };

        var categories = this.categoryService.GetAll();
        foreach (var c in categories)
        {
            var count = allTorrents.Count(t => string.Equals(t.Category, c.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Label, c.Name, StringComparison.OrdinalIgnoreCase));
            labels.Add(new object[] { c.Name, count });
        }

        var owners = new List<object[]>
        {
            new object[] { "All", allTorrents.Count },
        };

        return new Dictionary<string, object>
        {
            { "state", stateList },
            { "tracker_host", trackerHosts },
            { "label", labels },
            { "owner", owners },
        };
    }

    private static (JsonElement? FilterObj, HashSet<string> RequestedKeys) ParseStatusParams(JsonElement paramsElem, bool isWebUpdateUi = false)
    {
        JsonElement? filterObj = null;
        HashSet<string> requestedKeys = null;

        if (paramsElem.ValueKind == JsonValueKind.Array)
        {
            var len = paramsElem.GetArrayLength();
            if (isWebUpdateUi)
            {
                if (len > 0 && paramsElem[0].ValueKind == JsonValueKind.Array)
                {
                    requestedKeys = ExtractStringSet(paramsElem[0]);
                }

                if (len > 1 && paramsElem[1].ValueKind == JsonValueKind.Object)
                {
                    filterObj = paramsElem[1];
                }
                else if (len > 0 && paramsElem[0].ValueKind == JsonValueKind.Object)
                {
                    filterObj = paramsElem[0];
                }
            }
            else
            {
                if (len > 0)
                {
                    if (paramsElem[0].ValueKind == JsonValueKind.Object)
                    {
                        filterObj = paramsElem[0];
                        if (len > 1 && paramsElem[1].ValueKind == JsonValueKind.Array)
                        {
                            requestedKeys = ExtractStringSet(paramsElem[1]);
                        }
                    }
                    else if (paramsElem[0].ValueKind == JsonValueKind.Array)
                    {
                        requestedKeys = ExtractStringSet(paramsElem[0]);
                        if (len > 1 && paramsElem[1].ValueKind == JsonValueKind.Object)
                        {
                            filterObj = paramsElem[1];
                        }
                    }
                }
            }
        }
        else if (paramsElem.ValueKind == JsonValueKind.Object)
        {
            filterObj = paramsElem;
        }

        return (filterObj, requestedKeys);
    }

    private static HashSet<string> ExtractStringSet(JsonElement arrayElem)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (arrayElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in arrayElem.EnumerateArray())
            {
                if (elem.ValueKind == JsonValueKind.String)
                {
                    var s = elem.GetString();
                    if (s != null)
                    {
                        set.Add(s);
                    }
                }
            }
        }

        return set;
    }

    private static List<Torrent> FilterTorrents(IEnumerable<Torrent> torrents, JsonElement? filterObj)
    {
        if (!filterObj.HasValue || filterObj.Value.ValueKind != JsonValueKind.Object)
        {
            return torrents.ToList();
        }

        var obj = filterObj.Value;
        var result = torrents;

        var idList = ExtractStringOrArrayStrings(obj, "id", "ids", "hash", "hashes", "info_hash", "info_hashes", "infohash", "infohashes", "torrent_id", "torrent_ids");
        if (idList != null && idList.Count > 0)
        {
            var idSet = new HashSet<string>(idList.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            if (idSet.Count > 0)
            {
                result = result.Where(t => (t.InfoHash != null && idSet.Contains(t.InfoHash)) || idSet.Contains(t.Id.ToString()));
            }
        }

        var labelList = ExtractStringOrArrayStrings(obj, "label", "labels", "category", "categories");
        if (labelList != null && labelList.Count > 0)
        {
            if (!labelList.Any(l => string.Equals(l, "All", StringComparison.OrdinalIgnoreCase)))
            {
                result = result.Where(t => labelList.Any(l => MatchesLabel(t, l)));
            }
        }

        var stateList = ExtractStringOrArrayStrings(obj, "state", "states");
        if (stateList != null && stateList.Count > 0)
        {
            if (!stateList.Any(s => string.Equals(s, "All", StringComparison.OrdinalIgnoreCase)))
            {
                result = result.Where(t => stateList.Any(s => MatchesState(t, s)));
            }
        }

        var trackerList = ExtractStringOrArrayStrings(obj, "tracker_host", "tracker_hosts", "tracker", "trackers");
        if (trackerList != null && trackerList.Count > 0)
        {
            if (!trackerList.Any(th => string.Equals(th, "All", StringComparison.OrdinalIgnoreCase)))
            {
                result = result.Where(t => trackerList.Any(th =>
                    string.Equals(GetTrackerHost(t), th, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(t.TrackerUrl) && t.TrackerUrl.Contains(th, StringComparison.OrdinalIgnoreCase))));
            }
        }

        return result.ToList();
    }

    private static List<string> ExtractStringOrArrayStrings(JsonElement obj, params string[] propertyNames)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var propNameSet = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);

        foreach (var prop in obj.EnumerateObject())
        {
            if (propNameSet.Contains(prop.Name))
            {
                var list = new List<string>();
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    if (val != null)
                    {
                        list.Add(val);
                    }

                    return list;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    list.Add(prop.Value.GetRawText());
                    return list;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var val = item.GetString();
                            if (val != null)
                            {
                                list.Add(val);
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.Number)
                        {
                            list.Add(item.GetRawText());
                        }
                    }

                    return list;
                }
            }
        }

        return null;
    }

    private static bool MatchesLabel(Torrent t, string label)
    {
        if (label == null || string.Equals(label, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(label) || string.Equals(label, "None", StringComparison.OrdinalIgnoreCase) || string.Equals(label, "no_label", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(t.Category) && string.IsNullOrWhiteSpace(t.Label);
        }

        return string.Equals(t.Category, label, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesState(Torrent t, string state)
    {
        if (string.IsNullOrWhiteSpace(state) || string.Equals(state, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return t.DownloadSpeed > 0 || t.UploadSpeed > 0;
        }

        if (string.Equals(state, "Inactive", StringComparison.OrdinalIgnoreCase))
        {
            return t.DownloadSpeed == 0 && t.UploadSpeed == 0;
        }

        if (string.Equals(state, "Downloading", StringComparison.OrdinalIgnoreCase))
        {
            return t.Status == TorrentStatus.Downloading;
        }

        if (string.Equals(state, "Seeding", StringComparison.OrdinalIgnoreCase))
        {
            return t.Status == TorrentStatus.Seeding;
        }

        if (string.Equals(state, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            return t.Status == TorrentStatus.Paused;
        }

        if (string.Equals(state, "Checking", StringComparison.OrdinalIgnoreCase))
        {
            return t.Status == TorrentStatus.Checking;
        }

        if (string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
        {
            return t.Status == TorrentStatus.Queued;
        }

        if (string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase))
        {
            return t.Status == TorrentStatus.Error;
        }

        return string.Equals(t.Status.ToString(), state, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTrackerHost(Torrent t)
    {
        if (string.IsNullOrWhiteSpace(t?.TrackerUrl))
        {
            return string.Empty;
        }

        try
        {
            if (Uri.TryCreate(t.TrackerUrl, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static int ToDelugePriority(int priority)
    {
        return priority switch
        {
            <= 0 => 0,
            1 or 2 => 1,
            3 => 4,
            >= 4 => 7,
        };
    }

    private static int FromDelugePriority(int priority)
    {
        return priority switch
        {
            <= 0 => 0,
            1 => 2,
            2 or 3 or 4 => 3,
            >= 5 => 4,
        };
    }

    private static bool SafeGetBoolean(JsonElement element, bool defaultValue = false)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var n) ? n != 0 : (element.TryGetDouble(out var d) && Math.Abs(d) > double.Epsilon),
            JsonValueKind.String => bool.TryParse(element.GetString(), out var b) ? b : (element.GetString() == "1"),
            _ => defaultValue,
        };
    }

    private Dictionary<string, object> GetDelugeConfigDictionary()
    {
        var encPolicy = 1; // 1 = Enabled
        if (!string.IsNullOrWhiteSpace(this.configService.EncryptionMode))
        {
            if (this.configService.EncryptionMode.Equals("Forced", StringComparison.OrdinalIgnoreCase) ||
                this.configService.EncryptionMode.Equals("RequireEncrypted", StringComparison.OrdinalIgnoreCase) ||
                this.configService.EncryptionMode.Equals("ForcedEncryption", StringComparison.OrdinalIgnoreCase))
            {
                encPolicy = 0; // Forced
            }
            else if (this.configService.EncryptionMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                     this.configService.EncryptionMode.Equals("Plaintext", StringComparison.OrdinalIgnoreCase))
            {
                encPolicy = 2; // Disabled
            }
        }

        var isStopAtRatio = this.configService.GlobalSeedRatioLimit > 0;
        var stopRatio = this.configService.GlobalSeedRatioLimit > 0 ? this.configService.GlobalSeedRatioLimit : 2.0;
        var isRemoveAtRatio = string.Equals(this.configService.GlobalShareLimitAction, "Delete", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(this.configService.GlobalShareLimitAction, "Remove", StringComparison.OrdinalIgnoreCase);

        var listenPort = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 58846;

        return new Dictionary<string, object>
        {
            { "download_location", this.configService.DownloadDir ?? "/downloads" },
            { "move_completed", false },
            { "move_completed_path", this.configService.DownloadDir ?? "/downloads" },
            { "max_connections_global", this.configService.MaxGlobalConnections },
            { "max_connections_per_torrent", this.configService.MaxPerTorrentConnections > 0 ? this.configService.MaxPerTorrentConnections : 50 },
            { "max_upload_slots_global", this.configService.MaxUploadSlots > 0 ? this.configService.MaxUploadSlots : 4 },
            { "max_upload_slots_per_torrent", 4 },
            { "max_download_speed", (double)this.configService.MaxDownloadSpeedKbps },
            { "max_upload_speed", (double)this.configService.MaxUploadSpeedKbps },
            { "max_active_limit", this.configService.MaxActiveTorrents > 0 ? this.configService.MaxActiveTorrents : (this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8) },
            { "max_active_downloading", this.configService.MaxActiveDownloads > 0 ? this.configService.MaxActiveDownloads : 8 },
            { "max_active_seeding", this.configService.MaxActiveUploads > 0 ? this.configService.MaxActiveUploads : 5 },
            { "compact_allocation", false },
            { "prioritize_first_last_pieces", true },
            { "dht", this.configService.EnableDht },
            { "upnp", this.configService.UpnpEnabled },
            { "natpmp", this.configService.UpnpEnabled },
            { "lsd", this.configService.EnableLpd },
            { "listen_interface", this.configService.BindInterface ?? string.Empty },
            { "random_port", this.configService.PeerPortRandomOnStart },
            { "listen_ports", new[] { listenPort, listenPort } },
            { "enc_in_policy", encPolicy },
            { "enc_out_policy", encPolicy },
            { "enc_prefer_rc4", true },
            { "enc_level", 2 },
            { "stop_seed_at_ratio", isStopAtRatio },
            { "stop_seed_ratio", stopRatio },
            { "seed_time_limit", this.configService.IdleSeedingLimitMinutes > 0 ? this.configService.IdleSeedingLimitMinutes * 60 : 180 },
            { "remove_at_ratio", isRemoveAtRatio },
            { "queue_complete", true },
            { "dont_count_slow_torrents", this.configService.IgnoreSlowTorrents },
            { "auto_manage_prefer_seeds", false },
            { "shared", false },
        };
    }
}
