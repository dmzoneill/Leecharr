// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Peers;

[V1ApiController("peerlog")]
public class PeerConnectionLogController : Controller
{
    private readonly ITorrentService torrentService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IGeoIpService geoIpService;
    private readonly IPeerConnectionHistoryService historyService;

    public PeerConnectionLogController(
        ITorrentService torrentService,
        IDownloadEngine downloadEngine,
        IGeoIpService geoIpService,
        IPeerConnectionHistoryService historyService = null)
    {
        this.torrentService = torrentService;
        this.downloadEngine = downloadEngine;
        this.geoIpService = geoIpService;
        this.historyService = historyService ?? new PeerConnectionHistoryService(geoIpService);
    }

    private void IngestActivePeers()
    {
        var torrents = this.torrentService?.GetAll();
        if (torrents == null)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            var task = this.downloadEngine?.GetTask(torrent.Id);
            if (task != null)
            {
                foreach (var peer in task.GetPeers())
                {
                    this.historyService.RecordEvent(new PeerConnectionEvent
                    {
                        InfoHash = torrent.InfoHash,
                        TorrentName = torrent.Name,
                        RemoteIp = peer.Ip,
                        RemotePort = peer.Port,
                        PeerId = peer.Client,
                        IsEncrypted = peer.IsEncrypted,
                        EventType = "Connected",
                        Timestamp = DateTime.UtcNow,
                    });
                }
            }
        }
    }

    [HttpGet]
    public ActionResult<List<PeerConnectionLogResource>> GetLogs(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end,
        [FromQuery] string infoHash)
    {
        this.IngestActivePeers();
        var records = this.historyService.GetRecords(start, end, infoHash);
        var logs = records.Select(r => new PeerConnectionLogResource
        {
            Id = (int)r.Id,
            InfoHash = r.InfoHash,
            TorrentName = r.TorrentName,
            RemoteIp = r.RemoteIp,
            RemotePort = r.RemotePort,
            PeerId = r.PeerId,
            IsEncrypted = r.IsEncrypted,
            CountryCode = r.CountryCode ?? string.Empty,
            CountryName = r.CountryName ?? string.Empty,
            City = r.City ?? string.Empty,
            EventType = r.EventType,
            Timestamp = r.Timestamp,
        }).ToList();

        return this.Ok(logs);
    }

    [HttpGet("active")]
    public ActionResult<List<PeerConnectionLogResource>> GetActive()
    {
        var logs = new List<PeerConnectionLogResource>();
        var torrents = this.torrentService?.GetAll() ?? new List<Torrent>();
        var idCounter = 1;

        foreach (var torrent in torrents)
        {
            var task = this.downloadEngine?.GetTask(torrent.Id);
            if (task != null)
            {
                foreach (var peer in task.GetPeers())
                {
                    var geo = this.geoIpService?.Lookup(peer.Ip);

                    logs.Add(new PeerConnectionLogResource
                    {
                        Id = idCounter++,
                        InfoHash = torrent.InfoHash,
                        TorrentName = torrent.Name,
                        RemoteIp = peer.Ip,
                        RemotePort = peer.Port,
                        PeerId = peer.Client,
                        IsEncrypted = peer.IsEncrypted,
                        CountryCode = geo?.CountryCode ?? string.Empty,
                        CountryName = geo?.CountryName ?? string.Empty,
                        City = geo?.City ?? string.Empty,
                        EventType = "Connected",
                        Timestamp = DateTime.UtcNow,
                    });
                }
            }
        }

        return this.Ok(logs);
    }

    [HttpGet("graph")]
    public ActionResult<PeerGraphResource> GetGraph(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end)
    {
        this.IngestActivePeers();
        var records = this.historyService.GetRecords(start, end);
        var nodes = new List<PeerGraphNode>();
        var links = new List<PeerGraphLink>();
        var seenTorrents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPeers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        nodes.Add(new PeerGraphNode
        {
            Id = "leecharr",
            Label = "Leecharr",
            Type = "center",
        });

        foreach (var record in records)
        {
            var hash = record.InfoHash ?? "unknown";
            if (seenTorrents.Add(hash))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"torrent:{hash}",
                    Label = record.TorrentName ?? (hash.Length > 8 ? hash[..8] : hash),
                    Type = "torrent",
                    InfoHash = record.InfoHash,
                });

                links.Add(new PeerGraphLink
                {
                    Source = "leecharr",
                    Target = $"torrent:{hash}",
                    Type = "seeds",
                });
            }

            var peerKey = $"{record.RemoteIp}:{record.RemotePort}";
            if (seenPeers.Add($"{peerKey}:{hash}"))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"peer:{peerKey}:{hash}",
                    Label = record.RemoteIp,
                    Type = "peer",
                    IsEncrypted = record.IsEncrypted,
                });

                links.Add(new PeerGraphLink
                {
                    Source = $"torrent:{hash}",
                    Target = $"peer:{peerKey}:{hash}",
                    Type = record.IsEncrypted ? "encrypted" : "plain",
                });
            }
        }

        return this.Ok(new PeerGraphResource
        {
            Nodes = nodes,
            Links = links,
        });
    }

    [HttpDelete]
    public ActionResult Purge([FromQuery] DateTime? before)
    {
        this.historyService.Purge(before);
        return this.Ok();
    }
}

