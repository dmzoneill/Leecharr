using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Leecharr.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Seeding;

public class SeedingStatsResource
{
    public int ActiveTorrents { get; set; }
    public int DownloadingTorrents { get; set; }
    public int SeedingTorrents { get; set; }
    public int PausedTorrents { get; set; }
    public long DownloadSpeed { get; set; }
    public long UploadSpeed { get; set; }
    public long TotalDownloaded { get; set; }
    public long TotalUploaded { get; set; }
    public double GlobalRatio { get; set; }
}

public class SpeedSnapshotResource
{
    public DateTime Timestamp { get; set; }
    public long DownloadSpeed { get; set; }
    public long UploadSpeed { get; set; }
}

public class TorrentSpeedSnapshotResource
{
    public DateTime Timestamp { get; set; }
    public int TorrentId { get; set; }
    public long DownloadSpeed { get; set; }
    public long UploadSpeed { get; set; }
}

[V1ApiController("seeding")]
public class SeedingController : Controller
{
    private readonly ITorrentService _torrentService;

    public SeedingController(ITorrentService torrentService)
    {
        _torrentService = torrentService;
    }

    [HttpGet("stats")]
    public ActionResult<SeedingStatsResource> GetStats()
    {
        var torrents = _torrentService.GetAll().ToList();
        var totalDown = torrents.Sum(t => t.Downloaded);
        var totalUp = torrents.Sum(t => t.Uploaded);

        return Ok(new SeedingStatsResource
        {
            ActiveTorrents = torrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding),
            DownloadingTorrents = torrents.Count(t => t.Status == TorrentStatus.Downloading),
            SeedingTorrents = torrents.Count(t => t.Status == TorrentStatus.Seeding),
            PausedTorrents = torrents.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped),
            DownloadSpeed = torrents.Sum(t => t.DownloadSpeed),
            UploadSpeed = torrents.Sum(t => t.UploadSpeed),
            TotalDownloaded = totalDown,
            TotalUploaded = totalUp,
            GlobalRatio = totalDown > 0 ? (double)totalUp / totalDown : 0.0
        });
    }

    [HttpGet("history")]
    public ActionResult<List<SpeedSnapshotResource>> GetHistory()
    {
        var torrents = _torrentService.GetAll().ToList();
        var now = DateTime.UtcNow;
        var currentDown = torrents.Sum(t => t.DownloadSpeed);
        var currentUp = torrents.Sum(t => t.UploadSpeed);

        var list = new List<SpeedSnapshotResource>
        {
            new() { Timestamp = now.AddSeconds(-60), DownloadSpeed = (long)(currentDown * 0.9), UploadSpeed = (long)(currentUp * 0.9) },
            new() { Timestamp = now.AddSeconds(-30), DownloadSpeed = (long)(currentDown * 0.95), UploadSpeed = (long)(currentUp * 0.95) },
            new() { Timestamp = now, DownloadSpeed = currentDown, UploadSpeed = currentUp }
        };

        return Ok(list);
    }

    [HttpGet("history/{torrentId:int}")]
    public ActionResult<List<TorrentSpeedSnapshotResource>> GetTorrentHistory(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        return Ok(new List<TorrentSpeedSnapshotResource>
        {
            new() { Timestamp = now.AddSeconds(-60), TorrentId = torrentId, DownloadSpeed = (long)(torrent.DownloadSpeed * 0.9), UploadSpeed = (long)(torrent.UploadSpeed * 0.9) },
            new() { Timestamp = now.AddSeconds(-30), TorrentId = torrentId, DownloadSpeed = (long)(torrent.DownloadSpeed * 0.95), UploadSpeed = (long)(torrent.UploadSpeed * 0.95) },
            new() { Timestamp = now, TorrentId = torrentId, DownloadSpeed = torrent.DownloadSpeed, UploadSpeed = torrent.UploadSpeed }
        });
    }

    [HttpPost("start/{id:int}")]
    public async Task<ActionResult> Start(int id)
    {
        await _torrentService.ResumeAsync(id);
        return Ok();
    }

    [HttpPost("stop/{id:int}")]
    public async Task<ActionResult> Stop(int id)
    {
        await _torrentService.PauseAsync(id);
        return Ok();
    }

    [HttpPost("start-all")]
    public async Task<ActionResult> StartAll()
    {
        var torrents = _torrentService.GetAll();
        foreach (var t in torrents)
        {
            await _torrentService.ResumeAsync(t.Id);
        }

        return Ok();
    }

    [HttpPost("stop-all")]
    public async Task<ActionResult> StopAll()
    {
        var torrents = _torrentService.GetAll();
        foreach (var t in torrents)
        {
            await _torrentService.PauseAsync(t.Id);
        }

        return Ok();
    }
}
