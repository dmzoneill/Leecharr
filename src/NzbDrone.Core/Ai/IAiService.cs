// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Ai;

public interface IAiService
{
    Task<AiParsedRelease> ParseReleaseAsync(string releaseName);

    AiParsedRelease ParseRelease(string releaseName);

    Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers);

    AiDiagnosticReport DiagnoseTorrentHealth(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers);

    Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery);

    AiSearchParameters ProcessNaturalLanguageSearch(string naturalQuery);

    Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files);

    AiMalwareRiskAssessment AnalyzeMalwareRisk(string torrentName, IReadOnlyList<TorrentFile> files);

    Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null);

    string GenerateChatResponse(string userMessage, string systemContext = null);
}
