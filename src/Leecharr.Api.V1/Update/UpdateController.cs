// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Api.V1.Update;

public class UpdateChangesResource
{
    public List<string> New { get; set; } = new();

    public List<string> Fixed { get; set; } = new();
}

public class UpdateResource : RestResource
{
    public string Version { get; set; }

    public DateTime ReleaseDate { get; set; }

    public string FileName { get; set; }

    public string Url { get; set; }

    public bool Installed { get; set; }

    public bool Latest { get; set; }

    public UpdateChangesResource Changes { get; set; } = new();
}

[V1ApiController("update")]
[Authorize(Policy = "RequireAdmin")]
public class UpdateController : Controller
{
    [HttpGet]
    public ActionResult<List<UpdateResource>> GetUpdates()
    {
        var currentVersion = BuildInfo.Version?.ToString() ?? "1.0.25";
        var list = new List<UpdateResource>
        {
            new()
            {
                Id = 1,
                Version = currentVersion,
                ReleaseDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc),
                FileName = $"Leecharr.{currentVersion}.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases",
                Installed = true,
                Latest = true,
                Changes = new UpdateChangesResource
                {
                    New = new List<string>
                    {
                        "Integrated network diagnostics API with local interface, port mapping, and peer encryption telemetry",
                        "Added responsive stepped range sliders for upload and download speed rate limits in Quick Controls",
                        "Added default completed download directory configuration in Storage settings",
                        "Swarm peer map visualizer styling enhancements for crisp high-contrast node labels",
                    },
                    Fixed = new List<string>
                    {
                        "Fixed log viewer widget sizing to strictly honor parent bounds without double scrollbars",
                        "Resolved torrent state mapping displaying as 'queued' while actively transferring data",
                        "Fixed queue position numbering showing 0 for newly added and existing torrents",
                        "Corrected tracker announce timer to display fixed timestamp instead of continuous counter",
                        "Standardized numeric spin box widths and padding across torrent options tab",
                        "Prevented NaN undefined speed representations in statistics swarm overview graph",
                    },
                },
            },
            new()
            {
                Id = 2,
                Version = "1.0.24",
                ReleaseDate = new DateTime(2026, 8, 28, 14, 30, 0, DateTimeKind.Utc),
                FileName = "Leecharr.1.0.24.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.24",
                Installed = false,
                Latest = false,
                Changes = new UpdateChangesResource
                {
                    New = new List<string>
                    {
                        "Collapsible horizontal navigation sidebar and state/tracker filter pane",
                        "Vertical scrolling container for high-density torrent grid and media poster view",
                        "Native Torznab and Newznab multi-indexer search with category chips and one-click grab",
                    },
                    Fixed = new List<string>
                    {
                        "Resolved tracker status filter displaying 'unknown' for valid announce domains",
                        "Corrected announce interval and next update countdown timers in tracker inspector",
                        "Fixed poster image aspect ratio distortion on wide ultra-wide monitor viewports",
                    },
                },
            },
            new()
            {
                Id = 3,
                Version = "1.0.23",
                ReleaseDate = new DateTime(2026, 8, 20, 11, 15, 0, DateTimeKind.Utc),
                FileName = "Leecharr.1.0.23.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.23",
                Installed = false,
                Latest = false,
                Changes = new UpdateChangesResource
                {
                    New = new List<string>
                    {
                        "Interactive D3 Peer Map visualizer with swarm force graph and country flags",
                        "Dynamic write cache scaling from 128 MB to 1 GB based on system memory",
                        "Sparse file allocation for instant non-blocking torrent startup",
                    },
                    Fixed = new List<string>
                    {
                        "Improved multi-file priority selection handling for multi-gigabyte media torrents",
                        "Fixed SignalR real-time speed pulse reconnection handling on packet drops",
                    },
                },
            },
            new()
            {
                Id = 4,
                Version = "1.0.20",
                ReleaseDate = new DateTime(2026, 8, 10, 16, 0, 0, DateTimeKind.Utc),
                FileName = "Leecharr.1.0.20.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.20",
                Installed = false,
                Latest = false,
                Changes = new UpdateChangesResource
                {
                    New = new List<string>
                    {
                        "Pure C# TagLibSharp and EBML media container inspector (MKV, MP4, AVI, FLAC)",
                        "Audio channel, HDR10+, and Dolby Vision stream metadata parsing",
                        "Drop-in API compatibility adapters for qBittorrent WebAPI v2 and Deluge JSON-RPC",
                    },
                    Fixed = new List<string>
                    {
                        "Fixed Deluge JSON-RPC authentication challenge handshake edge cases",
                        "Corrected Transmission RPC torrent-get status key mapping",
                    },
                },
            },
            new()
            {
                Id = 5,
                Version = "1.0.10",
                ReleaseDate = new DateTime(2026, 7, 25, 9, 45, 0, DateTimeKind.Utc),
                FileName = "Leecharr.1.0.10.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.10",
                Installed = false,
                Latest = false,
                Changes = new UpdateChangesResource
                {
                    New = new List<string>
                    {
                        "24x7 3-tier weekly speed schedule matrix with automated time slot throttling",
                        "Automated VPN Kill Switch binding BitTorrent sockets to tun0/wg0 interfaces",
                        "Servarr webhook notification dispatcher with Polly exponential backoff",
                    },
                    Fixed = new List<string>
                    {
                        "Fixed memory retention in circular metrics buffer during sustained high-speed transfers",
                        "Resolved SQLite migration locking on simultaneous startup",
                    },
                },
            },
            new()
            {
                Id = 6,
                Version = "1.0.0",
                ReleaseDate = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                FileName = "Leecharr.1.0.0.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.0",
                Installed = false,
                Latest = false,
                Changes = new UpdateChangesResource
                {
                    New = new List<string>
                    {
                        "Initial release of Leecharr BitTorrent engine powered by MonoTorrent and .NET 10",
                        "Deep media enrichment & 100% exact correlation with Sonarr, Radarr, Lidarr, and Prowlarr",
                        "Reactive dark UI design system (Midnight Charcoal & Deep Indigo Navy)",
                    },
                    Fixed = new List<string>(),
                },
            },
        };

        return this.Ok(list);
    }
}
