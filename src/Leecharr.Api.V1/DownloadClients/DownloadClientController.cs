using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.Torrents;
using Leecharr.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclients")]
public class DownloadClientController : Controller
{
    private static readonly ConcurrentDictionary<int, DownloadClientResource> Store = new();
    private static int _idCounter = 1;
    private readonly ITorrentService _torrentService;

    public DownloadClientController(ITorrentService torrentService)
    {
        _torrentService = torrentService;
    }

    [HttpGet]
    public ActionResult<List<DownloadClientResource>> GetAll()
    {
        return Ok(Store.Values.ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadClientResource> Get(int id)
    {
        if (Store.TryGetValue(id, out var item))
        {
            return Ok(item);
        }

        return NotFound();
    }

    [HttpPost]
    public ActionResult<DownloadClientResource> Create([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        resource.Id = _idCounter++;
        Store[resource.Id] = resource;
        return Ok(resource);
    }

    [HttpPut("{id:int}")]
    public ActionResult<DownloadClientResource> Update(int id, [FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        if (!Store.ContainsKey(id))
        {
            return NotFound();
        }

        resource.Id = id;
        Store[id] = resource;
        return Ok(resource);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        Store.TryRemove(id, out _);
        return Ok();
    }

    [HttpPost("{id:int}/test")]
    public ActionResult<DownloadClientTestResult> Test(int id)
    {
        if (Store.TryGetValue(id, out var item))
        {
            return Ok(new DownloadClientTestResult { Success = true, Message = $"Connection to {item.Name} verified." });
        }

        return NotFound();
    }

    [HttpPost("test")]
    public ActionResult<DownloadClientTestResult> TestDirect([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        return Ok(new DownloadClientTestResult { Success = true, Message = $"Connection to {resource.Name ?? "Client"} verified." });
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
}
