// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent.Tracker;

public class UdpTrackerService : IUdpTrackerService
{
    private const long MagicProtocolId = 0x41727101980L;

    private const int MaxConnectionIds = 10000;

    private readonly IEmbeddedTrackerService trackerService;
    private readonly IConfigService configService;
    private readonly ConcurrentDictionary<long, (IPAddress Ip, DateTime ExpiresUtc)> connectionIds = new();
    private readonly ConcurrentDictionary<IPAddress, (int Count, long WindowMinute)> clientRateLimits = new();
    private readonly object pruneLock = new();
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private DateTime lastPruneUtc = DateTime.UtcNow;

    private Socket socket;
    private CancellationTokenSource cts;
    private Task listenTask;
    private int disposed;

    public UdpTrackerService(IEmbeddedTrackerService trackerService, IConfigService configService = null)
    {
        this.trackerService = trackerService;
        this.configService = configService;
    }

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (this)
        {
            if (this.IsRunning)
            {
                return Task.CompletedTask;
            }

            var enabled = this.configService == null || (this.configService.TrackerServerEnabled && this.configService.TrackerUdpEnabled);
            if (!enabled)
            {
                return Task.CompletedTask;
            }

            var port = this.configService?.TrackerUdpPort ?? 6969;
            var bindAddressStr = this.configService?.TrackerBindAddress;
            var bindAddress = IPAddress.Any;

            if (!string.IsNullOrWhiteSpace(bindAddressStr) && bindAddressStr != "0.0.0.0" && bindAddressStr != "*")
            {
                if (IPAddress.TryParse(bindAddressStr, out var parsed))
                {
                    bindAddress = parsed;
                }
            }

            try
            {
                var endPoint = new IPEndPoint(bindAddress, port);
                this.socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

                if (bindAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    this.socket.DualMode = true;
                }

                this.socket.Bind(endPoint);
                this.Port = ((IPEndPoint)this.socket.LocalEndPoint).Port;

                this.cts = new CancellationTokenSource();
                this.listenTask = Task.Run(() => this.ListenLoopAsync(this.socket, this.cts.Token), CancellationToken.None);
                this.IsRunning = true;
                this.logger.Info("UDP Tracker server started on {0}:{1}", bindAddress, this.Port);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to start UDP Tracker server on {0}:{1}", bindAddress, port);
                this.CleanupSocket();
                throw;
            }

            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource tokenSource;
        Task task;

        lock (this)
        {
            if (!this.IsRunning)
            {
                return;
            }

            this.IsRunning = false;
            tokenSource = this.cts;
            task = this.listenTask;
            this.CleanupSocket();
        }

        if (tokenSource != null)
        {
            try
            {
                await tokenSource.CancelAsync();
            }
            catch
            {
            }
        }

        if (task != null)
        {
            try
            {
                await Task.WhenAny(task, Task.Delay(2000, cancellationToken));
            }
            catch
            {
            }
        }

        tokenSource?.Dispose();
        this.logger.Info("UDP Tracker server stopped");
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await this.StopAsync(cancellationToken);
        await this.StartAsync(cancellationToken);
    }

