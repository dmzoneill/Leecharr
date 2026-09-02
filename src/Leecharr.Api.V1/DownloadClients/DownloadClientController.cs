using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.Torrents;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclients")]
public class DownloadClientController : Controller
{
    private readonly IDownloadClientRepository _repository;
    private readonly ITorrentService _torrentService;

    public DownloadClientController(IDownloadClientRepository repository, ITorrentService torrentService)
    {
        _repository = repository;
        _torrentService = torrentService;
    }

    [HttpGet]
    public ActionResult<List<DownloadClientResource>> GetAll()
    {
        var definitions = _repository.All();
        return Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadClientResource> Get(int id)
    {
        var definition = _repository.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<DownloadClientResource> Create([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        var created = _repository.Insert(model);
        return Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<DownloadClientResource> Update(int id, [FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var existing = _repository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        if (string.IsNullOrEmpty(model.Password) || model.Password.Contains('*'))
        {
            model.Password = existing.Password;
        }

        _repository.Update(model);
        return Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _repository.Delete(id);
        return Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<DownloadClientTestResult>> Test(int id)
    {
        var definition = _repository.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return await TestDirectInternal(ToResource(definition));
    }

    [HttpPost("test")]
    public async Task<ActionResult<DownloadClientTestResult>> TestDirect([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        return await TestDirectInternal(resource);
    }

    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<List<DownloadClientRemoteItem>>> GetItems(int id)
    {
        var client = _repository.Get(id);
        if (client == null)
        {
            return NotFound();
        }

        var items = await QueryRemoteClientItemsAsync(client);
        return Ok(items);
    }

    [HttpPost("{id:int}/import/{hash}")]
    public async Task<ActionResult<TorrentResource>> ImportTorrent(int id, string hash)
    {
        var client = _repository.Get(id);
        if (client == null)
        {
            return NotFound();
        }

        var existing = _torrentService.GetByInfoHash(hash);
        if (existing != null)
        {
            return Ok(TorrentResourceMapper.ToResource(existing));
        }

        // Add magnet by hash to engine
        var magnetUri = $"magnet:?xt=urn:btih:{hash}";
        var added = await _torrentService.AddFromMagnetAsync(magnetUri, client.Category, null, false);
        return Ok(TorrentResourceMapper.ToResource(added));
    }

    [HttpPost("{id:int}/import")]
    public async Task<ActionResult<SyncResultResource>> ImportTorrents(int id, [FromBody] ImportRequest request)
    {
        var client = _repository.Get(id);
        if (client == null)
        {
            return NotFound();
        }

        var count = 0;
        if (request?.Hashes != null)
        {
            foreach (var hash in request.Hashes)
            {
                try
                {
                    var existing = _torrentService.GetByInfoHash(hash);
                    if (existing == null)
                    {
                        var magnetUri = $"magnet:?xt=urn:btih:{hash}";
                        await _torrentService.AddFromMagnetAsync(magnetUri, request.Category ?? client.Category, request.SavePath, request.StartPaused);
                        count++;
                    }
                }
                catch
                {
                }
            }
        }

        return Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = count,
            Message = $"Import completed ({count} torrent(s) imported)."
        });
    }

    private static DownloadClientResource ToResource(DownloadClientDefinition model)
    {
        return new DownloadClientResource
        {
            Id = model.Id,
            Name = model.Name,
            ClientType = model.ClientType,
            Host = model.Host,
            Port = model.Port,
            UseSsl = model.UseSsl,
            Username = model.Username,
            Password = string.IsNullOrEmpty(model.Password) ? string.Empty : "********",
            Enabled = model.Enable
        };
    }

    private static DownloadClientDefinition ToModel(DownloadClientResource resource)
    {
        return new DownloadClientDefinition
        {
            Id = resource.Id,
            Name = resource.Name,
            ClientType = resource.ClientType ?? "qBittorrent",
            Host = resource.Host ?? "localhost",
            Port = resource.Port > 0 ? resource.Port : 8080,
            UseSsl = resource.UseSsl,
            Username = resource.Username,
            Password = resource.Password,
            Enable = resource.Enabled
        };
    }

    private async Task<ActionResult<DownloadClientTestResult>> TestDirectInternal(DownloadClientResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Host))
        {
            return Ok(new DownloadClientTestResult { Success = false, Message = "Host is required." });
        }

        var port = resource.Port > 0 ? resource.Port : 8080;
        var scheme = resource.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{resource.Host}:{port}";

        try
        {
            using var http = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            if (string.Equals(resource.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await http.GetAsync($"{baseUrl}/api/v2/app/webapiVersion");
                if (resp.IsSuccessStatusCode)
                {
                    var ver = await resp.Content.ReadAsStringAsync();
                    return Ok(new DownloadClientTestResult { Success = true, Message = $"qBittorrent WebAPI connected (v{ver.Trim()})." });
                }
            }
            else if (string.Equals(resource.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await http.GetAsync($"{baseUrl}/transmission/rpc");
                if (resp.StatusCode == global::System.Net.HttpStatusCode.Conflict || resp.IsSuccessStatusCode)
                {
                    return Ok(new DownloadClientTestResult { Success = true, Message = "Transmission RPC endpoint reachable." });
                }
            }
            else if (string.Equals(resource.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                var content = new global::System.Net.Http.StringContent("{\"method\":\"auth.check_session\",\"params\":[],\"id\":1}", global::System.Text.Encoding.UTF8, "application/json");
                var resp = await http.PostAsync($"{baseUrl}/json", content);
                if (resp.IsSuccessStatusCode)
                {
                    return Ok(new DownloadClientTestResult { Success = true, Message = "Deluge JSON-RPC endpoint reachable." });
                }
            }

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(resource.Host, port, cts.Token);
            return Ok(new DownloadClientTestResult
            {
                Success = true,
                Message = $"Connected to {resource.ClientType ?? "Client"} socket at {resource.Host}:{port} successfully."
            });
        }
        catch (Exception ex)
        {
            return Ok(new DownloadClientTestResult
            {
                Success = false,
                Message = $"Failed to connect to {resource.Host}:{port} - {ex.Message}"
            });
        }
    }

    private async Task<List<DownloadClientRemoteItem>> QueryRemoteClientItemsAsync(DownloadClientDefinition client)
    {
        var items = new List<DownloadClientRemoteItem>();
        var port = client.Port > 0 ? client.Port : 8080;
        var scheme = client.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{client.Host}:{port}";

        try
        {
            using var http = new global::System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await http.GetAsync($"{baseUrl}/api/v2/torrents/info");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = global::System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == global::System.Text.Json.JsonValueKind.Array)
                    {
                        var idx = 1;
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var hash = el.TryGetProperty("hash", out var h) ? h.GetString() : string.Empty;
                            var name = el.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                            var size = el.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                            var prog = el.TryGetProperty("progress", out var p) && p.TryGetDouble(out var pr) ? pr : 0.0;
                            var state = el.TryGetProperty("state", out var st) ? st.GetString() : "unknown";
                            var save = el.TryGetProperty("save_path", out var sp) ? sp.GetString() : string.Empty;
                            var cat = el.TryGetProperty("category", out var c) ? c.GetString() : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = state,
                                SavePath = save,
                                Category = cat
                            });
                        }
                    }
                }
            }
            else if (string.Equals(client.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                var body = new global::System.Net.Http.StringContent(
                    "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\"]}}",
                    global::System.Text.Encoding.UTF8,
                    "application/json");

                var resp = await http.PostAsync($"{baseUrl}/transmission/rpc", body);
                if (resp.StatusCode == global::System.Net.HttpStatusCode.Conflict && resp.Headers.TryGetValues("X-Transmission-Session-Id", out var sessValues))
                {
                    http.DefaultRequestHeaders.Add("X-Transmission-Session-Id", sessValues.FirstOrDefault());
                    body = new global::System.Net.Http.StringContent(
                        "{\"method\":\"torrent-get\",\"arguments\":{\"fields\":[\"id\",\"hashString\",\"name\",\"totalSize\",\"percentDone\",\"status\",\"downloadDir\"]}}",
                        global::System.Text.Encoding.UTF8,
                        "application/json");
                    resp = await http.PostAsync($"{baseUrl}/transmission/rpc", body);
                }

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = global::System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("arguments", out var args) && args.TryGetProperty("torrents", out var torrents) && torrents.ValueKind == global::System.Text.Json.JsonValueKind.Array)
                    {
                        var idx = 1;
                        foreach (var el in torrents.EnumerateArray())
                        {
                            var hash = el.TryGetProperty("hashString", out var h) ? h.GetString() : string.Empty;
                            var name = el.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                            var size = el.TryGetProperty("totalSize", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                            var prog = el.TryGetProperty("percentDone", out var p) && p.TryGetDouble(out var pr) ? pr : 0.0;
                            var save = el.TryGetProperty("downloadDir", out var sp) ? sp.GetString() : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = "active",
                                SavePath = save,
                                Category = string.Empty
                            });
                        }
                    }
                }
            }
            else if (string.Equals(client.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                var body = new global::System.Net.Http.StringContent(
                    "{\"method\":\"core.get_torrents_status\",\"params\":[{},[\"name\",\"total_size\",\"progress\",\"state\",\"save_path\",\"label\"]],\"id\":1}",
                    global::System.Text.Encoding.UTF8,
                    "application/json");

                var resp = await http.PostAsync($"{baseUrl}/json", body);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = global::System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind == global::System.Text.Json.JsonValueKind.Object)
                    {
                        var idx = 1;
                        foreach (var prop in res.EnumerateObject())
                        {
                            var hash = prop.Name;
                            var el = prop.Value;
                            var name = el.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                            var size = el.TryGetProperty("total_size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                            var prog = el.TryGetProperty("progress", out var p) && p.TryGetDouble(out var pr) ? pr / 100.0 : 0.0;
                            var state = el.TryGetProperty("state", out var st) ? st.GetString() : "unknown";
                            var save = el.TryGetProperty("save_path", out var sp) ? sp.GetString() : string.Empty;
                            var cat = el.TryGetProperty("label", out var c) ? c.GetString() : string.Empty;

                            items.Add(new DownloadClientRemoteItem
                            {
                                Id = (idx++).ToString(),
                                InfoHash = hash,
                                Name = name,
                                Size = size,
                                Progress = prog,
                                State = state,
                                SavePath = save,
                                Category = cat
                            });
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return items;
    }
}
