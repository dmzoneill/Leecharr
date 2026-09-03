// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.BitTorrent;

namespace NzbDrone.Core.Telemetry;

public interface ISystemResourceService
{
    HostProcessResourceMetrics GetHostMetrics();

    TorrentEngineMetrics GetTorrentEngineMetrics();

    IReadOnlyList<TorrentResourceMetrics> GetPerTorrentMetrics();

    TorrentResourceMetrics GetTorrentMetrics(int torrentId);

    List<SubsystemTelemetryReport> GetSubsystemTelemetry();

    SystemResourceTelemetrySnapshot GetFullTelemetrySnapshot();
}
