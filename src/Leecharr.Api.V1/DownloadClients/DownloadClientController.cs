// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
    private readonly IDownloadClientRepository repository;
    private readonly ITorrentService torrentService;
    private readonly HttpClient httpClient;

    public DownloadClientController(IDownloadClientRepository repository, ITorrentService torrentService, HttpClient httpClient = null)
    {
        this.repository = repository;
        this.torrentService = torrentService;
        this.httpClient = httpClient;
    }

    [HttpGet]
    public ActionResult<List<DownloadClientResource>> GetAll()
    {
        var definitions = this.repository.All();
        return this.Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadClientResource> Get(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<DownloadClientResource> Create([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.repository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<DownloadClientResource> Update(int id, [FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.repository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        if (string.IsNullOrEmpty(model.Password) || model.Password.Contains('*'))
        {
            model.Password = existing.Password;
        }

        this.repository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.repository.Delete(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<DownloadClientTestResult>> Test(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return await this.TestDirectInternal(ToResource(definition));
    }

    [HttpPost("test")]
    public async Task<ActionResult<DownloadClientTestResult>> TestDirect([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        return await this.TestDirectInternal(resource);
    }

    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<List<DownloadClientRemoteItem>>> GetItems(int id)
    {
        var client = this.repository.Get(id);
        if (client == null)
        {
            return this.NotFound();
        }

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
        return this.Ok(items);
    }

    [HttpPost("{id:int}/import/{hash}")]
    public async Task<ActionResult<TorrentResource>> ImportTorrent(int id, string hash)
    {
        var client = this.repository.Get(id);
        if (client == null)
        {
            return this.NotFound();
        }

        var existing = this.torrentService.GetByInfoHash(hash);
        if (existing != null)
        {
            return this.Ok(TorrentResourceMapper.ToResource(existing));
        }

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
        var remoteItem = items.FirstOrDefault(i => string.Equals(i.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        var savePath = !string.IsNullOrWhiteSpace(remoteItem?.SavePath) ? remoteItem.SavePath : null;
        var category = !string.IsNullOrWhiteSpace(remoteItem?.Category) ? remoteItem.Category : client.Category;

        // Add magnet by hash to engine
        var magnetUri = $"magnet:?xt=urn:btih:{hash}";
        var added = await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, false);
        return this.Ok(TorrentResourceMapper.ToResource(added));
    }

    [HttpPost("{id:int}/import")]
    public async Task<ActionResult<SyncResultResource>> ImportTorrents(int id, [FromBody] ImportRequest request)
    {
        var client = this.repository.Get(id);
        if (client == null)
        {
            return this.NotFound();
        }

        var count = 0;
        var hashes = request?.EffectiveHashes?.ToList();
        if (hashes != null && hashes.Count > 0)
        {
            var remoteItems = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
            var remoteItemMap = remoteItems
                .Where(i => !string.IsNullOrEmpty(i.InfoHash))
                .GroupBy(i => i.InfoHash, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var hash in hashes)
            {
                try
                {
                    var existing = this.torrentService.GetByInfoHash(hash);
                    if (existing == null)
                    {
                        remoteItemMap.TryGetValue(hash, out var remoteItem);
                        var savePath = !string.IsNullOrWhiteSpace(request?.SavePath)
                            ? request.SavePath
                            : (!string.IsNullOrWhiteSpace(remoteItem?.SavePath) ? remoteItem.SavePath : null);
                        var category = !string.IsNullOrWhiteSpace(request?.Category)
                            ? request.Category
                            : (!string.IsNullOrWhiteSpace(remoteItem?.Category) ? remoteItem.Category : client.Category);

                        var magnetUri = $"magnet:?xt=urn:btih:{hash}";
                        await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, request?.StartPaused ?? false);
                        count++;
                    }
                }
                catch
                {
                }
            }
        }

        return this.Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = count,
            Added = count,
            Message = $"Import completed ({count} torrent(s) imported).",
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
            Category = model.Category,
            Enabled = model.Enable,
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
            Category = resource.Category,
            Enable = resource.Enabled,
        };
    }

    private async Task<ActionResult<DownloadClientTestResult>> TestDirectInternal(DownloadClientResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Host))
        {
            return this.Ok(new DownloadClientTestResult { Success = false, Message = "Host is required." });
        }

        var port = resource.Port > 0 ? resource.Port : 8080;
        var scheme = resource.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{resource.Host}:{port}";

        try
        {
            var http = this.httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            if (string.Equals(resource.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await http.GetAsync($"{baseUrl}/api/v2/app/webapiVersion");
                if (resp.IsSuccessStatusCode)
                {
                    var ver = await resp.Content.ReadAsStringAsync();
                    return this.Ok(new DownloadClientTestResult { Success = true, Message = $"qBittorrent WebAPI connected (v{ver.Trim()})." });
                }
            }
            else if (string.Equals(resource.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await http.GetAsync($"{baseUrl}/transmission/rpc");
                if (resp.StatusCode == HttpStatusCode.Conflict || resp.IsSuccessStatusCode)
                {
                    return this.Ok(new DownloadClientTestResult { Success = true, Message = "Transmission RPC endpoint reachable." });
                }
            }
            else if (string.Equals(resource.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                var content = new StringContent("{\"method\":\"auth.check_session\",\"params\":[],\"id\":1}", Encoding.UTF8, "application/json");
                var resp = await http.PostAsync($"{baseUrl}/json", content);
                if (resp.IsSuccessStatusCode)
                {
                    return this.Ok(new DownloadClientTestResult { Success = true, Message = "Deluge JSON-RPC endpoint reachable." });
                }
            }

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(resource.Host, port, cts.Token);
            return this.Ok(new DownloadClientTestResult
            {
                Success = true,
                Message = $"Connected to {resource.ClientType ?? "Client"} socket at {resource.Host}:{port} successfully.",
            });
        }
        catch (Exception ex)
        {
            return this.Ok(new DownloadClientTestResult
            {
                Success = false,
                Message = $"Failed to connect to {resource.Host}:{port} - {ex.Message}",
            });
        }
    }
}
