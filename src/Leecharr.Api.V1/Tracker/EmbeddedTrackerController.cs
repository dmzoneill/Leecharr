// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Tracker;

[ApiController]
public class EmbeddedTrackerController : ControllerBase
{
    private readonly IEmbeddedTrackerService trackerService;
    private readonly ITorrentRepository torrentRepository;
    private readonly IDownloadHistoryRepository downloadHistoryRepository;
    private readonly ITorrentMediaMetadataRepository mediaMetadataRepository;
    private readonly ITrustedNetworkService trustedNetworkService;
    private readonly IConfigService configService;
    private readonly ITrackerClientRateLimiter rateLimiter;
    private readonly IEmbeddedTrackerHttpProcessor httpProcessor;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private static readonly char[] GenreSeparators = [',', ';'];

    public EmbeddedTrackerController(
        IEmbeddedTrackerService trackerService,
        ITorrentRepository torrentRepository = null,
        IDownloadHistoryRepository downloadHistoryRepository = null,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        ITrustedNetworkService trustedNetworkService = null,
        IConfigService configService = null,
        ITrackerClientRateLimiter rateLimiter = null,
        IEmbeddedTrackerHttpProcessor httpProcessor = null)
    {
        this.trackerService = trackerService;
        this.torrentRepository = torrentRepository;
        this.downloadHistoryRepository = downloadHistoryRepository;
        this.mediaMetadataRepository = mediaMetadataRepository;
        this.trustedNetworkService = trustedNetworkService ?? new TrustedNetworkService();
        this.configService = configService;
        this.rateLimiter = rateLimiter ?? new TrackerClientRateLimiter();
        this.httpProcessor = httpProcessor
            ?? new EmbeddedTrackerHttpProcessor(trackerService, configService, rateLimiter, this.trustedNetworkService);
    }

    [AllowAnonymous]
    [HttpGet("/announce")]
    public ActionResult Announce()
    {
        if (!this.ShouldAcceptHttpTrackerOnThisEndpoint())
        {
            return this.NotFound();
        }

        var response = this.httpProcessor.ProcessAnnounce(this.BuildHttpRequest());
        if (response.IsNotFound)
        {
            return this.NotFound();
        }

        return this.File(response.Body, response.ContentType);
    }

    [AllowAnonymous]
    [HttpGet("/scrape")]
    public ActionResult Scrape()
    {
        if (!this.ShouldAcceptHttpTrackerOnThisEndpoint())
        {
            return this.NotFound();
        }

        var response = this.httpProcessor.ProcessScrape(this.BuildHttpRequest());
        if (response.IsNotFound)
        {
            return this.NotFound();
        }

        return this.File(response.Body, response.ContentType);
    }

    [Authorize]
    [HttpGet("/api/v1/trackerserver/stats")]
    [HttpGet("/api/v1/tracker/stats")]
    public ActionResult GetStats()
    {
        var swarms = this.trackerService.GetAllSwarms();
        var internalCount = 0;
        if (swarms.Count > 0 && this.torrentRepository != null)
        {
            var torrents = this.torrentRepository.All().ToList();
            var knownHashes = new HashSet<string>(
                torrents.Where(t => !string.IsNullOrEmpty(t.InfoHash)).Select(t => t.InfoHash),
                StringComparer.OrdinalIgnoreCase);

            foreach (var swarm in swarms)
            {
                if (knownHashes.Contains(swarm.InfoHash))
                {
                    internalCount++;
                }
            }
        }

        return this.Ok(new
        {
            enabled = this.trackerService.IsEnabled,
            activeSwarms = this.trackerService.ActiveSwarmsCount,
            totalTorrents = this.trackerService.ActiveSwarmsCount,
            internalTorrents = internalCount,
            activePeers = this.trackerService.ActivePeersCount,
            totalPeers = this.trackerService.ActivePeersCount,
            totalAnnounces = this.trackerService.TotalAnnounces,
            totalScrapes = this.trackerService.TotalScrapes,
            uptime = (long)this.trackerService.Uptime.TotalSeconds,
        });
    }

