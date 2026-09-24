// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent;

public interface ITorrentEngine : IDownloadEngine
{
    string EngineId { get; }

    string DisplayName { get; }

    string Version { get; }

    string ActiveVersion { get; }

    IReadOnlyList<string> SupportedVersions { get; }

    string Description { get; }

    bool IsAvailable { get; }

    TorrentEngineCapabilities Capabilities { get; }

    TorrentEngineCapabilities GetCapabilitiesForVersion(string version);

    Task<bool> SwitchVersionAsync(string targetVersion, CancellationToken cancellationToken = default);
}
