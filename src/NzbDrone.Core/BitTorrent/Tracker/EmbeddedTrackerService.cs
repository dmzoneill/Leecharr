// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MonoTorrent.BEncoding;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent.Tracker;

public class TrackerPeerState
{
    public byte[] PeerId { get; set; }

    public IPAddress Ip { get; set; }

    public int Port { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public long Left { get; set; }

    public DateTime LastAnnounceUtc { get; set; } = DateTime.UtcNow;

    public bool IsSeeder => this.Left == 0;
}

public class SwarmState
{
    public byte[] InfoHash { get; set; }

    public ConcurrentDictionary<string, TrackerPeerState> Peers { get; } = new();

    public long DownloadedCount { get; set; }

    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    public bool IsRegistered { get; set; }
}

public class EmbeddedTrackerService : IEmbeddedTrackerService,
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<ApplicationStartedEvent>,
    IDisposable
{
    public const int DefaultMaxSwarms = 20_000;

    private readonly IConfigService configService;
    private readonly ITorrentRepository torrentRepository;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<string, SwarmState> swarms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer cleanupTimer;
    private int disposed;

    public EmbeddedTrackerService(
        IConfigService configService = null,
        ITorrentRepository torrentRepository = null,
        int? maxSwarms = null)
    {
        this.configService = configService;
        this.torrentRepository = torrentRepository;
        this.MaxSwarms = maxSwarms ?? (configService != null && configService.TrackerMaxSwarms > 0
            ? configService.TrackerMaxSwarms
            : DefaultMaxSwarms);

        this.cleanupTimer = new Timer(
            _ =>
            {
                try
                {
                    this.PruneInactivePeers();
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error occurred during background tracker peer pruning.");
                }
            },
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public bool IsEnabled => this.configService?.TrackerServerEnabled ?? true;

    public int ActiveSwarmsCount => this.swarms.Count;

    public int ActivePeersCount => this.swarms.Values.Sum(s => s.Peers.Count);

    public int MaxSwarms { get; set; }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent?.InfoHash != null)
        {
            this.RegisterSwarm(message.Torrent.InfoHash);
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent?.InfoHash != null)
        {
            this.UnregisterSwarm(message.Torrent.InfoHash);
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        if (this.torrentRepository != null)
        {
            try
            {
                var torrents = this.torrentRepository.All();
                if (torrents != null)
                {
                    foreach (var torrent in torrents)
                    {
                        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
                        {
                            this.RegisterSwarm(torrent.InfoHash);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to register existing torrents with embedded tracker on startup.");
            }
        }
    }

    public void RegisterSwarm(string infoHashHex)
    {
        if (string.IsNullOrWhiteSpace(infoHashHex))
        {
            return;
        }

        if (!TryValidateInfoHashHex(infoHashHex, out var normalizedHex, out var normalizedBytes, out var failureReason))
        {
            this.logger.Warn("Failed to register swarm: {0}", failureReason);
            return;
        }

        if (this.swarms.TryGetValue(normalizedHex, out var existing))
        {
            lock (existing)
            {
                existing.IsRegistered = true;
                existing.LastActivityUtc = DateTime.UtcNow;
            }

            return;
        }

        if (!this.TryAcquireSwarmSlot())
        {
            this.logger.Warn("Failed to register swarm: maximum number of swarms reached ({0}).", this.MaxSwarms);
            return;
        }

        var swarm = this.swarms.GetOrAdd(normalizedHex, _ => new SwarmState
        {
            InfoHash = normalizedBytes,
            LastActivityUtc = DateTime.UtcNow,
            IsRegistered = true,
        });

        lock (swarm)
        {
            swarm.IsRegistered = true;
            swarm.LastActivityUtc = DateTime.UtcNow;
        }
    }

    public void UnregisterSwarm(string infoHashHex)
    {
        if (string.IsNullOrWhiteSpace(infoHashHex))
        {
            return;
        }

        string normalizedHex;
        if (!TryValidateInfoHashHex(infoHashHex, out normalizedHex, out _, out _))
        {
            normalizedHex = infoHashHex.Trim().ToUpperInvariant();
        }

        if (this.swarms.TryGetValue(normalizedHex, out var swarm))
        {
            lock (swarm)
            {
                if (swarm.Peers.IsEmpty)
                {
                    ((ICollection<KeyValuePair<string, SwarmState>>)this.swarms).Remove(
                        new KeyValuePair<string, SwarmState>(normalizedHex, swarm));
                }
                else
                {
                    swarm.IsRegistered = false;
                }
            }
        }
    }

    public TrackerAnnounceResult Announce(TrackerAnnounceRequest request)
    {
        if (!this.IsEnabled)
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Embedded tracker is disabled." };
        }

        if (request == null)
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Missing request." };
        }

        if (!TryValidateInfoHash(request.InfoHashBytes, request.InfoHashHex, out var hexKey, out var validBytes, out var hashError))
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = hashError };
        }

        var isPrivate = this.configService?.TrackerPrivateMode ?? false;
        if (isPrivate && (!this.swarms.TryGetValue(hexKey, out var registeredSwarm) || !registeredSwarm.IsRegistered))
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Torrent not registered on this private tracker." };
        }

        var isStopped = string.Equals(request.Event, "stopped", StringComparison.OrdinalIgnoreCase);
        var isCompleted = string.Equals(request.Event, "completed", StringComparison.OrdinalIgnoreCase);
        var peerKey = $"{request.RemoteIp}:{request.Port}";

        if (isStopped && !this.swarms.ContainsKey(hexKey))
        {
            var announceInterval = this.configService?.TrackerAnnounceInterval ?? 1800;
            return new TrackerAnnounceResult
            {
                Success = true,
                Interval = announceInterval,
                MinInterval = Math.Min(300, announceInterval / 2),
                Seeders = 0,
                Leechers = 0,
                Peers = Array.Empty<TrackerPeerState>(),
            };
        }

        SwarmState swarm;
        while (true)
        {
            if (!this.swarms.TryGetValue(hexKey, out swarm))
            {
                if (!this.TryAcquireSwarmSlot())
                {
                    return new TrackerAnnounceResult { Success = false, FailureReason = "Tracker swarm limit reached." };
                }

                var newSwarm = new SwarmState
                {
                    InfoHash = validBytes,
                    LastActivityUtc = DateTime.UtcNow,
                };

                swarm = this.swarms.GetOrAdd(hexKey, newSwarm);
            }

            lock (swarm)
            {
                if (this.swarms.TryGetValue(hexKey, out var current) && ReferenceEquals(current, swarm))
                {
                    swarm.LastActivityUtc = DateTime.UtcNow;

                    if (isStopped)
                    {
                        swarm.Peers.TryRemove(peerKey, out _);

                        if (swarm.Peers.IsEmpty)
                        {
                            if (!swarm.IsRegistered)
                            {
                                ((ICollection<KeyValuePair<string, SwarmState>>)this.swarms).Remove(
                                    new KeyValuePair<string, SwarmState>(hexKey, swarm));
                            }
                        }
                    }
                    else
                    {
                        var wasSeeder = swarm.Peers.TryGetValue(peerKey, out var existingPeer) && existingPeer.IsSeeder;

                        var peer = swarm.Peers.GetOrAdd(peerKey, _ => new TrackerPeerState());
                        peer.PeerId = request.PeerIdBytes;
                        peer.Ip = request.RemoteIp;
                        peer.Port = request.Port;
                        peer.Uploaded = request.Uploaded;
                        peer.Downloaded = request.Downloaded;
                        peer.Left = request.Left;
                        peer.LastAnnounceUtc = DateTime.UtcNow;

                        if (isCompleted && !wasSeeder)
                        {
                            swarm.DownloadedCount++;
                        }
                    }

                    break;
                }
            }
        }

        var interval = this.configService?.TrackerAnnounceInterval ?? 1800;
        this.PruneStalePeers(swarm, TimeSpan.FromSeconds(interval * 2), hexKey);

        var seeders = swarm.Peers.Values.Count(p => p.IsSeeder);
        var leechers = swarm.Peers.Values.Count(p => !p.IsSeeder);

        var eligiblePeers = swarm.Peers.Values
            .Where(p => !Equals(p.Ip, request.RemoteIp) || p.Port != request.Port)
            .Where(p => request.Left != 0 || !p.IsSeeder)
            .ToArray();

        Random.Shared.Shuffle(eligiblePeers);

        var numWant = request.NumWant > 0 ? request.NumWant : (this.configService?.TrackerMaxPeersPerAnnounce ?? 50);
        var candidatePeers = eligiblePeers.Take(numWant).ToList();

        return new TrackerAnnounceResult
        {
            Success = true,
            Interval = interval,
            MinInterval = Math.Min(300, interval / 2),
            Seeders = seeders,
            Leechers = leechers,
            Peers = candidatePeers,
        };
    }