    [Authorize]
    [HttpGet("/api/v1/trackerserver/torrents")]
    [HttpGet("/api/v1/tracker/torrents")]
    public ActionResult GetTorrents()
    {
        var swarms = this.trackerService.GetAllSwarms();
        if (swarms == null || swarms.Count == 0)
        {
            return this.Ok(Array.Empty<TrackerSwarmInfo>());
        }

        var torrentsByHash = new Dictionary<string, Torrent>(StringComparer.OrdinalIgnoreCase);
        if (this.torrentRepository != null)
        {
            foreach (var t in this.torrentRepository.All())
            {
                if (!string.IsNullOrEmpty(t.InfoHash))
                {
                    torrentsByHash[t.InfoHash] = t;
                }
            }
        }

        var result = swarms.Select(swarm =>
        {
            var hash = swarm.InfoHash;
            torrentsByHash.TryGetValue(hash, out var torrent);

            DownloadHistory hist = null;
            if (this.downloadHistoryRepository != null)
            {
                try
                {
                    hist = this.downloadHistoryRepository.FindByInfoHash(hash);
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to find download history for infohash {Hash}", hash);
                }
            }

            TorrentMediaMetadata meta = null;
            if (torrent != null && this.mediaMetadataRepository != null)
            {
                try
                {
                    meta = this.mediaMetadataRepository.GetByTorrentId(torrent.Id);
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to retrieve media metadata for torrent {TorrentId}", torrent.Id);
                }
            }

            var posterUrl = meta?.PosterUrl;
            var fanartUrl = meta?.BackdropUrl;
            var mediaTitle = meta?.Title;
            int? year = meta?.Year > 0 ? meta.Year : null;
            double? rating = meta?.Rating > 0 ? meta.Rating : null;
            var genres = !string.IsNullOrWhiteSpace(meta?.Genres)
                ? meta.Genres.Split(GenreSeparators, StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim()).ToList()
                : new List<string>();

            var source = torrent != null ? (torrent.IsPrivate ? "Private Tracker" : "Public Tracker") : (hist?.Source ?? "External");

            var peers = this.trackerService.GetPeersForSwarm(hash);
            var peerCount = peers.Count > 0 ? peers.Count : (swarm.Seeders + swarm.Leechers);
            DateTime? lastActivity = peers.Count > 0
                ? peers.Max(p => p.LastAnnounceUtc)
                : (swarm.LastActivityUtc != default ? swarm.LastActivityUtc : null);

            return new
            {
                infoHash = hash,
                name = torrent?.Name ?? mediaTitle ?? hist?.Title ?? hash,
                peerCount = peerCount,
                seeders = swarm.Seeders,
                leechers = swarm.Leechers,
                completed = swarm.DownloadedCount,
                uploaded = torrent?.Uploaded ?? hist?.Uploaded ?? 0L,
                downloaded = torrent?.Downloaded ?? hist?.Downloaded ?? 0L,
                totalSize = torrent?.TotalSize ?? hist?.TotalSize ?? 0L,
                ratio = torrent?.Ratio ?? hist?.Ratio ?? 0.0,
                isInternal = torrent != null,
                lastActivity = lastActivity,
                posterUrl = posterUrl,
                fanartUrl = fanartUrl,
                mediaTitle = mediaTitle,
                year = year,
                rating = rating,
                genres = genres,
                source = source,
            };
        }).ToList();

        return this.Ok(result);
    }

    [Authorize]
    [HttpGet("/api/v1/trackerserver/torrents/{infoHash}/peers")]
    [HttpGet("/api/v1/tracker/torrents/{infoHash}/peers")]
    public ActionResult GetPeersForTorrent(string infoHash)
    {
        var peers = this.trackerService.GetPeersForSwarm(infoHash);
        var result = peers.Select(p => new
        {
            ip = p.Ip?.ToString() ?? "0.0.0.0",
            port = p.Port,
            peerId = p.PeerId != null ? Convert.ToHexString(p.PeerId) : string.Empty,
            lastAnnounce = p.LastAnnounceUtc,
        }).ToList();

        return this.Ok(result);
    }

