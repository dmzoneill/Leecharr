// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MonoTorrent.BEncoding;
using NLog;
using NzbDrone.Core.Configuration;

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
}

public class EmbeddedTrackerService : IEmbeddedTrackerService
{
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<string, SwarmState> swarms = new(StringComparer.OrdinalIgnoreCase);

    public EmbeddedTrackerService(IConfigService configService = null)
    {
        this.configService = configService;
    }

    public bool IsEnabled => this.configService?.TrackerServerEnabled ?? true;

    public int ActiveSwarmsCount => this.swarms.Count;

    public int ActivePeersCount => this.swarms.Values.Sum(s => s.Peers.Count);

    public void RegisterSwarm(string infoHashHex)
    {
        if (string.IsNullOrWhiteSpace(infoHashHex))
        {
            return;
        }

        this.swarms.GetOrAdd(infoHashHex.ToUpperInvariant(), key => new SwarmState
        {
            InfoHash = ConvertHexToBytes(key),
        });
    }

    public void UnregisterSwarm(string infoHashHex)
    {
        if (!string.IsNullOrWhiteSpace(infoHashHex))
        {
            this.swarms.TryRemove(infoHashHex.ToUpperInvariant(), out _);
        }
    }

    public byte[] ProcessAnnounce(TrackerAnnounceRequest request)
    {
        if (!this.IsEnabled)
        {
            return FailureResponse("Embedded tracker is disabled.");
        }

        if (request == null || (request.InfoHashBytes == null && string.IsNullOrWhiteSpace(request.InfoHashHex)))
        {
            return FailureResponse("Missing info_hash parameter.");
        }

        var hexKey = !string.IsNullOrWhiteSpace(request.InfoHashHex)
            ? request.InfoHashHex.ToUpperInvariant()
            : ConvertBytesToHex(request.InfoHashBytes);

        var isPrivate = this.configService?.TrackerPrivateMode ?? false;
        if (isPrivate && !this.swarms.ContainsKey(hexKey))
        {
            return FailureResponse("Torrent not registered on this private tracker.");
        }

        var swarm = this.swarms.GetOrAdd(hexKey, key => new SwarmState
        {
            InfoHash = request.InfoHashBytes ?? ConvertHexToBytes(key),
        });

        var peerKey = $"{request.RemoteIp}:{request.Port}";
        var isStopped = string.Equals(request.Event, "stopped", StringComparison.OrdinalIgnoreCase);
        var isCompleted = string.Equals(request.Event, "completed", StringComparison.OrdinalIgnoreCase);

        if (isStopped)
        {
            swarm.Peers.TryRemove(peerKey, out _);
        }
        else
        {
            if (isCompleted)
            {
                swarm.DownloadedCount++;
            }

            var peer = swarm.Peers.GetOrAdd(peerKey, _ => new TrackerPeerState());
            peer.PeerId = request.PeerIdBytes;
            peer.Ip = request.RemoteIp;
            peer.Port = request.Port;
            peer.Uploaded = request.Uploaded;
            peer.Downloaded = request.Downloaded;
            peer.Left = request.Left;
            peer.LastAnnounceUtc = DateTime.UtcNow;
        }

        PruneStalePeers(swarm, TimeSpan.FromSeconds((this.configService?.TrackerAnnounceInterval ?? 1800) * 2));

        var seeders = swarm.Peers.Values.Count(p => p.IsSeeder);
        var leechers = swarm.Peers.Values.Count(p => !p.IsSeeder);
        var interval = this.configService?.TrackerAnnounceInterval ?? 1800;

        var dict = new BEncodedDictionary
        {
            { "interval", new BEncodedNumber(interval) },
            { "min interval", new BEncodedNumber(Math.Min(300, interval / 2)) },
            { "complete", new BEncodedNumber(seeders) },
            { "incomplete", new BEncodedNumber(leechers) },
        };

        var candidatePeers = swarm.Peers.Values
            .Where(p => !Equals(p.Ip, request.RemoteIp) || p.Port != request.Port)
            .Take(request.NumWant > 0 ? request.NumWant : (this.configService?.TrackerMaxPeersPerAnnounce ?? 50))
            .ToList();

        if (request.Compact)
        {
            using var ms = new MemoryStream();
            Span<byte> portBytes = stackalloc byte[2];
            foreach (var p in candidatePeers)
            {
                if (p.Ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ipBytes = p.Ip.GetAddressBytes();
                    BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)p.Port);
                    ms.Write(ipBytes);
                    ms.Write(portBytes);
                }
            }

            dict["peers"] = new BEncodedString(ms.ToArray());
        }
        else
        {
            var peerList = new BEncodedList();
            foreach (var p in candidatePeers)
            {
                var pDict = new BEncodedDictionary
                {
                    { "ip", new BEncodedString(p.Ip.ToString()) },
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

    public byte[] ProcessScrape(List<byte[]> infoHashList)
    {
        if (!this.IsEnabled)
        {
            return FailureResponse("Embedded tracker is disabled.");
        }

        var filesDict = new BEncodedDictionary();

        if (infoHashList != null && infoHashList.Count > 0)
        {
            foreach (var rawHash in infoHashList)
            {
                var hex = ConvertBytesToHex(rawHash);
                if (this.swarms.TryGetValue(hex, out var swarm))
                {
                    filesDict[new BEncodedString(rawHash)] = new BEncodedDictionary
                    {
                        { "complete", new BEncodedNumber(swarm.Peers.Values.Count(p => p.IsSeeder)) },
                        { "downloaded", new BEncodedNumber(swarm.DownloadedCount) },
                        { "incomplete", new BEncodedNumber(swarm.Peers.Values.Count(p => !p.IsSeeder)) },
                    };
                }
            }
        }
        else
        {
            foreach (var swarm in this.swarms.Values)
            {
                var hashBytes = swarm.InfoHash ?? ConvertHexToBytes(ConvertBytesToHex(swarm.InfoHash));
                filesDict[new BEncodedString(hashBytes)] = new BEncodedDictionary
                {
                    { "complete", new BEncodedNumber(swarm.Peers.Values.Count(p => p.IsSeeder)) },
                    { "downloaded", new BEncodedNumber(swarm.DownloadedCount) },
                    { "incomplete", new BEncodedNumber(swarm.Peers.Values.Count(p => !p.IsSeeder)) },
                };
            }
        }

        var root = new BEncodedDictionary
        {
            { "files", filesDict },
        };

        return root.Encode();
    }

    private static void PruneStalePeers(SwarmState swarm, TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        foreach (var kvp in swarm.Peers)
        {
            if (kvp.Value.LastAnnounceUtc < cutoff)
            {
                swarm.Peers.TryRemove(kvp.Key, out _);
            }
        }
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