    public byte[] ProcessAnnounce(TrackerAnnounceRequest request)
    {
        var result = this.Announce(request);
        if (!result.Success)
        {
            return FailureResponse(result.FailureReason);
        }

        return this.BuildAnnounceResponse(result.Seeders, result.Leechers, result.Peers, request);
    }

    public TrackerScrapeResult Scrape(List<byte[]> infoHashList)
    {
        if (!this.IsEnabled)
        {
            return new TrackerScrapeResult { Success = false, FailureReason = "Embedded tracker is disabled." };
        }

        if (this.configService?.TrackerEnableScrape == false)
        {
            return new TrackerScrapeResult { Success = false, FailureReason = "Scrape is disabled." };
        }

        if (this.configService?.TrackerPrivateMode == true &&
            (infoHashList == null || infoHashList.Count == 0))
        {
            return new TrackerScrapeResult { Success = false, FailureReason = "Wildcard scrape not allowed on private tracker." };
        }

        var list = new List<TrackerScrapeItem>();

        if (infoHashList != null && infoHashList.Count > 0)
        {
            foreach (var rawHash in infoHashList)
            {
                if (rawHash == null || IsAllZeros(rawHash))
                {
                    continue;
                }

                string hexKey = null;
                byte[] binaryHash = null;

                if (rawHash.Length == 20)
                {
                    hexKey = ConvertBytesToHex(rawHash);
                    binaryHash = rawHash;
                }
                else if (rawHash.Length == 40)
                {
                    var hexStr = System.Text.Encoding.ASCII.GetString(rawHash);
                    if (IsValidHex(hexStr))
                    {
                        hexKey = hexStr.ToUpperInvariant();
                        binaryHash = ConvertHexToBytes(hexStr);
                    }
                }

                if (hexKey != null && binaryHash != null && this.swarms.TryGetValue(hexKey, out var swarm))
                {
                    list.Add(new TrackerScrapeItem
                    {
                        InfoHash = binaryHash,
                        Seeders = swarm.Peers.Values.Count(p => p.IsSeeder),
                        Downloaded = swarm.DownloadedCount,
                        Leechers = swarm.Peers.Values.Count(p => !p.IsSeeder),
                    });
                }
            }
        }
        else
        {
            foreach (var kvp in this.swarms)
            {
                var hexKey = kvp.Key;
                var swarm = kvp.Value;
                var hashBytes = (swarm.InfoHash != null && swarm.InfoHash.Length == 20)
                    ? swarm.InfoHash
                    : ConvertHexToBytes(hexKey);

                list.Add(new TrackerScrapeItem
                {
                    InfoHash = hashBytes,
                    Seeders = swarm.Peers.Values.Count(p => p.IsSeeder),
                    Downloaded = swarm.DownloadedCount,
                    Leechers = swarm.Peers.Values.Count(p => !p.IsSeeder),
                });
            }
        }

        return new TrackerScrapeResult
        {
            Success = true,
            Files = list,
        };
    }

