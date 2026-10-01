// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public static class TorrentEngineNetworkHelper
{
    public static string ResolveBoundIpv4Address(
        bool isHaltedByKillSwitch,
        IVpnKillSwitchService vpnKillSwitchService,
        IConfigService configService,
        INetworkBindingService networkBindingService)
    {
        if (isHaltedByKillSwitch || vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return "127.0.0.1";
        }

        var iface = !string.IsNullOrWhiteSpace(configService?.NetworkInterfaceBinding)
            ? configService.NetworkInterfaceBinding
            : configService?.BindInterface;

        var hasSpecificInterface = !string.IsNullOrWhiteSpace(iface) &&
            !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase);

        if (!hasSpecificInterface)
        {
            return "0.0.0.0";
        }

        if (IPAddress.TryParse(iface, out var parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            return parsedIp.ToString();
        }

        if (vpnKillSwitchService != null)
        {
            if (vpnKillSwitchService.IsFailClosedActive)
            {
                return "127.0.0.1";
            }

            var vpnIp = vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork);
            if (vpnIp != null)
            {
                return vpnIp.ToString();
            }
        }

        if (networkBindingService != null)
        {
            if (networkBindingService.CheckVpnKillSwitch(iface))
            {
                return "127.0.0.1";
            }

            if (!networkBindingService.IsInterfaceUp(iface))
            {
                return "127.0.0.1";
            }
        }

        var resolved = ManagedSocketBindingProvider.GetInterfaceIp(iface, AddressFamily.InterNetwork);
        if (resolved != null)
        {
            return resolved.ToString();
        }

        // When a specific interface binding is active but cannot be resolved or is down, fail closed.
        return "127.0.0.1";
    }

    public static void HaltActiveTorrents<TTask>(
        IEnumerable<TTask> tasks,
        HashSet<int> haltedIds,
        Func<TTask, int> getTorrentId,
        Func<TTask, TorrentStatus> getStatus,
        Action<TTask, TorrentStatus> setStatus)
    {
        lock (haltedIds)
        {
            haltedIds.Clear();
            var activeTasks = tasks.Where(t =>
            {
                var status = getStatus(t);
                return status == TorrentStatus.Downloading || status == TorrentStatus.Seeding;
            });

            foreach (var task in activeTasks)
            {
                haltedIds.Add(getTorrentId(task));
                setStatus(task, TorrentStatus.Paused);
            }
        }
    }

    public static void ResumeHaltedTorrents<TTask>(
        ConcurrentDictionary<int, TTask> tasks,
        HashSet<int> haltedIds,
        Func<TTask, TorrentStatus> getStatus,
        Action<TTask, TorrentStatus> setStatus,
        Func<TTask, double> getProgress,
        Action<int> resumeTorrentAction)
    {
        lock (haltedIds)
        {
            var pausedIds = haltedIds
                .Where(id => tasks.TryGetValue(id, out var task) && getStatus(task) == TorrentStatus.Paused)
                .ToList();

            foreach (var torrentId in pausedIds)
            {
                if (tasks.TryGetValue(torrentId, out var task))
                {
                    setStatus(task, getProgress(task) >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading);
                    resumeTorrentAction(torrentId);
                }
            }

            haltedIds.Clear();
        }
    }
}
