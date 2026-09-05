// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Api.V1.RTorrent;

[ApiController]
public class RTorrentController : ControllerBase
{
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ITorrentFileService torrentFileService;
    private readonly ICategoryService categoryService;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public RTorrentController(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ICategoryService categoryService,
        IConfigService configService,
        ITorrentFileService torrentFileService = null,
        IConfigFileProvider configFileProvider = null,
        ISafeHttpClientService safeHttpClientService = null,
        ITrackerEntryRepository trackerEntryRepository = null)
    {
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser;
        this.categoryService = categoryService;
        this.configService = configService;
        this.torrentFileService = torrentFileService;
        this.configFileProvider = configFileProvider;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.trackerEntryRepository = trackerEntryRepository;
    }

    [HttpPost]
    [Route("RPC2")]
    [Route("RPC1")]
    [Route("rutorrent/plugins/httprpc/action.php")]
    [Route("plugins/httprpc/action.php")]
    public async Task<IActionResult> HandleXmlRpc()
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(this.HttpContext, this.configFileProvider))
        {
            this.Response.Headers["WWW-Authenticate"] = "Basic realm=\"rTorrent\"";
            return this.Unauthorized();
        }

        string requestBody;
        using (var reader = new StreamReader(this.Request.Body, Encoding.UTF8))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return this.BuildXmlRpcResponse(new XElement("string", "0.9.8"));
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
            this.logger.Error(ex, "Error processing rTorrent XML-RPC request");
            return this.BuildXmlRpcFault(1, ex.Message);
        }
    }

    private async Task<XElement> ExecuteXmlRpcMethodAsync(string methodName, List<object> paramValues)
    {
        switch (methodName.ToLowerInvariant())
        {
            case "system.multicall":
                var resultsArray = new XElement("data");
                var callsList = paramValues.Count > 0 && paramValues[0] is List<object> l ? l : paramValues;
                foreach (var callItem in callsList)
                {
                    if (callItem is Dictionary<string, object> callDict)
                    {
                        var subMethod = callDict.TryGetValue("methodName", out var mn) ? mn?.ToString() : string.Empty;
                        var subParams = callDict.TryGetValue("params", out var p) && p is List<object> pl ? pl : new List<object>();
                        var subResult = await this.ExecuteXmlRpcMethodAsync(subMethod ?? string.Empty, subParams);
                        resultsArray.Add(new XElement("value", new XElement("array", new XElement("data", new XElement("value", subResult)))));
                    }
                }

                return new XElement("array", resultsArray);

            case "system.listmethods":
            case "system.list_methods":
            case "system.getcapabilities":
                return new XElement("array", new XElement(
                    "data",
                    new XElement("value", new XElement("string", "d.multicall2")),
                    new XElement("value", new XElement("string", "d.multicall")),
                    new XElement("value", new XElement("string", "system.multicall")),
                    new XElement("value", new XElement("string", "system.client_version")),
                    new XElement("value", new XElement("string", "system.api_version")),
                    new XElement("value", new XElement("string", "system.listMethods")),
                    new XElement("value", new XElement("string", "load.raw_start")),
                    new XElement("value", new XElement("string", "load.raw_verbose")),
                    new XElement("value", new XElement("string", "load.start")),
                    new XElement("value", new XElement("string", "load.verbose")),
                    new XElement("value", new XElement("string", "d.erase")),
                    new XElement("value", new XElement("string", "d.stop")),
                    new XElement("value", new XElement("string", "d.start")),
                    new XElement("value", new XElement("string", "d.close")),
                    new XElement("value", new XElement("string", "d.open")),
                    new XElement("value", new XElement("string", "d.check_hash")),
                    new XElement("value", new XElement("string", "d.custom1.set")),
                    new XElement("value", new XElement("string", "d.directory.set")),
                    new XElement("value", new XElement("string", "d.directory_base.set")),
                    new XElement("value", new XElement("string", "get_directory")),
                    new XElement("value", new XElement("string", "get_down_rate")),
                    new XElement("value", new XElement("string", "get_up_rate"))));

            case "system.client_version":
            case "system.api_version":
            case "system.get_version":
                return new XElement("string", "0.9.8");

            case "get_directory":
                return new XElement("string", this.configService.DownloadDir ?? "/downloads");

            case "get_down_rate":
                return new XElement("i8", this.torrentService.GetAll().Sum(t => t.DownloadSpeed));

            case "get_up_rate":
                return new XElement("i8", this.torrentService.GetAll().Sum(t => t.UploadSpeed));

            case "d.multicall2":
            case "d.multicall":
                return this.HandleMulticall(paramValues);

            case "f.multicall":
                if (paramValues.Count > 0 && paramValues[0] is string fHash)
                {
                    var t = this.torrentService.GetByInfoHash(fHash);
                    var fArrayData = new XElement("data");
                    if (t != null && this.torrentFileService != null)
                    {
                        var files = this.torrentFileService.GetFiles(t.Id).ToList();
                        var downloadTask = this.torrentService?.GetDownloadTask(t.Id);
                        TorrentFileProgressEnricher.Enrich(t, files, downloadTask);
                        var fFields = paramValues.Skip(2).OfType<string>().ToList();
                        foreach (var file in files)
                        {
                            var fRowData = new XElement("data");
                            foreach (var field in fFields)
                            {
                                var cleanField = field.Trim().TrimEnd('=', '(', ')');
                                if (cleanField.Equals("f.get_path", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.path", StringComparison.OrdinalIgnoreCase))
                                {
                                    fRowData.Add(new XElement("value", new XElement("string", file.Path ?? string.Empty)));
                                }
                                else if (cleanField.Equals("f.get_size_bytes", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.size_bytes", StringComparison.OrdinalIgnoreCase))
                                {
                                    fRowData.Add(new XElement("value", new XElement("i8", file.Size)));
                                }
                                else if (cleanField.Equals("f.get_completed_chunks", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.completed_chunks", StringComparison.OrdinalIgnoreCase))
                                {
                                    var pLen = t.PieceLength > 0 ? t.PieceLength : 1024 * 1024;
                                    var fileChunks = Math.Max(1, (long)Math.Ceiling((double)file.Size / pLen));
                                    var completedChunks = file.Progress >= 1.0 ? fileChunks : (long)(file.Progress * fileChunks);
                                    fRowData.Add(new XElement("value", new XElement("i8", completedChunks)));
                                }
                                else if (cleanField.Equals("f.get_size_chunks", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.size_chunks", StringComparison.OrdinalIgnoreCase))
                                {
                                    fRowData.Add(new XElement("value", new XElement("i8", file.PieceCount)));
                                }
                                else if (cleanField.Equals("f.get_range_first", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.range_first", StringComparison.OrdinalIgnoreCase))
                                {
                                    fRowData.Add(new XElement("value", new XElement("i8", file.PieceOffset)));
                                }
                                else if (cleanField.Equals("f.get_range_second", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.range_second", StringComparison.OrdinalIgnoreCase))
                                {
                                    fRowData.Add(new XElement("value", new XElement("i8", Math.Max(file.PieceOffset, file.PieceOffset + file.PieceCount - 1))));
                                }
                                else if (cleanField.Equals("f.get_priority", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("f.priority", StringComparison.OrdinalIgnoreCase))
                                {
                                    fRowData.Add(new XElement("value", new XElement("i4", file.Priority)));
                                }
                                else
                                {
                                    fRowData.Add(new XElement("value", new XElement("string", string.Empty)));
                                }
                            }

                            fArrayData.Add(new XElement("value", new XElement("array", fRowData)));
                        }
                    }

                    return new XElement("array", fArrayData);
                }

                return new XElement("array", new XElement("data"));

            case "t.multicall":
                if (paramValues.Count > 0 && paramValues[0] is string tHash)
                {
                    var t = this.torrentService.GetByInfoHash(tHash);
                    var tArrayData = new XElement("data");
                    if (t != null)
                    {
                        var tFields = paramValues.Skip(2).OfType<string>().ToList();
                        if (tFields.Count == 0 && paramValues.Count > 1)
                        {
                            tFields = paramValues.Skip(1).OfType<string>().Where(x => x.StartsWith("t.", StringComparison.OrdinalIgnoreCase)).ToList();
                        }

                        var trackers = this.trackerEntryRepository?.GetByTorrentId(t.Id)?.ToList();
                        if (trackers != null && trackers.Count > 0)
                        {
                            foreach (var tracker in trackers)
                            {
                                var tRowData = new XElement("data");
                                foreach (var field in tFields)
                                {
                                    var cleanField = field.Trim().TrimEnd('=', '(', ')');
                                    if (cleanField.Equals("t.get_url", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.url", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("string", tracker.Url ?? string.Empty)));
                                    }
                                    else if (cleanField.Equals("t.get_type", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.type", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.Tier > 0 ? tracker.Tier : 1)));
                                    }
                                    else if (cleanField.Equals("t.is_enabled", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.Enabled ? 1 : 0)));
                                    }
                                    else if (cleanField.Equals("t.is_open", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", 1)));
                                    }
                                    else if (cleanField.Equals("t.get_group", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.group", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.Tier)));
                                    }
                                    else if (cleanField.Equals("t.get_scrape_complete", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.scrape_complete", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.Seeders)));
                                    }
                                    else if (cleanField.Equals("t.get_scrape_incomplete", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.scrape_incomplete", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.Leechers)));
                                    }
                                    else if (cleanField.Equals("t.get_scrape_downloaded", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.scrape_downloaded", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.Downloaded)));
                                    }
                                    else if (cleanField.Equals("t.get_normal_interval", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.normal_interval", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tRowData.Add(new XElement("value", new XElement("i4", tracker.AnnounceInterval)));
                                    }
                                    else
                                    {
                                        tRowData.Add(new XElement("value", new XElement("string", string.Empty)));
                                    }
                                }

                                tArrayData.Add(new XElement("value", new XElement("array", tRowData)));
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(t.TrackerUrl))
                        {
                            var tRowData = new XElement("data");
                            foreach (var field in tFields)
                            {
                                var cleanField = field.Trim().TrimEnd('=', '(', ')');
                                if (cleanField.Equals("t.get_url", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.url", StringComparison.OrdinalIgnoreCase))
                                {
                                    tRowData.Add(new XElement("value", new XElement("string", t.TrackerUrl)));
                                }
                                else if (cleanField.Equals("t.get_type", StringComparison.OrdinalIgnoreCase) || cleanField.Equals("t.type", StringComparison.OrdinalIgnoreCase))
                                {
                                    tRowData.Add(new XElement("value", new XElement("i4", 1)));
                                }
                                else if (cleanField.Equals("t.is_enabled", StringComparison.OrdinalIgnoreCase))
                                {
                                    tRowData.Add(new XElement("value", new XElement("i4", 1)));
                                }
                                else if (cleanField.Equals("t.is_open", StringComparison.OrdinalIgnoreCase))
                                {
                                    tRowData.Add(new XElement("value", new XElement("i4", 1)));
                                }
                                else
                                {
                                    tRowData.Add(new XElement("value", new XElement("string", string.Empty)));
                                }
                            }

                            tArrayData.Add(new XElement("value", new XElement("array", tRowData)));
                        }
                    }

                    return new XElement("array", tArrayData);
                }

                return new XElement("array", new XElement("data"));

            case "load.raw_start":
            case "load.raw_verbose":
            case "load_raw_start":
            case "load.raw":
            case "load.normal":
            case "load.start":
            case "load.verbose":
            case "load_start":
                if (paramValues.Count >= 2)
                {
                    var isRaw = methodName.Contains("raw", StringComparison.OrdinalIgnoreCase);
                    var isStart = !methodName.Equals("load.normal", StringComparison.OrdinalIgnoreCase) && !methodName.Equals("load.raw", StringComparison.OrdinalIgnoreCase);

                    string customCategory = null;
                    string customDir = null;
                    int? customPriority = null;
                    for (var i = 2; i < paramValues.Count; i++)
                    {
                        if (paramValues[i] is string arg)
                        {
                            if (arg.StartsWith("d.custom1.set=", StringComparison.OrdinalIgnoreCase))
                            {
                                customCategory = arg["d.custom1.set=".Length..].Trim('\"', '\'');
                            }
                            else if (arg.StartsWith("d.directory.set=", StringComparison.OrdinalIgnoreCase))
                            {
                                customDir = arg["d.directory.set=".Length..].Trim('\"', '\'');
                            }
                            else if (arg.StartsWith("d.directory_base.set=", StringComparison.OrdinalIgnoreCase))
                            {
                                customDir = arg["d.directory_base.set=".Length..].Trim('\"', '\'');
                            }
                            else if (arg.StartsWith("d.priority.set=", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(arg["d.priority.set=".Length..].Trim('\"', '\''), out var prio))
                                {
                                    customPriority = prio;
                                }
                            }
                        }
                    }

                    if (isRaw)
                    {
                        var rawData = paramValues[1];
                        byte[] torrentBytes = null;
                        if (rawData is byte[] b)
                        {
                            torrentBytes = b;
                        }
                        else if (rawData is string s)
                        {
                            try
                            {
                                torrentBytes = Convert.FromBase64String(s);
                            }
                            catch
                            {
                                torrentBytes = Encoding.Latin1.GetBytes(s);
                            }
                        }

                        if (torrentBytes != null && torrentBytes.Length > 0)
                        {
                            var parsed = this.torrentFileParser.Parse(torrentBytes);
                            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, customCategory, customDir, !isStart, torrentBytes);
                            if (added != null && customPriority.HasValue)
                            {
                                added.Priority = customPriority.Value;
                                await this.torrentService.UpdateAsync(added);
                            }
                        }
                    }
                    else if (paramValues[1] is string uriStr)
                    {
                        if (uriStr.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                        {
                            var added = await this.torrentService.AddFromMagnetAsync(uriStr, customCategory, customDir, !isStart);
                            if (added != null && customPriority.HasValue)
                            {
                                added.Priority = customPriority.Value;
                                await this.torrentService.UpdateAsync(added);
                            }
                        }
                        else if (global::System.IO.File.Exists(uriStr))
                        {
                            var bytes = await global::System.IO.File.ReadAllBytesAsync(uriStr);
                            var parsed = this.torrentFileParser.Parse(bytes);
                            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, customCategory, customDir, !isStart, bytes);
                            if (added != null && customPriority.HasValue)
                            {
                                added.Priority = customPriority.Value;
                                await this.torrentService.UpdateAsync(added);
                            }
                        }
                        else
                        {
                            var bytes = await this.safeHttpClientService.DownloadBytesAsync(uriStr, maxSizeBytes: 10 * 1024 * 1024);
                            var parsed = this.torrentFileParser.Parse(bytes);
                            var added = await this.torrentService.AddFromParsedTorrentAsync(parsed, customCategory, customDir, !isStart, bytes);
                            if (added != null && customPriority.HasValue)
                            {
                                added.Priority = customPriority.Value;
                                await this.torrentService.UpdateAsync(added);
                            }
                        }
                    }
                }

                return new XElement("i4", 0);

            case "d.name":
            case "d.get_name":
                if (paramValues.Count > 0 && paramValues[0] is string hashForName)
                {
                    var t = this.torrentService.GetByInfoHash(hashForName);
                    if (t != null)
                    {
                        return new XElement("string", t.Name ?? string.Empty);
                    }
                }

                return new XElement("string", string.Empty);

            case "d.custom1":
            case "d.get_custom1":
                if (paramValues.Count > 0 && paramValues[0] is string hashForCustom)
                {
                    var t = this.torrentService.GetByInfoHash(hashForCustom);
                    if (t != null)
                    {
                        return new XElement("string", t.Category ?? string.Empty);
                    }
                }

                return new XElement("string", string.Empty);

            case "d.erase":
            case "d.delete":
                if (paramValues.Count > 0 && paramValues[0] is string hashToErase)
                {
                    var t = this.torrentService.GetByInfoHash(hashToErase);
                    if (t != null)
                    {
                        await this.torrentService.DeleteAsync(t.Id, false);
                    }
                }

                return new XElement("i4", 0);

            case "d.stop":
            case "d.pause":
            case "d.close":
                if (paramValues.Count > 0 && paramValues[0] is string hashToStop)
                {
                    var t = this.torrentService.GetByInfoHash(hashToStop);
                    if (t != null)
                    {
                        await this.torrentService.PauseAsync(t.Id);
                    }
                }

                return new XElement("i4", 0);

            case "d.start":
            case "d.resume":
            case "d.open":
                if (paramValues.Count > 0 && paramValues[0] is string hashToStart)
                {
                    var t = this.torrentService.GetByInfoHash(hashToStart);
                    if (t != null)
                    {
                        await this.torrentService.ResumeAsync(t.Id);
                    }
                }

                return new XElement("i4", 0);

            case "d.check_hash":
                if (paramValues.Count > 0 && paramValues[0] is string hashToCheck)
                {
                    var t = this.torrentService.GetByInfoHash(hashToCheck);
                    if (t != null)
                    {
                        await this.torrentService.ForceRecheckAsync(t.Id);
                    }
                }

                return new XElement("i4", 0);

            case "d.custom1.set":
                var targetCat = string.Empty;
                if (paramValues.Count >= 2 && paramValues[0] is string targetHash && paramValues[1] is string newCategory)
                {
                    var t = this.torrentService.GetByInfoHash(targetHash);
                    if (t != null)
                    {
                        t.Category = newCategory;
                        await this.torrentService.UpdateAsync(t);
                        targetCat = newCategory;
                    }
                }

                return new XElement("string", targetCat);

            case "d.directory.set":
            case "d.directory_base.set":
                if (paramValues.Count >= 2 && paramValues[0] != null)
                {
                    Torrent t = null;
                    if (paramValues[0] is int idNum)
                    {
                        t = this.torrentService.Get(idNum);
                    }
                    else
                    {
                        var target = paramValues[0].ToString()?.Trim();
                        if (!string.IsNullOrEmpty(target))
                        {
                            t = this.torrentService.GetByInfoHash(target) ??
                                (int.TryParse(target, out var parsedId) ? this.torrentService.Get(parsedId) : null);
                        }
                    }

                    var newDir = paramValues[1]?.ToString();
                    if (t != null && !string.IsNullOrWhiteSpace(newDir))
                    {
                        await this.torrentService.SetLocationAsync(t.Id, newDir, moveFiles: true);
                    }
                }

                return new XElement("i4", 0);

            case "d.priority.set":
                if (paramValues.Count >= 2 && paramValues[0] is string prioHash)
                {
                    var t = this.torrentService.GetByInfoHash(prioHash);
                    if (t != null)
                    {
                        if (paramValues[1] is int prioInt)
                        {
                            t.Priority = prioInt;
                            await this.torrentService.UpdateAsync(t);
                        }
                        else if (paramValues[1] is string prioStr && int.TryParse(prioStr, out var parsedPrio))
                        {
                            t.Priority = parsedPrio;
                            await this.torrentService.UpdateAsync(t);
                        }
                    }
                }

                return new XElement("i4", 0);

            case "f.priority.set":
            case "f.set_priority":
                if (paramValues.Count >= 3 && paramValues[0] is string filePrioHash && this.torrentFileService != null)
                {
                    var t = this.torrentService.GetByInfoHash(filePrioHash);
                    if (t != null)
                    {
                        var fIdx = paramValues[1] is int fi ? fi : (int.TryParse(paramValues[1]?.ToString(), out var pfi) ? pfi : -1);
                        var fPrio = paramValues[2] is int fp ? fp : (int.TryParse(paramValues[2]?.ToString(), out var pfp) ? pfp : 1);
                        var files = this.torrentFileService.GetFiles(t.Id).ToList();
                        if (fIdx >= 0 && fIdx < files.Count)
                        {
                            await this.torrentFileService.SetPriorityAsync(files[fIdx].Id, fPrio);
                        }
                    }
                }

                return new XElement("i4", 0);

            case "d.views.push_back_unique":
                return new XElement("i4", 0);

            default:
                if (methodName.StartsWith("d.", StringComparison.OrdinalIgnoreCase) && paramValues.Count > 0 && paramValues[0] is string dHash)
                {
                    var dt = this.torrentService.GetByInfoHash(dHash);
                    if (dt != null)
                    {
                        return this.GetTorrentXmlFieldValue(dt, methodName);
                    }
                }

                this.logger.Debug("Unhandled rTorrent XML-RPC method: {0}", methodName);
                return new XElement("i4", 0);
        }
    }

    private XElement HandleMulticall(List<object> paramValues)
    {
        var torrents = this.torrentService.GetAll().ToList();
        var requestedFields = new List<string>();

        foreach (var item in paramValues)
        {
            if (item is string field && !string.IsNullOrWhiteSpace(field))
            {
                var trimmed = field.Trim();
                if (trimmed.StartsWith("d.", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("f.", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("t.", StringComparison.OrdinalIgnoreCase))
                {
                    requestedFields.Add(trimmed);
                }
            }
        }

        var arrayData = new XElement("data");

        foreach (var torrent in torrents)
        {
            var rowData = new XElement("data");

            foreach (var field in requestedFields)
            {
                var cleanField = field.Trim().TrimEnd('=', '(', ')');
                rowData.Add(new XElement("value", this.GetTorrentXmlFieldValue(torrent, cleanField)));
            }

            arrayData.Add(new XElement("value", new XElement("array", rowData)));
        }

        return new XElement("array", arrayData);
    }

    private XElement GetTorrentXmlFieldValue(Torrent torrent, string field)
    {
        switch (field.ToLowerInvariant())
        {
            case "d.hash":
            case "d.get_hash":
                return new XElement("string", torrent.InfoHash.ToUpperInvariant());

            case "d.name":
            case "d.get_name":
                return new XElement("string", torrent.Name ?? string.Empty);

            case "d.base_path":
            case "d.get_base_path":
            case "d.directory":
            case "d.get_directory":
            case "d.directory_base":
            case "d.get_directory_base":
                return new XElement("string", torrent.SavePath ?? (this.configService.DownloadDir ?? "/downloads"));

            case "d.bytes_done":
            case "d.get_bytes_done":
            case "d.completed_bytes":
            case "d.get_completed_bytes":
                return new XElement("i8", torrent.Downloaded);

            case "d.size_bytes":
            case "d.get_size_bytes":
                return new XElement("i8", torrent.TotalSize);

            case "d.left_bytes":
            case "d.get_left_bytes":
                return new XElement("i8", Math.Max(0, torrent.TotalSize - torrent.Downloaded));

            case "d.down.rate":
            case "d.get_down_rate":
                return new XElement("i8", torrent.DownloadSpeed);

            case "d.up.rate":
            case "d.get_up_rate":
                return new XElement("i8", torrent.UploadSpeed);

            case "d.is_active":
            case "d.is_open":
                return new XElement("i4", (torrent.Status == TorrentStatus.Downloading || torrent.Status == TorrentStatus.Seeding) ? 1 : 0);

            case "d.complete":
            case "d.is_complete":
                return new XElement("i4", (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding) ? 1 : 0);

            case "d.state":
            case "d.get_state":
                return new XElement("i4", (torrent.Status == TorrentStatus.Paused || torrent.Status == TorrentStatus.Stopped) ? 0 : 1);

            case "d.message":
            case "d.get_message":
                return new XElement("string", torrent.Status == TorrentStatus.Error ? "Error" : string.Empty);

            case "d.custom1":
            case "d.get_custom1":
                return new XElement("string", torrent.Category ?? string.Empty);

            case "d.timestamp.started":
            case "d.get_timestamp.started":
                return new XElement("i8", new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds());

            case "d.timestamp.finished":
            case "d.get_timestamp.finished":
                return new XElement("i8", torrent.DateCompleted.HasValue ? new DateTimeOffset(torrent.DateCompleted.Value).ToUnixTimeSeconds() : 0);

            case "d.ratio":
            case "d.get_ratio":
                return new XElement("i8", (long)(torrent.Ratio * 1000));

            case "d.peers_connected":
            case "d.get_peers_connected":
            case "d.peers_accounted":
            case "d.get_peers_accounted":
                return new XElement("i4", torrent.Seeders + torrent.Leechers);

            case "d.peers_not_connected":
            case "d.get_peers_not_connected":
                return new XElement("i4", 0);

            case "d.peers_complete":
            case "d.get_peers_complete":
                return new XElement("i4", torrent.Seeders);

            case "d.up.total":
            case "d.get_up_total":
                return new XElement("i8", torrent.Uploaded);

            case "d.down.total":
            case "d.get_down_total":
                return new XElement("i8", torrent.Downloaded);

            case "d.priority":
            case "d.get_priority":
                return new XElement("i4", torrent.Priority);

            case "d.chunk_size":
            case "d.get_chunk_size":
                return new XElement("i8", torrent.PieceLength > 0 ? torrent.PieceLength : 1024 * 1024);

            case "d.size_chunks":
            case "d.get_size_chunks":
                var pieceLen = torrent.PieceLength > 0 ? torrent.PieceLength : 1024 * 1024;
                return new XElement("i8", torrent.PieceCount > 0 ? torrent.PieceCount : Math.Max(1, torrent.TotalSize / pieceLen));

            case "d.completed_chunks":
            case "d.get_completed_chunks":
                var totalP = torrent.PieceCount > 0 ? torrent.PieceCount : Math.Max(1, torrent.TotalSize / (torrent.PieceLength > 0 ? torrent.PieceLength : 1024 * 1024));
                return new XElement("i8", (long)(torrent.Progress * totalP));

            case "d.is_hash_checking":
            case "d.get_is_hash_checking":
                return new XElement("i4", torrent.Status == TorrentStatus.Checking ? 1 : 0);

            case "d.connection_current":
            case "d.get_connection_current":
                return new XElement("string", torrent.Status == TorrentStatus.Seeding ? "seed" : "leech");

            default:
                return new XElement("string", string.Empty);
        }
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
}