    public byte[] ProcessScrape(List<byte[]> infoHashList)
    {
        var result = this.Scrape(infoHashList);
        if (!result.Success)
        {
            return FailureResponse(result.FailureReason);
        }

        var filesDict = new BEncodedDictionary();
        foreach (var file in result.Files)
        {
            filesDict[new BEncodedString(file.InfoHash)] = new BEncodedDictionary
            {
                { "complete", new BEncodedNumber(file.Seeders) },
                { "downloaded", new BEncodedNumber(file.Downloaded) },
                { "incomplete", new BEncodedNumber(file.Leechers) },
            };
        }

        var root = new BEncodedDictionary
        {
            { "files", filesDict },
        };

        return root.Encode();
    }

    public void PruneInactivePeers()
    {
        var interval = this.configService?.TrackerAnnounceInterval ?? 1800;
        this.PruneInactivePeers(TimeSpan.FromSeconds(interval * 2));
    }

    public void PruneInactivePeers(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var isPrivate = this.configService?.TrackerPrivateMode ?? false;

        foreach (var kvp in this.swarms)
        {
            var hexKey = kvp.Key;
            var swarm = kvp.Value;

            foreach (var peerKvp in swarm.Peers)
            {
                if (peerKvp.Value.LastAnnounceUtc < cutoff)
                {
                    swarm.Peers.TryRemove(peerKvp.Key, out _);
                }
            }

            if (swarm.Peers.IsEmpty)
            {
                if (swarm.IsRegistered)
                {
                    continue;
                }

                lock (swarm)
                {
                    if (swarm.Peers.IsEmpty)
                    {
                        ((ICollection<KeyValuePair<string, SwarmState>>)this.swarms).Remove(
                            new KeyValuePair<string, SwarmState>(hexKey, swarm));
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.cleanupTimer?.Dispose();
    }

    private void PruneStalePeers(SwarmState swarm, TimeSpan timeout, string hexKey)
    {
        var cutoff = DateTime.UtcNow - timeout;
        foreach (var kvp in swarm.Peers)
        {
            if (kvp.Value.LastAnnounceUtc < cutoff)
            {
                swarm.Peers.TryRemove(kvp.Key, out _);
            }
        }

        if (swarm.Peers.IsEmpty)
        {
            if (!swarm.IsRegistered)
            {
                lock (swarm)
                {
                    if (swarm.Peers.IsEmpty)
                    {
                        ((ICollection<KeyValuePair<string, SwarmState>>)this.swarms).Remove(
                            new KeyValuePair<string, SwarmState>(hexKey, swarm));
                    }
                }
            }
        }
    }

    private bool TryAcquireSwarmSlot()
    {
        if (this.swarms.Count < this.MaxSwarms)
        {
            return true;
        }

        this.PruneInactivePeers();

        if (this.swarms.Count < this.MaxSwarms)
        {
            return true;
        }

        var emptySwarms = this.swarms
            .Where(kvp => kvp.Value.Peers.IsEmpty && !kvp.Value.IsRegistered)
            .OrderBy(kvp => kvp.Value.LastActivityUtc)
            .ToList();

        foreach (var kvp in emptySwarms)
        {
            var hexKey = kvp.Key;
            var swarm = kvp.Value;

            lock (swarm)
            {
                if (swarm.Peers.IsEmpty && !swarm.IsRegistered)
                {
                    ((ICollection<KeyValuePair<string, SwarmState>>)this.swarms).Remove(
                        new KeyValuePair<string, SwarmState>(hexKey, swarm));
                }
            }

            if (this.swarms.Count < this.MaxSwarms)
            {
                return true;
            }
        }

        return this.swarms.Count < this.MaxSwarms;
    }

    private byte[] BuildAnnounceResponse(int seeders, int leechers, IReadOnlyCollection<TrackerPeerState> candidatePeers, TrackerAnnounceRequest request)
    {
        var interval = this.configService?.TrackerAnnounceInterval ?? 1800;

        var dict = new BEncodedDictionary
        {
            { "interval", new BEncodedNumber(interval) },
            { "min interval", new BEncodedNumber(Math.Min(300, interval / 2)) },
            { "complete", new BEncodedNumber(seeders) },
            { "incomplete", new BEncodedNumber(leechers) },
        };

        if (request.Compact)
        {
            using var ms4 = new MemoryStream();
            using var ms6 = new MemoryStream();
            Span<byte> portBytes = stackalloc byte[2];

            foreach (var p in candidatePeers)
            {
                if (p.Ip == null)
                {
                    continue;
                }

                var ip = p.Ip;
                if (ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();
                }

                BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)p.Port);

                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ipBytes = ip.GetAddressBytes();
                    ms4.Write(ipBytes);
                    ms4.Write(portBytes);
                }
                else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    var ipBytes = ip.GetAddressBytes();
                    ms6.Write(ipBytes);
                    ms6.Write(portBytes);
                }
            }

            dict["peers"] = new BEncodedString(ms4.ToArray());

            if (ms6.Length > 0)
            {
                dict["peers6"] = new BEncodedString(ms6.ToArray());
            }
        }
        else
        {
            var peerList = new BEncodedList();
            foreach (var p in candidatePeers)
            {
                if (p.Ip == null)
                {
                    continue;
                }

                var ip = p.Ip;
                if (ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();
                }

                var pDict = new BEncodedDictionary
                {
                    { "ip", new BEncodedString(ip.ToString()) },
                    { "port", new BEncodedNumber(p.Port) },
                };
                if (p.PeerId != null && p.PeerId.Length > 0)
                {
                    pDict["peer id"] = new BEncodedString(p.PeerId);
                }

                peerList.Add(pDict);
            }

            dict["peers"] = peerList;
        }

        return dict.Encode();
    }

