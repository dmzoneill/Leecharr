// Copyright (c) FeedItOut. All rights reserved.

using System;
using NzbDrone.Core.BitTorrent;

namespace Leecharr.Api.V1.DownloadClients;

public static class UtWebUiAvailabilityHelper
{
    public const int UnitsPerFullCopy = 65536;

    /// <summary>
    /// uTorrent / Hadouken WebUI torrent rows store swarm availability at index 16 in units of 1/65536 of a full copy.
    /// </summary>
    public static int EncodeAvailabilityUnits(IDownloadTask task)
    {
        if (task == null)
        {
            return 0;
        }

        double? availability = task.SwarmAvailability
            ?? task.Metrics?.SwarmAvailability
            ?? task.GetResourceMetrics()?.SwarmAvailability;

        if (availability == null)
        {
            var pieces = task.PieceAvailability;
            if (pieces != null && pieces.Length > 0)
            {
                availability = TorrentResourceMetrics.ComputeAverageSwarmAvailability(pieces);
            }
        }

        if (availability == null || availability <= 0)
        {
            return 0;
        }

        return (int)Math.Round(availability.Value * UnitsPerFullCopy, MidpointRounding.AwayFromZero);
    }
}