    private bool ShouldAcceptHttpTrackerOnThisEndpoint()
    {
        var localPort = this.HttpContext.Connection.LocalPort;
        if (!TrackerHttpEndpointPolicy.ShouldAcceptOnLocalPort(this.configService, localPort))
        {
            return false;
        }

        return this.IsHttpTrackerRequestOnBindAddress();
    }

    private EmbeddedTrackerHttpRequest BuildHttpRequest()
    {
        return new EmbeddedTrackerHttpRequest
        {
            RawQuery = this.Request.QueryString.Value,
            RemoteIp = this.HttpContext.Connection.RemoteIpAddress,
            LocalIp = this.HttpContext.Connection.LocalIpAddress,
            ForwardedFor = this.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) ? forwardedFor.ToString() : null,
            RealIp = this.Request.Headers.TryGetValue("X-Real-IP", out var realIp) ? realIp.ToString() : null,
        };
    }

    internal static TrackerAnnounceRequest ParseAnnounceQuery(string rawQuery, IPAddress remoteIp)
    {
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var request = new TrackerAnnounceRequest
        {
            RemoteIp = remoteIp ?? IPAddress.Loopback,
            Compact = true,
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
            else if ((string.Equals(key, "numwant", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "num_want", StringComparison.OrdinalIgnoreCase)) && int.TryParse(val, out var want))
            {
                request.NumWant = want;
            }
            else if (string.Equals(key, "event", StringComparison.OrdinalIgnoreCase))
            {
                request.Event = val;
            }
            else if (string.Equals(key, "no_peer_id", StringComparison.OrdinalIgnoreCase))
            {
                request.NoPeerId = val == "1";
            }
            else if (string.Equals(key, "trackerid", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "tracker_id", StringComparison.OrdinalIgnoreCase))
            {
                request.TrackerId = Uri.UnescapeDataString(val);
            }
            else if (string.Equals(key, "ipv6", StringComparison.OrdinalIgnoreCase))
            {
                var unescapedVal = Uri.UnescapeDataString(val);
                var ipStr = unescapedVal;
                int? explicitPort = null;
                if (ipStr.StartsWith('['))
                {
                    var closeBracket = ipStr.IndexOf(']');
                    if (closeBracket > 0)
                    {
                        if (closeBracket + 2 < ipStr.Length && ipStr[closeBracket + 1] == ':' && int.TryParse(ipStr.AsSpan(closeBracket + 2), out var p))
                        {
                            explicitPort = p;
                        }

                        ipStr = ipStr.Substring(1, closeBracket - 1);
                    }
                }

                if (IPAddress.TryParse(ipStr, out var queryIpv6))
                {
                    if (queryIpv6.IsIPv4MappedToIPv6)
                    {
                        queryIpv6 = queryIpv6.MapToIPv4();
                    }

                    request.Ipv6 = queryIpv6;
                    if (explicitPort.HasValue)
                    {
                        request.Ipv6Port = explicitPort.Value;
                    }

                    if ((request.RemoteIp == null || IPAddress.IsLoopback(request.RemoteIp) || IsPrivateNetwork(request.RemoteIp)) &&
                        (IPAddress.IsLoopback(request.RemoteIp) || !IsLoopbackOrLinkLocal(queryIpv6)))
                    {
                        request.RemoteIp = queryIpv6;
                    }
                }
            }
            else if ((string.Equals(key, "ip", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "ipv4", StringComparison.OrdinalIgnoreCase)) &&
                (request.RemoteIp == null || IPAddress.IsLoopback(request.RemoteIp) || IsPrivateNetwork(request.RemoteIp)) &&
                IPAddress.TryParse(val, out var queryIp))
            {
                if (queryIp.IsIPv4MappedToIPv6)
                {
                    queryIp = queryIp.MapToIPv4();
                }

                if (IPAddress.IsLoopback(request.RemoteIp) || !IsLoopbackOrLinkLocal(queryIp))
                {
                    request.RemoteIp = queryIp;
                }
            }
        }

        if (request.RemoteIp != null && request.RemoteIp.IsIPv4MappedToIPv6)
        {
            request.RemoteIp = request.RemoteIp.MapToIPv4();
        }

        return request;
    }

    internal static List<byte[]> ParseScrapeQuery(string rawQuery)
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

    internal static IPAddress ResolveRemoteIp(
        EmbeddedTrackerHttpRequest request,
        ITrustedNetworkService trustedNetworkService,
        IConfigService configService)
    {
        var remoteIp = request.RemoteIp;
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var trustedCidrs = GetTrustedProxies(configService);
        if (remoteIp != null && (IPAddress.IsLoopback(remoteIp) || trustedNetworkService.IsTrustedProxy(remoteIp, trustedCidrs)))
        {
            IPAddress candidateIp = null;
            if (!string.IsNullOrWhiteSpace(request.ForwardedFor))
            {
                var firstIp = request.ForwardedFor.Split(',')[0].Trim();
                if (IPAddress.TryParse(firstIp, out var parsedFwd))
                {
                    candidateIp = parsedFwd.IsIPv4MappedToIPv6 ? parsedFwd.MapToIPv4() : parsedFwd;
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.RealIp) && IPAddress.TryParse(request.RealIp.Trim(), out var parsedReal))
            {
                candidateIp = parsedReal.IsIPv4MappedToIPv6 ? parsedReal.MapToIPv4() : parsedReal;
            }

            if (candidateIp != null)
            {
                var isOriginatingLocally = IPAddress.IsLoopback(remoteIp);
                if (isOriginatingLocally || !IsLoopbackOrLinkLocal(candidateIp))
                {
                    remoteIp = candidateIp;
                }
            }
        }

        return remoteIp;
    }

    internal static EmbeddedTrackerHttpResponse RateLimitResponseIfNeeded(
        IPAddress clientIp,
        IConfigService configService,
        ITrackerClientRateLimiter rateLimiter)
    {
        var limit = configService?.TrackerRateLimitPerMinute ?? 0;
        if (clientIp != null && rateLimiter.IsRateLimited(clientIp, limit))
        {
            return EmbeddedTrackerHttpResponse.Plain(TrackerProtocolEncoding.BuildFailure("Rate limit exceeded."));
        }

        return null;
    }

    internal static bool IsHttpTrackerRequestOnBindAddress(IConfigService configService, IPAddress localIpAddress)
    {
        var bindAddress = configService?.TrackerBindAddress;
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return true;
        }

        return TrackerBindAddressHelper.IsHttpRequestAllowed(bindAddress, localIpAddress);
    }

    private bool IsHttpTrackerRequestOnBindAddress()
    {
        return IsHttpTrackerRequestOnBindAddress(this.configService, this.HttpContext.Connection.LocalIpAddress);
    }

    internal static string GetTrustedProxies(IConfigService configService)
    {
        var cidrs = configService?.GetValue("TrackerTrustedProxies", string.Empty);
        if (!string.IsNullOrWhiteSpace(cidrs))
        {
            return cidrs;
        }

        cidrs = configService?.GetValue("ForwardAuthTrustedProxies", string.Empty);
        if (!string.IsNullOrWhiteSpace(cidrs))
        {
            return cidrs;
        }

        return configService?.GetValue("TrustedProxies", string.Empty) ?? string.Empty;
    }

    internal static bool IsLoopbackOrLinkLocal(IPAddress ip)
    {
        return ClientIpResolver.IsLoopbackOrLinkLocal(ip);
    }

    internal static bool IsPrivateNetwork(IPAddress ip)
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
