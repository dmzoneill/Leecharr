// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using NzbDrone.Core.BitTorrent;

namespace NzbDrone.Core.Telemetry;

public class SystemResourceTelemetrySnapshot
{
    public HostProcessResourceMetrics Host { get; set; } = new();

    public TorrentEngineMetrics TorrentEngine { get; set; } = new();

    public List<TorrentResourceMetrics> PerTorrent { get; set; } = new();

    public List<SubsystemTelemetryReport> Subsystems { get; set; } = new();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
