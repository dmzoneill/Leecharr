// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Leecharr.Http.REST;
using NzbDrone.Core.BitTorrent;

namespace Leecharr.Api.V1.BitTorrent;

public class TorrentEngineResource : RestResource
{
    public string EngineId { get; set; }

    public string DisplayName { get; set; }

    public string Version { get; set; }

    public bool IsActive { get; set; }

    public bool IsAvailable { get; set; }

    public string Status { get; set; }

    public string Description { get; set; }

    public TorrentEngineCapabilities Capabilities { get; set; }

    public List<string> Warnings { get; set; } = new();
}

public class ActiveEngineStatusResource : RestResource
{
    public string EngineId { get; set; }

    public string DisplayName { get; set; }

    public string Version { get; set; }

    public int ActiveTorrentsCount { get; set; }

    public int ConnectedPeersCount { get; set; }

    public long DownloadSpeedBytes { get; set; }

    public long UploadSpeedBytes { get; set; }

    public string ProtocolName { get; set; }
}

public class SwitchEngineRequest
{
    public string EngineId { get; set; }

    public bool PreserveTransfers { get; set; } = true;
}

public class SwitchEngineResultResource
{
    public bool Success { get; set; }

    public string PreviousEngine { get; set; }

    public string ActiveEngine { get; set; }

    public int TorrentsMigrated { get; set; }

    public string Message { get; set; }

    public string Error { get; set; }
}

public class EngineProbeResultResource
{
    public string EngineId { get; set; }

    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; }

    public List<string> DependencyChecks { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

public class EngineProbeRequest
{
    public string EngineId { get; set; }
}
