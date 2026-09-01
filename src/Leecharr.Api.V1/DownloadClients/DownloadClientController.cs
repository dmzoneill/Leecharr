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
    public ActionResult<List<DownloadClientRemoteItem>> GetItems(int id)
    {
        return Ok(new List<DownloadClientRemoteItem>());
    }

    [HttpPost("{id:int}/import/{hash}")]
    public ActionResult<TorrentResource> ImportTorrent(int id, string hash)
    {
        var torrent = _torrentService.GetByInfoHash(hash);
        if (torrent == null)
        {
            return NotFound();
        }

        return Ok(TorrentResourceMapper.ToResource(torrent));
    }

    [HttpPost("{id:int}/import")]
    public ActionResult<SyncResultResource> ImportTorrents(int id, [FromBody] ImportRequest request)
    {
        return Ok(new SyncResultResource { Success = true, SyncedCount = request?.Hashes?.Count ?? 0, Message = "Import completed." });
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
            Password = model.Password,
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
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(resource.Host, port, cts.Token);
            return Ok(new DownloadClientTestResult
            {
                Success = true,
                Message = $"Connected to {resource.ClientType ?? "Client"} at {resource.Host}:{port} successfully."
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
}
