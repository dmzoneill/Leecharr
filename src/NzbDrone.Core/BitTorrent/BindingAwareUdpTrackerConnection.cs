// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Connections.Tracker;
using MonoTorrent.Messages;
using MonoTorrent.Messages.UdpTracker;
using MonoTorrent.Trackers;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using ReusableTasks;

namespace NzbDrone.Core.BitTorrent;

/// <summary>
/// UDP tracker transport that binds datagram sockets through the active network binding provider.
/// Proxy-tunneled swarms fail closed because SOCKS TCP cannot carry UDP tracker announces.
/// </summary>
public sealed class BindingAwareUdpTrackerConnection : ITrackerConnection
{
    private readonly Func<BoundSocketConnector> createConnector;
    private readonly INetworkBindingService networkBindingService;
    private readonly IConfigService configService;

    public BindingAwareUdpTrackerConnection(
        Uri announceUri,
        AddressFamily addressFamily,
        Func<BoundSocketConnector> createConnector,
        INetworkBindingService networkBindingService = null,
        IConfigService configService = null)
    {
        this.Uri = announceUri ?? throw new ArgumentNullException(nameof(announceUri));
        this.AddressFamily = addressFamily;
        this.createConnector = createConnector ?? throw new ArgumentNullException(nameof(createConnector));
        this.networkBindingService = networkBindingService;
        this.configService = configService;
    }

    public bool CanScrape => true;

    public Uri Uri { get; }

    public AddressFamily AddressFamily { get; }

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxRetries { get; set; } = 3;

    public async ReusableTask<AnnounceResponse> AnnounceAsync(AnnounceRequest parameters, CancellationToken token)
    {
        try
        {
            var connectionId = await this.ConnectAsync(token).ConfigureAwait(false);
            var reported = parameters.GetReportedAddress(this.AddressFamily == AddressFamily.InterNetwork ? "ipv4" : "ipv6");
            var peers = new Dictionary<InfoHash, IList<global::MonoTorrent.PeerInfo>>();
            TimeSpan? interval = null;

            foreach (var infoHash in EnumerateInfoHashes(parameters.InfoHashes))
            {
                var announce = new AnnounceMessage(Random.Shared.Next(), connectionId, parameters, infoHash, reported.port);
                var (message, error) = await this.SendAndReceiveAsync(announce, token).ConfigureAwait(false);
                if (error != null)
                {
                    return new AnnounceResponse(TrackerState.InvalidResponse, failureMessage: error);
                }

                if (message is not AnnounceResponseMessage announceResponse)
                {
                    return new AnnounceResponse(TrackerState.InvalidResponse, failureMessage: "UDP tracker announce returned an unexpected message.");
                }

                peers[infoHash] = announceResponse.Peers;
                interval ??= announceResponse.Interval;
            }

            if (peers.Count == 0)
            {
                return new AnnounceResponse(TrackerState.InvalidResponse, failureMessage: "UDP tracker announce had no infohash.");
            }

            return new AnnounceResponse(TrackerState.Ok, peers, updateInterval: interval);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return new AnnounceResponse(TrackerState.Offline, failureMessage: "UDP tracker traffic is blocked while a proxy tunnel is active.");
        }
        catch (Exception ex)
        {
            return new AnnounceResponse(TrackerState.Offline, failureMessage: ex.Message);
        }
    }

