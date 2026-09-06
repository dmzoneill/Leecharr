// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent;

public interface ITorrentEngine : IDownloadEngine
{
    string EngineId { get; }

    string DisplayName { get; }

    string Version { get; }

    string Description { get; }

    bool IsAvailable { get; }

    TorrentEngineCapabilities Capabilities { get; }
}
