using System;
using System.Collections.Generic;
using System.Reflection;
using MonoTorrent.Client;
using MonoTorrent.Messages.Peer.Libtorrent;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

internal static class MonoTorrentProtocolExtensionConfigurator
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object ApplyLock = new();
    private static FieldInfo maxPeersField;
    private static FieldInfo bufferSizeField;
    private static FieldInfo supportedMessagesField;
    private static bool reflectionInitialized;

    public static void Apply(IConfigService config)
    {
        if (config == null)
        {
            return;
        }

        lock (ApplyLock)
        {
            try
            {
                EnsureReflectionInitialized();
                SyncExtensionSupport("ut_metadata", ProtocolExtensionPolicy.IsUtMetadataEnabled(config));
                SyncExtensionSupport("ut_pex", ProtocolExtensionPolicy.IsUtPexEnabled(config));
                ApplyMaxPeersPerMessage(ProtocolExtensionPolicy.GetPexMaxPeersPerMessage(config));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to apply MonoTorrent protocol extension settings");
            }
        }
    }

    private static void EnsureReflectionInitialized()
    {
        if (reflectionInitialized)
        {
            return;
        }

        var messagesAssembly = typeof(ExtendedHandshakeMessage).Assembly;
        var extensionMessageType = messagesAssembly.GetType("MonoTorrent.Messages.Peer.Libtorrent.ExtensionMessage", throwOnError: true);
        supportedMessagesField = extensionMessageType.GetField("SupportedMessages", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("ExtensionMessage.SupportedMessages field not found");

        var clientAssembly = typeof(TorrentManager).Assembly;
        var peerExchangeManagerType = clientAssembly.GetType("MonoTorrent.Client.PeerExchangeManager", throwOnError: true);
        maxPeersField = peerExchangeManagerType.GetField("MAX_PEERS", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PeerExchangeManager.MAX_PEERS field not found");
        bufferSizeField = peerExchangeManagerType.GetField("BufferSize", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PeerExchangeManager.BufferSize field not found");

        reflectionInitialized = true;
    }

    private static void SyncExtensionSupport(string extensionName, bool enabled)
    {
        var supported = (List<ExtensionSupport>)supportedMessagesField.GetValue(null);
        if (supported == null)
        {
            return;
        }

        var existingIndex = supported.FindIndex(s => string.Equals(s.Name, extensionName, StringComparison.Ordinal));
        if (enabled)
        {
            if (existingIndex >= 0)
            {
                return;
            }

            if (string.Equals(extensionName, PeerExchangeMessage.Support.Name, StringComparison.Ordinal))
            {
                supported.Add(PeerExchangeMessage.Support);
            }
            else if (string.Equals(extensionName, LTMetadata.Support.Name, StringComparison.Ordinal))
            {
                supported.Add(LTMetadata.Support);
            }
        }
        else if (existingIndex >= 0)
        {
            supported.RemoveAt(existingIndex);
        }
    }

    private static void ApplyMaxPeersPerMessage(int configuredMaxPeers)
    {
        var bufferSize = (int)bufferSizeField.GetValue(null);
        var hardCap = bufferSize / 7;
        var effective = Math.Min(hardCap, configuredMaxPeers);
        maxPeersField.SetValue(null, Math.Max(1, effective));
    }
}
