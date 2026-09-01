using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.RTorrent;

[AllowAnonymous]
[ApiController]
public class RTorrentController : ControllerBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ICategoryService _categoryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public RTorrentController(
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

    [HttpPost]
    [Route("RPC2")]
    [Route("RPC1")]
    [Route("rutorrent/plugins/httprpc/action.php")]
    [Route("plugins/httprpc/action.php")]
    public async Task<IActionResult> HandleXmlRpc()
    {
        string requestBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return BuildXmlRpcResponse(new XElement("string", "0.9.8"));
        }

        try
        {
            var doc = XDocument.Parse(requestBody);
            var methodName = doc.Root?.Element("methodName")?.Value ?? string.Empty;
            var paramsElement = doc.Root?.Element("params");
            var paramValues = ExtractParamValues(paramsElement);

            switch (methodName.ToLowerInvariant())
            {
                case "system.listmethods":
                case "system.list_methods":
                case "system.getcapabilities":
                    return BuildXmlRpcResponse(new XElement("array", new XElement("data",
                        new XElement("value", new XElement("string", "d.multicall2")),
                        new XElement("value", new XElement("string", "d.multicall")),
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
                        new XElement("value", new XElement("string", "get_directory")),
                        new XElement("value", new XElement("string", "get_down_rate")),
                        new XElement("value", new XElement("string", "get_up_rate")))));

                case "system.client_version":
                case "system.api_version":
                case "system.get_version":
                    return BuildXmlRpcResponse(new XElement("string", "0.9.8"));

                case "get_directory":
                    return BuildXmlRpcResponse(new XElement("string", _configService.DownloadDir ?? "/downloads"));

                case "get_down_rate":
                    return BuildXmlRpcResponse(new XElement("i8", _torrentService.GetAll().Sum(t => t.DownloadSpeed)));

                case "get_up_rate":
                    return BuildXmlRpcResponse(new XElement("i8", _torrentService.GetAll().Sum(t => t.UploadSpeed)));

                case "d.multicall2":
                case "d.multicall":
                    return HandleMulticall(paramValues);

                case "load.raw_start":
                case "load.raw_verbose":
                case "load_raw_start":
                    if (paramValues.Count >= 2)
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
                            var parsed = _torrentFileParser.Parse(torrentBytes);
                            await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, torrentBytes);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                case "load.start":
                case "load.verbose":
                case "load_start":
                    if (paramValues.Count >= 2 && paramValues[1] is string uriStr)
                    {
                        if (uriStr.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                        {
                            await _torrentService.AddFromMagnetAsync(uriStr, null, null, false);
                        }
                        else
                        {
                            using var client = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                            var bytes = await client.GetByteArrayAsync(uriStr);
                            var parsed = _torrentFileParser.Parse(bytes);
                            await _torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, bytes);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                case "d.erase":
                case "d.delete":
                    if (paramValues.Count > 0 && paramValues[0] is string hashToErase)
                    {
                        var t = _torrentService.GetByInfoHash(hashToErase);
                        if (t != null)
                        {
                            await _torrentService.DeleteAsync(t.Id, false);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                case "d.stop":
                case "d.pause":
                case "d.close":
                    if (paramValues.Count > 0 && paramValues[0] is string hashToStop)
                    {
                        var t = _torrentService.GetByInfoHash(hashToStop);
                        if (t != null)
                        {
                            await _torrentService.PauseAsync(t.Id);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                case "d.start":
                case "d.resume":
                case "d.open":
                    if (paramValues.Count > 0 && paramValues[0] is string hashToStart)
                    {
                        var t = _torrentService.GetByInfoHash(hashToStart);
                        if (t != null)
                        {
                            await _torrentService.ResumeAsync(t.Id);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                case "d.check_hash":
                    if (paramValues.Count > 0 && paramValues[0] is string hashToCheck)
                    {
                        var t = _torrentService.GetByInfoHash(hashToCheck);
                        if (t != null)
                        {
                            await _torrentService.ForceRecheckAsync(t.Id);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                case "d.custom1.set":
                    if (paramValues.Count >= 2 && paramValues[0] is string targetHash && paramValues[1] is string newCategory)
                    {
                        var t = _torrentService.GetByInfoHash(targetHash);
                        if (t != null)
                        {
                            t.Category = newCategory;
                            await _torrentService.UpdateAsync(t);
                        }
                    }

                    return BuildXmlRpcResponse(new XElement("i4", 0));

                default:
                    _logger.Debug("Unhandled rTorrent XML-RPC method: {0}", methodName);
                    return BuildXmlRpcResponse(new XElement("i4", 0));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing rTorrent XML-RPC request");
            return BuildXmlRpcFault(1, ex.Message);
        }
    }

    private IActionResult HandleMulticall(List<object> paramValues)
    {
        var torrents = _torrentService.GetAll().ToList();
        var requestedFields = new List<string>();

        var startIndex = 0;
        if (paramValues.Count > 0 && (paramValues[0] is string s) && (string.IsNullOrEmpty(s) || s == "default" || s == "main" || s == "started" || s == "stopped" || s == "complete" || s == "incomplete"))
        {
            startIndex = 1;
        }

        for (var i = startIndex; i < paramValues.Count; i++)
        {
            if (paramValues[i] is string field)
            {
                requestedFields.Add(field);
            }
        }

        var arrayData = new XElement("data");

        foreach (var torrent in torrents)
        {
            var rowData = new XElement("data");

            foreach (var field in requestedFields)
            {
                var cleanField = field.Trim().TrimEnd('=', '(', ')');
                rowData.Add(new XElement("value", GetTorrentXmlFieldValue(torrent, cleanField)));
            }

            arrayData.Add(new XElement("value", new XElement("array", rowData)));
        }

        return BuildXmlRpcResponse(new XElement("array", arrayData));
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
                return new XElement("string", torrent.SavePath ?? (_configService.DownloadDir ?? "/downloads"));

            case "d.bytes_done":
            case "d.get_bytes_done":
            case "d.completed_bytes":
            case "d.get_completed_bytes":
                return new XElement("i8", torrent.Downloaded);

            case "d.size_bytes":
            case "d.get_size_bytes":
                return new XElement("i8", torrent.TotalSize);

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
                return new XElement("i4", (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding || torrent.Status == TorrentStatus.Stopped) ? 1 : 0);

            case "d.state":
            case "d.get_state":
                return new XElement("i4", (torrent.Status == TorrentStatus.Paused || torrent.Status == TorrentStatus.Stopped) ? 0 : 1);

            case "d.message":
            case "d.get_message":
                return new XElement("string", string.Empty);

            case "d.custom1":
            case "d.get_custom1":
                return new XElement("string", torrent.Category ?? string.Empty);

            case "d.ratio":
            case "d.get_ratio":
                return new XElement("i8", (long)(torrent.Ratio * 1000));

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
            case "boolean":
                return first.Value == "1" || first.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            case "base64":
                return Convert.FromBase64String(first.Value);
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
            new XElement("methodResponse",
                new XElement("params",
                    new XElement("param",
                        new XElement("value", valueContent)))));

        return Content(doc.ToString(SaveOptions.DisableFormatting), "text/xml", Encoding.UTF8);
    }

    private IActionResult BuildXmlRpcFault(int faultCode, string faultString)
    {
        var doc = new XDocument(
            new XElement("methodResponse",
                new XElement("fault",
                    new XElement("value",
                        new XElement("struct",
                            new XElement("member",
                                new XElement("name", "faultCode"),
                                new XElement("value", new XElement("int", faultCode))),
                            new XElement("member",
                                new XElement("name", "faultString"),
                                new XElement("value", new XElement("string", faultString))))))));

        return Content(doc.ToString(SaveOptions.DisableFormatting), "text/xml", Encoding.UTF8);
    }
}
