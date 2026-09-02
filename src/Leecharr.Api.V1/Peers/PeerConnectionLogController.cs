// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Peers;

[V1ApiController("peerlog")]
public class PeerConnectionLogController : Controller
{
    private readonly ITorrentService torrentService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IGeoIpService geoIpService;

    public PeerConnectionLogController(
        ITorrentService torrentService,
        IDownloadEngine downloadEngine,
        IGeoIpService geoIpService)
    {
        this.torrentService = torrentService;
        this.downloadEngine = downloadEngine;
        this.geoIpService = geoIpService;
    }

    [HttpGet]
    public ActionResult<List<PeerConnectionLogResource>> GetLogs(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end,
        [FromQuery] string infoHash)
    {
        var logs = new List<PeerConnectionLogResource>();
        var torrents = this.torrentService.GetAll();
        var idCounter = 1;

        foreach (var torrent in torrents)
        {
            if (!string.IsNullOrEmpty(infoHash) && !string.Equals(torrent.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var task = this.downloadEngine.GetTask(torrent.Id);
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

    [HttpGet("active")]
    public ActionResult<List<PeerConnectionLogResource>> GetActive()
    {
        return this.GetLogs(null, null, null);
    }

    [HttpGet("graph")]
    public ActionResult<PeerGraphResource> GetGraph(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end)
    {
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

        var torrents = this.torrentService.GetAll();
        foreach (var torrent in torrents)
        {
            var hash = torrent.InfoHash ?? torrent.Id.ToString();
            if (seenTorrents.Add(hash))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"torrent:{hash}",
                    Label = torrent.Name ?? (hash.Length > 8 ? hash[..8] : hash),
                    Type = "torrent",
                    InfoHash = torrent.InfoHash,
                });

                links.Add(new PeerGraphLink
                {
                    Source = "leecharr",
                    Target = $"torrent:{hash}",
                    Type = "seeds",
                });
            }

            var task = this.downloadEngine.GetTask(torrent.Id);
            if (task != null)
            {
                foreach (var peer in task.GetPeers())
                {
                    var peerKey = $"{peer.Ip}:{peer.Port}";
                    if (seenPeers.Add($"{peerKey}:{hash}"))
                    {
                        nodes.Add(new PeerGraphNode
                        {
                            Id = $"peer:{peerKey}:{hash}",
                            Label = peer.Ip,
                            Type = "peer",
                            IsEncrypted = peer.IsEncrypted,
                        });

                        links.Add(new PeerGraphLink
                        {
                            Source = $"torrent:{hash}",
                            Target = $"peer:{peerKey}:{hash}",
                            Type = peer.IsEncrypted ? "encrypted" : "plain",
                        });
                    }
                }
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
        return this.Ok();
    }
}
