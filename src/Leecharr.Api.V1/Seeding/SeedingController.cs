using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
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
    private static readonly ConcurrentQueue<SpeedSnapshotResource> GlobalHistory = new();
    private static readonly ConcurrentDictionary<int, ConcurrentQueue<TorrentSpeedSnapshotResource>> TorrentHistories = new();
    private static readonly object SyncLock = new();
    private static DateTime _lastSampleTime = DateTime.MinValue;

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

        RecordSample(torrents);

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
        RecordSample(torrents);

        var list = GlobalHistory.ToList();
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

        var torrents = _torrentService.GetAll().ToList();
        RecordSample(torrents);

        if (TorrentHistories.TryGetValue(torrentId, out var queue))
        {
            return Ok(queue.ToList());
        }

        return Ok(new List<TorrentSpeedSnapshotResource>
        {
            new() { Timestamp = DateTime.UtcNow, TorrentId = torrentId, DownloadSpeed = torrent.DownloadSpeed, UploadSpeed = torrent.UploadSpeed }
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

    private static void RecordSample(List<Torrent> torrents)
    {
        var now = DateTime.UtcNow;
        lock (SyncLock)
        {
            if ((now - _lastSampleTime).TotalSeconds < 2.0)
            {
                return;
            }

            _lastSampleTime = now;

            var totalDown = torrents.Sum(t => t.DownloadSpeed);
            var totalUp = torrents.Sum(t => t.UploadSpeed);

            GlobalHistory.Enqueue(new SpeedSnapshotResource
            {
                Timestamp = now,
                DownloadSpeed = totalDown,
                UploadSpeed = totalUp
            });

            while (GlobalHistory.Count > 120)
            {
                GlobalHistory.TryDequeue(out _);
            }

            foreach (var t in torrents)
            {
                var q = TorrentHistories.GetOrAdd(t.Id, _ => new ConcurrentQueue<TorrentSpeedSnapshotResource>());
                q.Enqueue(new TorrentSpeedSnapshotResource
                {
                    Timestamp = now,
                    TorrentId = t.Id,
                    DownloadSpeed = t.DownloadSpeed,
                    UploadSpeed = t.UploadSpeed
                });

                while (q.Count > 120)
                {
                    q.TryDequeue(out _);
                }
            }
        }
    }
}