    private static bool TryValidateInfoHash(
        byte[] infoHashBytes,
        string infoHashHex,
        out string normalizedHex,
        out byte[] normalizedBytes,
        out string failureReason)
    {
        normalizedHex = null;
        normalizedBytes = null;
        failureReason = null;

        var hasBytes = infoHashBytes != null;
        var hasHex = !string.IsNullOrWhiteSpace(infoHashHex);

        if (!hasBytes && !hasHex)
        {
            failureReason = "Missing info_hash parameter.";
            return false;
        }

        if (hasBytes)
        {
            if (infoHashBytes.Length != 20)
            {
                failureReason = "Invalid info_hash: must be exactly 20 bytes.";
                return false;
            }

            if (IsAllZeros(infoHashBytes))
            {
                failureReason = "Invalid info_hash: null hash (all zero bytes) is not allowed.";
                return false;
            }

            var derivedHex = ConvertBytesToHex(infoHashBytes);

            if (hasHex)
            {
                var trimmedHex = infoHashHex.Trim();
                if (trimmedHex.Length != 40 || !IsValidHex(trimmedHex))
                {
                    failureReason = "Invalid info_hash: malformed hex string.";
                    return false;
                }

                if (!string.Equals(derivedHex, trimmedHex, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = "Invalid info_hash: byte and hex representations do not match.";
                    return false;
                }
            }

            normalizedHex = derivedHex;
            normalizedBytes = (byte[])infoHashBytes.Clone();
            return true;
        }

        var cleanHex = infoHashHex.Trim();
        if (cleanHex.Length != 40)
        {
            failureReason = "Invalid info_hash: hex string must be exactly 40 characters.";
            return false;
        }

        if (!IsValidHex(cleanHex))
        {
            failureReason = "Invalid info_hash: malformed hex characters.";
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = ConvertHexToBytes(cleanHex);
        }
        catch
        {
            failureReason = "Invalid info_hash: failed to parse hex string.";
            return false;
        }

        if (bytes.Length != 20 || IsAllZeros(bytes))
        {
            failureReason = "Invalid info_hash: null hash (all zero bytes) is not allowed.";
            return false;
        }

        normalizedHex = cleanHex.ToUpperInvariant();
        normalizedBytes = bytes;
        return true;
    }

    private static bool TryValidateInfoHashHex(
        string infoHashHex,
        out string normalizedHex,
        out byte[] normalizedBytes,
        out string failureReason)
    {
        return TryValidateInfoHash(null, infoHashHex, out normalizedHex, out normalizedBytes, out failureReason);
    }

    private static bool IsAllZeros(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidHex(string hex)
    {
        for (var i = 0; i < hex.Length; i++)
        {
            var c = hex[i];
            var isHex = (c >= '0' && c <= '9') ||
                        (c >= 'a' && c <= 'f') ||
                        (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] FailureResponse(string reason)
    {
        var dict = new BEncodedDictionary
        {
            { "failure reason", new BEncodedString(reason) },
        };
        return dict.Encode();
    }

    private static string ConvertBytesToHex(byte[] bytes)
    {
        if (bytes == null)
        {
            return string.Empty;
        }

        return Convert.ToHexString(bytes).ToUpperInvariant();
    }

    private static byte[] ConvertHexToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Array.Empty<byte>();
        }

        return Convert.FromHexString(hex);
    }
}