    public byte[] HandlePacket(ReadOnlySpan<byte> packet, IPEndPoint remoteEndPoint)
    {
        if (packet.Length < 16)
        {
            return null;
        }

        var clientIp = remoteEndPoint?.Address ?? IPAddress.Loopback;
        if (clientIp.IsIPv4MappedToIPv6)
        {
            clientIp = clientIp.MapToIPv4();
        }

        var first8 = BinaryPrimitives.ReadInt64BigEndian(packet.Slice(0, 8));
        var action = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(8, 4));
        var transactionId = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(12, 4));

        if (this.IsRateLimited(clientIp))
        {
            return BuildErrorResponse(transactionId, "Rate limit exceeded.");
        }

        // Connect
        if (action == 0)
        {
            if (first8 != MagicProtocolId)
            {
                return BuildErrorResponse(transactionId, "Invalid protocol_id.");
            }

            this.PruneConnectionIdsIfNeeded();

            var connectionId = GenerateConnectionId();
            this.connectionIds[connectionId] = (clientIp, DateTime.UtcNow.AddMinutes(2));

            var response = new byte[16];
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
            BinaryPrimitives.WriteInt64BigEndian(response.AsSpan(8, 8), connectionId);
            return response;
        }

        var connectionIdIn = first8;
        if (!this.IsValidConnectionId(connectionIdIn, clientIp))
        {
            // Silently drop packets with invalid or expired connection IDs to avoid UDP reflection/amplification attacks
            return null;
        }

        // Announce
        if (action == 1)
        {
            if (packet.Length < 98)
            {
                return BuildErrorResponse(transactionId, "Invalid announce packet size.");
            }

            var infoHashBytes = packet.Slice(16, 20).ToArray();
            var peerIdBytes = packet.Slice(36, 20).ToArray();
            var downloaded = BinaryPrimitives.ReadInt64BigEndian(packet.Slice(56, 8));
            var left = BinaryPrimitives.ReadInt64BigEndian(packet.Slice(64, 8));
            var uploaded = BinaryPrimitives.ReadInt64BigEndian(packet.Slice(72, 8));
            var eventCode = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(80, 4));
            var key = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(88, 4));
            var numWant = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(92, 4));
            var port = (int)BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(96, 2));

            var eventStr = eventCode switch
            {
                1 => "completed",
                2 => "started",
                3 => "stopped",
                _ => null,
            };

            var announceIp = clientIp;
            if (IPAddress.IsLoopback(clientIp))
            {
                var customIp = new IPAddress(packet.Slice(84, 4));
                if (!customIp.Equals(IPAddress.Any) && !customIp.Equals(IPAddress.None))
                {
                    announceIp = customIp;
                }
            }

            var req = new TrackerAnnounceRequest
            {
                InfoHashBytes = infoHashBytes,
                InfoHashHex = Convert.ToHexString(infoHashBytes),
                PeerIdBytes = peerIdBytes,
                PeerId = Convert.ToHexString(peerIdBytes),
                RemoteIp = announceIp,
                Port = port,
                Uploaded = uploaded,
                Downloaded = downloaded,
                Left = left,
                Event = eventStr,
                Compact = true,
                NumWant = numWant,
            };

            var result = this.trackerService.Announce(req);
            if (!result.Success)
            {
                return BuildErrorResponse(transactionId, result.FailureReason ?? "Announce failed.");
            }

            var isIpv6 = clientIp.AddressFamily == AddressFamily.InterNetworkV6
                         && !clientIp.IsIPv4MappedToIPv6;

            var peers = result.Peers ?? Array.Empty<TrackerPeerState>();

            var selectedPeers = isIpv6
                ? peers.Where(p => p.Ip != null
                                   && p.Ip.AddressFamily == AddressFamily.InterNetworkV6
                                   && !p.Ip.IsIPv4MappedToIPv6).ToList()
                : peers.Where(p => p.Ip != null
                                   && (p.Ip.AddressFamily == AddressFamily.InterNetwork
                                       || p.Ip.IsIPv4MappedToIPv6)).ToList();

            var recordSize = isIpv6 ? 18 : 6;
            var ipSize = isIpv6 ? 16 : 4;

            var response = new byte[20 + (selectedPeers.Count * recordSize)];
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(8, 4), result.Interval);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(12, 4), result.Leechers);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(16, 4), result.Seeders);

            var offset = 20;
            foreach (var peer in selectedPeers)
            {
                var ip = isIpv6
                    ? peer.Ip
                    : (peer.Ip.IsIPv4MappedToIPv6 ? peer.Ip.MapToIPv4() : peer.Ip);

                ip.GetAddressBytes().CopyTo(response.AsSpan(offset, ipSize));
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + ipSize, 2), (ushort)peer.Port);
                offset += recordSize;
            }

            return response;
        }
        else if (action == 2)
        {
            // Scrape
            if (packet.Length < 36 || ((packet.Length - 16) % 20 != 0))
            {
                return BuildErrorResponse(transactionId, "Invalid scrape packet size.");
            }

            var hashCount = (packet.Length - 16) / 20;
            if (hashCount > 74)
            {
                return BuildErrorResponse(transactionId, "Too many infohashes in scrape (max 74).");
            }

            var hashes = new List<byte[]>(hashCount);
            for (var i = 0; i < hashCount; i++)
            {
                hashes.Add(packet.Slice(16 + (i * 20), 20).ToArray());
            }

            var result = this.trackerService.Scrape(hashes);
            if (!result.Success)
            {
                return BuildErrorResponse(transactionId, result.FailureReason ?? "Scrape failed.");
            }

            var scrapeMap = new Dictionary<string, TrackerScrapeItem>(StringComparer.OrdinalIgnoreCase);
            if (result.Files != null)
            {
                foreach (var item in result.Files)
                {
                    if (item.InfoHash != null)
                    {
                        scrapeMap[Convert.ToHexString(item.InfoHash)] = item;
                    }
                }
            }

            var respSize = 8 + (hashCount * 12);
            var response = new byte[respSize];
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), 2);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);

            var offset = 8;
            for (var i = 0; i < hashCount; i++)
            {
                var hex = Convert.ToHexString(hashes[i]);
                if (scrapeMap.TryGetValue(hex, out var item))
                {
                    BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(offset, 4), item.Seeders);
                    BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(offset + 4, 4), (int)item.Downloaded);
                    BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(offset + 8, 4), item.Leechers);
                }
                else
                {
                    BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(offset, 4), 0);
                    BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(offset + 4, 4), 0);
                    BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(offset + 8, 4), 0);
                }

                offset += 12;
            }

            return response;
        }

        return BuildErrorResponse(transactionId, "Unknown action.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.StopAsync().GetAwaiter().GetResult();
        this.cts?.Dispose();
    }

    private async Task ListenLoopAsync(Socket listeningSocket, CancellationToken token)
    {
        var buffer = new byte[4096];
        EndPoint remoteEp = listeningSocket.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0)
            : new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await listeningSocket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, token);
                if (result.ReceivedBytes <= 0)
                {
                    continue;
                }

                var remote = (IPEndPoint)result.RemoteEndPoint;
                var response = this.HandlePacket(buffer.AsSpan(0, result.ReceivedBytes), remote);
                if (response != null && response.Length > 0)
                {
                    await listeningSocket.SendToAsync(response, SocketFlags.None, remote, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || ex.SocketErrorCode == SocketError.Interrupted)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error processing UDP tracker packet");
            }
        }
    }

    private void CleanupSocket()
    {
        try
        {
            this.socket?.Close();
            this.socket?.Dispose();
        }
        catch
        {
        }
        finally
        {
            this.socket = null;
        }
    }

    private static byte[] BuildErrorResponse(int transactionId, string message)
    {
        var msgBytes = Encoding.UTF8.GetBytes(message ?? "Unknown error");
        var response = new byte[8 + msgBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), 3);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
        msgBytes.CopyTo(response.AsSpan(8));
        return response;
    }

    private static long GenerateConnectionId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var id = BinaryPrimitives.ReadInt64BigEndian(bytes);
        return id == 0 ? 1 : id;
    }

    private bool IsValidConnectionId(long connectionId, IPAddress clientIp)
    {
        if (this.connectionIds.TryGetValue(connectionId, out var state))
        {
            if (state.ExpiresUtc >= DateTime.UtcNow && state.Ip != null && state.Ip.Equals(clientIp))
            {
                return true;
            }

            if (state.ExpiresUtc < DateTime.UtcNow)
            {
                this.connectionIds.TryRemove(connectionId, out _);
            }
        }

        return false;
    }

    private void PruneConnectionIdsIfNeeded()
    {
        var now = DateTime.UtcNow;
        if (now - this.lastPruneUtc < TimeSpan.FromSeconds(30) && this.connectionIds.Count < MaxConnectionIds)
        {
            return;
        }

        lock (this.pruneLock)
        {
            if (now - this.lastPruneUtc < TimeSpan.FromSeconds(30) && this.connectionIds.Count < MaxConnectionIds)
            {
                return;
            }

            this.lastPruneUtc = now;
            foreach (var kvp in this.connectionIds)
            {
                if (kvp.Value.ExpiresUtc < now)
                {
                    this.connectionIds.TryRemove(kvp.Key, out _);
                }
            }

            if (this.connectionIds.Count >= MaxConnectionIds)
            {
                var excess = this.connectionIds.Count - MaxConnectionIds + 1000;
                var toEvict = this.connectionIds.OrderBy(kvp => kvp.Value.ExpiresUtc).Take(excess);
                foreach (var item in toEvict)
                {
                    this.connectionIds.TryRemove(item.Key, out _);
                }
            }

            var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            foreach (var kvp in this.clientRateLimits)
            {
                if (kvp.Value.WindowMinute < currentMinute)
                {
                    this.clientRateLimits.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    private bool IsRateLimited(IPAddress clientIp)
    {
        var limit = this.configService?.TrackerRateLimitPerMinute ?? 0;
        if (limit <= 0)
        {
            return false;
        }

        var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var entry = this.clientRateLimits.AddOrUpdate(
            clientIp,
            _ => (1, currentMinute),
            (_, existing) => existing.WindowMinute == currentMinute ? (existing.Count + 1, currentMinute) : (1, currentMinute));

        return entry.Count > limit;
    }
}
