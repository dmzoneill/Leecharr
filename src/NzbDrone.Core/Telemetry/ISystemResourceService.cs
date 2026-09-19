// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;

namespace NzbDrone.Core.Telemetry;

public interface ISystemResourceService
{
    HostProcessResourceMetrics GetHostMetrics();

    TorrentEngineMetrics GetTorrentEngineMetrics();

    IReadOnlyList<TorrentResourceMetrics> GetPerTorrentMetrics();

    TorrentResourceMetrics GetTorrentMetrics(int torrentId);

    List<SubsystemTelemetryReport> GetSubsystemTelemetry();

    Task<List<SubsystemTelemetryReport>> GetSubsystemTelemetryAsync(string subsystemId = null, CancellationToken cancellationToken = default);

    SystemResourceTelemetrySnapshot GetFullTelemetrySnapshot();

    Task<SystemResourceTelemetrySnapshot> GetFullTelemetrySnapshotAsync(CancellationToken cancellationToken = default);
}