    public async ReusableTask<ScrapeResponse> ScrapeAsync(ScrapeRequest parameters, CancellationToken token)
    {
        try
        {
            var connectionId = await this.ConnectAsync(token).ConfigureAwait(false);
            var infoHashes = new List<InfoHash>(2);
            if (parameters.InfoHashes.V1 != null)
            {
                infoHashes.Add(parameters.InfoHashes.V1);
            }

            if (parameters.InfoHashes.V2 != null)
            {
                infoHashes.Add(parameters.InfoHashes.V2);
            }

            var scrape = new ScrapeMessage(Random.Shared.Next(), connectionId, infoHashes);
            var (message, error) = await this.SendAndReceiveAsync(scrape, token).ConfigureAwait(false);
            if (error != null)
            {
                return new ScrapeResponse(TrackerState.InvalidResponse, failureMessage: error);
            }

            if (message is not ScrapeResponseMessage scrapeResponse)
            {
                return new ScrapeResponse(TrackerState.InvalidResponse, failureMessage: "UDP tracker scrape returned an unexpected message.");
            }

            var scrapes = new Dictionary<InfoHash, ScrapeInfo>();
            for (var i = 0; i < infoHashes.Count && i < scrapeResponse.Scrapes.Count; i++)
            {
                var details = scrapeResponse.Scrapes[i];
                scrapes[infoHashes[i]] = new ScrapeInfo(details.Seeds, details.Complete, details.Leeches);
            }

            return new ScrapeResponse(TrackerState.Ok, scrapes);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return new ScrapeResponse(TrackerState.Offline, failureMessage: "UDP tracker traffic is blocked while a proxy tunnel is active.");
        }
        catch (Exception ex)
        {
            return new ScrapeResponse(TrackerState.Offline, failureMessage: ex.Message);
        }
    }

    internal void BindTrackerSocket(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (this.IsProxyBlockingDatagrams())
        {
            throw new SocketException((int)SocketError.AccessDenied);
        }

        this.createConnector().BindSocket(socket, 0);
    }

    private async Task<long> ConnectAsync(CancellationToken token)
    {
        var connect = new ConnectMessage();
        var (message, error) = await this.SendAndReceiveAsync(connect, token).ConfigureAwait(false);
        if (error != null)
        {
            throw new TrackerException(error);
        }

        if (message is ConnectResponseMessage connectResponse)
        {
            return connectResponse.ConnectionId;
        }

        throw new InvalidOperationException("UDP tracker connect returned an unexpected message.");
    }

    private async Task<(UdpTrackerMessage Message, string Error)> SendAndReceiveAsync(UdpTrackerMessage message, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        var budget = TimeSpan.FromMilliseconds(Math.Max(1, this.RetryDelay.TotalMilliseconds * Math.Max(1, this.MaxRetries)));
        timeout.CancelAfter(budget);

        using var udp = this.CreateBoundUdpClient();
        udp.Connect(this.Uri.Host, this.Uri.Port);

        var buffer = new byte[message.ByteLength];
        message.Encode(buffer);

        while (!timeout.IsCancellationRequested)
        {
            await udp.SendAsync(buffer, timeout.Token).ConfigureAwait(false);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            attempt.CancelAfter(this.RetryDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : this.RetryDelay);
            try
            {
                var result = await udp.ReceiveAsync(attempt.Token).ConfigureAwait(false);
                var decoded = UdpTrackerMessage.DecodeMessage(result.Buffer, MessageType.Response, result.RemoteEndPoint.AddressFamily);
                if (decoded.TransactionId != message.TransactionId)
                {
                    continue;
                }

                if (decoded is ErrorMessage errorMessage)
                {
                    return (null, errorMessage.Error ?? "The tracker returned an error");
                }

                return (decoded, null);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !timeout.IsCancellationRequested)
            {
            }
        }

        timeout.Token.ThrowIfCancellationRequested();
        throw new TimeoutException("The UDP tracker did not respond.");
    }

    private UdpClient CreateBoundUdpClient()
    {
        var socket = new Socket(this.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            this.BindTrackerSocket(socket);
            var client = new UdpClient();
            var replaced = client.Client;
            client.Client = socket;
            replaced.Dispose();
            return client;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private bool IsProxyBlockingDatagrams()
    {
        if (this.networkBindingService?.ActiveProvider is IProxyTunnelBindingProvider)
        {
            return true;
        }

        var proxyType = this.configService?.ProxyType?.ToLowerInvariant();
        return proxyType is "socks5" or "socks4" or "socks4a" or "http"
            && !string.IsNullOrWhiteSpace(this.configService?.ProxyHost);
    }

    private static IEnumerable<InfoHash> EnumerateInfoHashes(InfoHashes infoHashes)
    {
        if (infoHashes.V1 != null)
        {
            yield return infoHashes.V1;
        }

        if (infoHashes.V2 != null)
        {
            yield return infoHashes.V2;
        }
    }
}
