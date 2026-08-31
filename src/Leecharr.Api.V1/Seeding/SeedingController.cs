// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private static DateTime lastSampleTime = DateTime.MinValue;

    private readonly ITorrentService torrentService;

    public SeedingController(ITorrentService torrentService)
    {
        this.torrentService = torrentService;
    }

    [HttpGet("stats")]
    public ActionResult<SeedingStatsResource> GetStats()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var totalDown = torrents.Sum(t => t.Downloaded);
        var totalUp = torrents.Sum(t => t.Uploaded);

        RecordSample(torrents);

        return this.Ok(new SeedingStatsResource
        {
            ActiveTorrents = torrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding),
            DownloadingTorrents = torrents.Count(t => t.Status == TorrentStatus.Downloading),
            SeedingTorrents = torrents.Count(t => t.Status == TorrentStatus.Seeding),
            PausedTorrents = torrents.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped),
            DownloadSpeed = torrents.Sum(t => t.DownloadSpeed),
            UploadSpeed = torrents.Sum(t => t.UploadSpeed),
            TotalDownloaded = totalDown,
            TotalUploaded = totalUp,
            GlobalRatio = totalDown > 0 ? (double)totalUp / totalDown : 0.0,
        });
    }

    [HttpGet("history")]
    public ActionResult<List<SpeedSnapshotResource>> GetHistory()
    {
        var torrents = this.torrentService.GetAll().ToList();
        RecordSample(torrents);

        var list = GlobalHistory.ToList();
        return this.Ok(list);
    }

    [HttpGet("history/{torrentId:int}")]
    public ActionResult<List<TorrentSpeedSnapshotResource>> GetTorrentHistory(int torrentId)
    {
        var torrent = this.torrentService.Get(torrentId);
        if (torrent == null)
        {
            return this.NotFound();
        }

        var torrents = this.torrentService.GetAll().ToList();
        RecordSample(torrents);

        if (TorrentHistories.TryGetValue(torrentId, out var queue))
        {
            return this.Ok(queue.ToList());
        }

        return this.Ok(new List<TorrentSpeedSnapshotResource>
        {
            new() { Timestamp = DateTime.UtcNow, TorrentId = torrentId, DownloadSpeed = torrent.DownloadSpeed, UploadSpeed = torrent.UploadSpeed },
        });
    }

    [HttpPost("start/{id:int}")]
    public async Task<ActionResult> Start(int id)
    {
        await this.torrentService.ResumeAsync(id);
        return this.Ok();
    }

    [HttpPost("stop/{id:int}")]
    public async Task<ActionResult> Stop(int id)
    {
        await this.torrentService.PauseAsync(id);
        return this.Ok();
    }

    [HttpPost("start-all")]
    public async Task<ActionResult> StartAll()
    {
        var torrents = this.torrentService.GetAll();
        foreach (var t in torrents)
        {
            await this.torrentService.ResumeAsync(t.Id);
        }

        return this.Ok();
    }

    [HttpPost("stop-all")]
    public async Task<ActionResult> StopAll()
    {
        var torrents = this.torrentService.GetAll();
        foreach (var t in torrents)
        {
            await this.torrentService.PauseAsync(t.Id);
        }

        return this.Ok();
    }

    private static void RecordSample(List<Torrent> torrents)
    {
        var now = DateTime.UtcNow;
        lock (SyncLock)
        {
            if ((now - lastSampleTime).TotalSeconds < 2.0)
            {
                return;
            }

            lastSampleTime = now;

            var totalDown = torrents.Sum(t => t.DownloadSpeed);
            var totalUp = torrents.Sum(t => t.UploadSpeed);

            GlobalHistory.Enqueue(new SpeedSnapshotResource
            {
                Timestamp = now,
                DownloadSpeed = totalDown,
                UploadSpeed = totalUp,
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
                    UploadSpeed = t.UploadSpeed,
                });

                while (q.Count > 120)
                {
                    q.TryDequeue(out _);
                }
            }
        }
    }
}
