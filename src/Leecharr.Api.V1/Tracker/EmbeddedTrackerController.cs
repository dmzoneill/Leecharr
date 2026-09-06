// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.BitTorrent.Tracker;

namespace Leecharr.Api.V1.Tracker;

[AllowAnonymous]
[ApiController]
public class EmbeddedTrackerController : ControllerBase
{
    private readonly IEmbeddedTrackerService trackerService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public EmbeddedTrackerController(IEmbeddedTrackerService trackerService)
    {
        this.trackerService = trackerService;
    }

    [HttpGet("/announce")]
    public ActionResult Announce()
    {
        var rawQuery = this.Request.QueryString.Value;
        var remoteIp = this.HttpContext.Connection.RemoteIpAddress;
        if (remoteIp == null || IPAddress.IsLoopback(remoteIp) || IsPrivateNetwork(remoteIp))
        {
            if (this.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
            {
                var firstIp = forwardedFor.ToString().Split(',')[0].Trim();
                if (IPAddress.TryParse(firstIp, out var parsedFwd))
                {
                    remoteIp = parsedFwd;
                }
            }
            else if (this.Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !string.IsNullOrWhiteSpace(realIp))
            {
                if (IPAddress.TryParse(realIp.ToString().Trim(), out var parsedReal))
                {
                    remoteIp = parsedReal;
                }
            }
        }

        var announceRequest = ParseAnnounceQuery(rawQuery, remoteIp);

        var responseBytes = this.trackerService.ProcessAnnounce(announceRequest);
        return this.File(responseBytes, "text/plain");
    }

    [HttpGet("/scrape")]
    public ActionResult Scrape()
    {
        var rawQuery = this.Request.QueryString.Value;
        var hashes = ParseScrapeQuery(rawQuery);

        var responseBytes = this.trackerService.ProcessScrape(hashes);
        return this.File(responseBytes, "text/plain");
    }

    [HttpGet("/api/v1/trackerserver/stats")]
    [HttpGet("/api/v1/tracker/stats")]
    public ActionResult GetStats()
    {
        return this.Ok(new
        {
            enabled = this.trackerService.IsEnabled,
            activeSwarms = this.trackerService.ActiveSwarmsCount,
            totalTorrents = this.trackerService.ActiveSwarmsCount,
            activePeers = this.trackerService.ActivePeersCount,
            totalPeers = this.trackerService.ActivePeersCount,
            totalAnnounces = 0,
            totalScrapes = 0,
            uptime = 0,
        });
    }

    [HttpGet("/api/v1/trackerserver/torrents")]
    [HttpGet("/api/v1/tracker/torrents")]
    public ActionResult GetTorrents()
    {
        return this.Ok(Array.Empty<object>());
    }

    private static TrackerAnnounceRequest ParseAnnounceQuery(string rawQuery, IPAddress remoteIp)
    {
        var request = new TrackerAnnounceRequest
        {
            RemoteIp = remoteIp ?? IPAddress.Loopback,
            Compact = true,
            NumWant = 50,
        };

        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return request;
        }

        var parts = rawQuery.TrimStart('?').Split('&');
        foreach (var part in parts)
        {
            var kvp = part.Split('=', 2);
            if (kvp.Length != 2)
            {
                continue;
            }

            var key = kvp[0];
            var val = kvp[1];

            if (string.Equals(key, "info_hash", StringComparison.OrdinalIgnoreCase))
            {
                if (val.Length == 40 && IsValidHex(val))
                {
                    request.InfoHashBytes = Convert.FromHexString(val);
                    request.InfoHashHex = val.ToUpperInvariant();
                }
                else
                {
                    request.InfoHashBytes = ParseRawUrlBytes(val);
                    request.InfoHashHex = Convert.ToHexString(request.InfoHashBytes);
                }
            }
            else if (string.Equals(key, "peer_id", StringComparison.OrdinalIgnoreCase))
            {
                request.PeerIdBytes = ParseRawUrlBytes(val);
                request.PeerId = val;
            }
            else if (string.Equals(key, "port", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var port))
            {
                request.Port = port;
            }
            else if (string.Equals(key, "uploaded", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out var up))
            {
                request.Uploaded = up;
            }
            else if (string.Equals(key, "downloaded", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out var down))
            {
                request.Downloaded = down;
            }
            else if (string.Equals(key, "left", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out var left))
            {
                request.Left = left;
            }
            else if (string.Equals(key, "compact", StringComparison.OrdinalIgnoreCase))
            {
                request.Compact = val == "1";
            }
            else if (string.Equals(key, "numwant", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var want))
            {
                request.NumWant = want;
            }
            else if (string.Equals(key, "event", StringComparison.OrdinalIgnoreCase))
            {
                request.Event = val;
            }
            else if ((string.Equals(key, "ip", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "ipv4", StringComparison.OrdinalIgnoreCase)) &&
                     (request.RemoteIp == null || IPAddress.IsLoopback(request.RemoteIp) || IsPrivateNetwork(request.RemoteIp)) &&
                     IPAddress.TryParse(val, out var queryIp))
            {
                request.RemoteIp = queryIp;
            }
        }

        return request;
    }

    private static List<byte[]> ParseScrapeQuery(string rawQuery)
    {
        var list = new List<byte[]>();
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return list;
        }

        var parts = rawQuery.TrimStart('?').Split('&');
        foreach (var part in parts)
        {
            var kvp = part.Split('=', 2);
            if (kvp.Length == 2 && string.Equals(kvp[0], "info_hash", StringComparison.OrdinalIgnoreCase))
            {
                byte[] bytes;
                if (kvp[1].Length == 40 && IsValidHex(kvp[1]))
                {
                    bytes = Convert.FromHexString(kvp[1]);
                }
                else
                {
                    bytes = ParseRawUrlBytes(kvp[1]);
                }

                if (bytes.Length > 0)
                {
                    list.Add(bytes);
                }
            }
        }

        return list;
    }

    private static byte[] ParseRawUrlBytes(string urlEncoded)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < urlEncoded.Length; i++)
        {
            if (urlEncoded[i] == '%' && i + 2 < urlEncoded.Length)
            {
                var hex = urlEncoded.Substring(i + 1, 2);
                if (byte.TryParse(hex, NumberStyles.HexNumber, null, out var b))
                {
                    bytes.Add(b);
                    i += 2;
                    continue;
                }
            }

            bytes.Add((byte)urlEncoded[i]);
        }

        return bytes.ToArray();
    }

    private static bool IsValidHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPrivateNetwork(IPAddress ip)
    {
        if (ip == null || IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == global::System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            if (bytes[0] == 127)
            {
                return true;
            }

            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == global::System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }

            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            {
                return true;
            }
        }

        return false;
    }
}
