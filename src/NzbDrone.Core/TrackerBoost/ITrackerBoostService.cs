// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.TrackerBoost;

public interface ITrackerBoostService
{
    List<TrackerBoostTracker> GetAllTrackers();

    TrackerBoostTracker GetTrackerById(int id);

    TrackerBoostTracker AddTracker(string url, TrackerSourceType source = TrackerSourceType.Manual, string sourceName = "Manual");

    void DeleteTracker(int id);

    Task<TrackerBoostStatusSummary> GetStatusSummaryAsync();

    TrackerBoostSettings GetSettings();

    void UpdateSettings(TrackerBoostSettings settings);

    Task<int> HarvestFromActiveDownloadsAsync();

    Task<int> HarvestFromProwlarrAsync();

    Task<int> HarvestFromCuratedListsAsync();

    Task<int> ProbeTrackerHealthAsync();

    Task<TorrentTrackerInspectionResult> InspectTorrentTrackersAsync(int torrentId);

    Task<TorrentTrackerInspectionResult> InspectHashTrackersAsync(string infoHash, string name = "");

    Task<SwarmBoostResult> BoostTorrentAsync(int torrentId, bool onlyVerified = true);

    Task<SwarmBoostResult> BoostHashAsync(string infoHash, string name = "", bool onlyVerified = true);

    Task<SwarmBoostResult> InjectTrackerToTorrentAsync(int torrentId, string trackerUrl, bool force = false);

    Task<SwarmBoostResult> InjectTrackerToHashAsync(string infoHash, string trackerUrl, bool force = false);

    Task<List<SwarmBoostResult>> BoostAllTorrentsAsync(bool onlyVerified = true);

    Task<TrackerCrossMatrixResult> GetCrossMatrixAsync();

    Task<int> RecoverMissingTrackersAsync();

    int InjectIntoDownloadClients(string infoHash, IEnumerable<string> trackers);

    IReadOnlyList<TrackerBoostLogEntry> GetLogs(int limit = 100, string category = null, string level = null);

    void ClearLogs();

    void LogActivity(string level, string category, string message, string trackerUrl = null, string infoHash = null);

    Task RunOptimizationCycleAsync();

    void ClearScrapeCache();

    int ScrapeCacheCount { get; }
}
