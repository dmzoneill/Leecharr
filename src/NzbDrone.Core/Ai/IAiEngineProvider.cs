// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Ai;

public interface IAiEngineProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Version { get; }

    string Description { get; }

    bool IsAvailable { get; }

    AiCapabilities Capabilities { get; }

    Task<AiHealthResult> ProbeHealthAsync();

    Task<AiParsedRelease> ParseReleaseAsync(string releaseName);

    Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers);

    Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery);

    Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files);

    Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null);
}
