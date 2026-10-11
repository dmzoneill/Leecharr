using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using MonoTorrent.Client;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

internal static class MonoTorrentPeerExchangeScheduler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<int, DateTime> LastExchangeUtcByPeer = new();
    private static FieldInfo connectedPeersField;
    private static FieldInfo peerExchangeManagerField;
    private static FieldInfo lastPeerExchangeReviewField;
    private static MethodInfo peerExchangeOnTickMethod;
    private static bool reflectionInitialized;

    public static void Tick(ClientEngine engine, IConfigService config)
    {
        if (engine == null || config == null || !ProtocolExtensionPolicy.IsUtPexEnabled(config))
        {
            return;
        }

        try
        {
            EnsureReflectionInitialized();
            var interval = ProtocolExtensionPolicy.GetPeerExchangeInterval(config);
            var now = DateTime.UtcNow;

            foreach (var manager in engine.Torrents)
            {
                if (manager?.Settings?.AllowPeerExchange != true)
                {
                    continue;
                }

                var connectedPeers = connectedPeersField.GetValue(manager.Peers) as IList;
                if (connectedPeers == null || connectedPeers.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < connectedPeers.Count; i++)
                {
                    var peer = connectedPeers[i];
                    if (peer == null)
                    {
                        continue;
                    }

                    SuppressBuiltInPeerExchangeTick(peer);

                    var peerKey = peer.GetHashCode();
                    if (LastExchangeUtcByPeer.TryGetValue(peerKey, out var lastUtc) && now - lastUtc < interval)
                    {
                        continue;
                    }

                    var exchangeManager = peerExchangeManagerField.GetValue(peer);
                    if (exchangeManager != null)
                    {
                        peerExchangeOnTickMethod.Invoke(exchangeManager, null);
                        LastExchangeUtcByPeer[peerKey] = now;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "Peer exchange scheduler tick failed");
        }
    }

    private static void SuppressBuiltInPeerExchangeTick(object peer)
    {
        var stopwatch = lastPeerExchangeReviewField.GetValue(peer);
        var restart = stopwatch?.GetType().GetMethod("Restart", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        restart?.Invoke(stopwatch, null);
    }

    private static void EnsureReflectionInitialized()
    {
        if (reflectionInitialized)
        {
            return;
        }

        var clientAssembly = typeof(TorrentManager).Assembly;
        var peerManagerType = clientAssembly.GetType("MonoTorrent.Client.PeerManager", throwOnError: true);
        connectedPeersField = peerManagerType.GetField("ConnectedPeers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("PeerManager.ConnectedPeers field not found");

        var peerIdType = clientAssembly.GetType("MonoTorrent.Client.PeerId", throwOnError: true);
        peerExchangeManagerField = peerIdType.GetField("<PeerExchangeManager>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? peerIdType.GetField("PeerExchangeManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("PeerId.PeerExchangeManager field not found");
        lastPeerExchangeReviewField = peerIdType.GetField("LastPeerExchangeReview", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("PeerId.LastPeerExchangeReview field not found");

        var peerExchangeManagerType = clientAssembly.GetType("MonoTorrent.Client.PeerExchangeManager", throwOnError: true);
        peerExchangeOnTickMethod = peerExchangeManagerType.GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("PeerExchangeManager.OnTick method not found");

        reflectionInitialized = true;
    }
}
