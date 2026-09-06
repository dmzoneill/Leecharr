// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.TrackerBoost;

public class TrackerBoostService : ITrackerBoostService
{
    private const int MaxLogEntries = 500;

    private static readonly HttpClient HttpClient = new(new HttpClientHandler { CheckCertificateRevocationList = true }) { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly BencodeParser BParser = new();
    private static readonly ConcurrentDictionary<string, (DateTime BoostedAt, HashSet<string> InjectedTrackers)> BoostHistory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<TrackerBoostLogEntry> LogBuffer = new();
    private static readonly TimeSpan ScrapeCacheTtl = TimeSpan.FromSeconds(60);

    private static readonly string[] DefaultBootstrapTrackers = new[]
    {
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.tracker.cl:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://explodie.org:6969/announce",
        "udp://tracker.openbittorrent.com:6969/announce",
        "udp://tracker.bittor.pw:1337/announce",
        "udp://tracker.dler.org:6969/announce",
        "udp://tracker.moeking.me:6969/announce",
        "udp://p4p.arenabg.com:1337/announce",
        "http://tracker.files.fm:6969/announce",
        "https://tracker.tamersunion.org:443/announce",
    };

    private static DateTime? lastScanTime;
    private static DateTime? lastHarvestTime;
    private static DateTime? lastProwlarrHarvestTime;
    private static DateTime? lastAutoBoostTime;
    private static int totalTorrentsBoosted;
    private static int totalTrackersInjected;
    private static int totalVerifiedMatchesCount;
    private static int nextLogId;

    private readonly ITrackerBoostTrackerRepository trackerRepository;
    private readonly ITorrentService torrentService;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IIndexerRepository indexerRepository;
    private readonly IConfigService configService;
    private readonly IDownloadEngine downloadEngine;
    private readonly IDownloadClientRepository downloadClientRepository;
    private readonly SemaphoreSlim globalScrapeThrottle = new(10, 10);
    private readonly ConcurrentDictionary<string, (bool Success, int Seeders, int Leechers, int Downloaded, DateTime CachedUtc)> scrapeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger logger;

    public TrackerBoostService(
        ITrackerBoostTrackerRepository trackerRepository,
        ITorrentService torrentService,
        ITrackerEntryRepository trackerEntryRepository,
        IIndexerRepository indexerRepository,
        IConfigService configService,
        IDownloadEngine downloadEngine = null,
        IDownloadClientRepository downloadClientRepository = null)
    {
        this.trackerRepository = trackerRepository;
        this.torrentService = torrentService;
        this.trackerEntryRepository = trackerEntryRepository;
        this.indexerRepository = indexerRepository;
        this.configService = configService;
        this.downloadEngine = downloadEngine;
        this.downloadClientRepository = downloadClientRepository;
        this.logger = LogManager.GetCurrentClassLogger();

        this.EnsureDefaultTrackersBootstrapped();
    }

    public static bool HasPasskey(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return HasPasskey(uri);
    }

    public static bool HasPasskey(Uri uri)
    {
        if (uri == null)
        {
            return false;
        }

        // Check user info (e.g. http://username:passkey@tracker.site/announce)
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return true;
        }

        // Check query parameters for authentication tokens / passkeys
        var query = uri.Query;
        if (!string.IsNullOrEmpty(query))
        {
            var lowerQuery = query.ToLowerInvariant();
            if (lowerQuery.Contains("passkey=") ||
                lowerQuery.Contains("authkey=") ||
                lowerQuery.Contains("torrentpass=") ||
                lowerQuery.Contains("auth=") ||
                lowerQuery.Contains("token=") ||
                lowerQuery.Contains("pass=") ||
                lowerQuery.Contains("key="))
            {
                return true;
            }

            var queryParams = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var param in queryParams)
            {
                var parts = param.Split('=', 2);
                var val = parts.Length > 1 ? parts[1] : parts[0];
                if (val.Length >= 16 && val.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    return true;
                }
            }
        }

        // Check path segments for authentication tokens / passkeys (Gazelle, UNIT3D, PTP, RED, etc.)
        var path = uri.AbsolutePath;
        if (!string.IsNullOrEmpty(path))
        {
            var lowerPath = path.ToLowerInvariant();
            if (lowerPath.Contains("/passkey") ||
                lowerPath.Contains("/authkey") ||
                lowerPath.Contains("/torrentpass"))
            {
                return true;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var segClean = Path.GetFileNameWithoutExtension(segment);
                if (string.IsNullOrEmpty(segClean))
                {
                    continue;
                }

                // Hex string >= 12 characters (e.g. Gazelle / UNIT3D 32-char hex passkey or 12+ hex hashes)
                if (segClean.Length >= 12 && IsHexString(segClean))
                {
                    return true;
                }

                // Alphanumeric / token >= 16 characters in path
                if (segClean.Length >= 16 && segClean.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsValidPublicTrackerUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var clean = url.Trim();
        if (clean.StartsWith("dht:", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("pex:", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("lsd:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
            {
                return false;
            }
        }
        else
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("[dht]", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("[pex]", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("[lsd]", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (HasPasskey(uri))
        {
            return false;
        }

        return true;
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    public TrackerBoostSettings GetSettings()
    {
        return new TrackerBoostSettings
        {
            AutoBoostEnabled = this.configService.GetValueBoolean("TrackerBoostAutoBoostEnabled", true),
            AutoHarvestEnabled = this.configService.GetValueBoolean("TrackerBoostAutoHarvestEnabled", true),
            IntervalMinutes = this.configService.GetValueInt("TrackerBoostIntervalMinutes", 2),
            MaxTrackersPerTorrent = this.configService.GetValueInt("TrackerBoostMaxTrackersPerTorrent", 8),
            OnlyVerified = this.configService.GetValueBoolean("TrackerBoostOnlyVerified", true),
        };
    }

    public void UpdateSettings(TrackerBoostSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        this.configService.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["TrackerBoostAutoBoostEnabled"] = settings.AutoBoostEnabled,
            ["TrackerBoostAutoHarvestEnabled"] = settings.AutoHarvestEnabled,
            ["TrackerBoostIntervalMinutes"] = Math.Max(1, settings.IntervalMinutes),
            ["TrackerBoostMaxTrackersPerTorrent"] = Math.Max(1, settings.MaxTrackersPerTorrent),
            ["TrackerBoostOnlyVerified"] = settings.OnlyVerified,
        });

        this.LogActivity("Info", "General", $"Tracker Boost settings updated: AutoBoost={settings.AutoBoostEnabled}, Interval={settings.IntervalMinutes}m, OnlyVerified={settings.OnlyVerified}");
    }

    public IReadOnlyList<TrackerBoostLogEntry> GetLogs(int limit = 100, string category = null, string level = null)
    {
        var query = LogBuffer.ToArray().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderByDescending(l => l.Id).Take(limit).ToList();
    }

    public void ClearLogs()
    {
        while (LogBuffer.TryDequeue(out _))
        {
        }

        this.LogActivity("Info", "General", "Tracker Boost activity logs cleared");
    }

    public void LogActivity(string level, string category, string message, string trackerUrl = null, string infoHash = null)
    {
        var entry = new TrackerBoostLogEntry
        {
            Id = Interlocked.Increment(ref nextLogId),
            Timestamp = DateTime.UtcNow,
            Level = level ?? "Info",
            Category = category ?? "General",
            Message = message ?? string.Empty,
            TrackerUrl = trackerUrl ?? string.Empty,
            InfoHash = infoHash ?? string.Empty,
        };

        LogBuffer.Enqueue(entry);
        while (LogBuffer.Count > MaxLogEntries && LogBuffer.TryDequeue(out _))
        {
        }
    }

    public List<TrackerBoostTracker> GetAllTrackers()
    {
        return this.trackerRepository.All().OrderByDescending(t => t.Status == TrackerHealthStatus.Alive)
            .ThenBy(t => t.LatencyMs > 0 ? t.LatencyMs : 9999)
            .ToList();
    }

    public TrackerBoostTracker GetTrackerById(int id)
    {
        return this.trackerRepository.Get(id);
    }

    public TrackerBoostTracker AddTracker(string url, TrackerSourceType source = TrackerSourceType.Manual, string sourceName = "Manual")
    {
        if (!IsValidPublicTrackerUrl(url))
        {
            throw new ArgumentException("Tracker URL is invalid or contains private passkey tokens.");
        }

        return this.AddTrackerInternal(url, source, sourceName);
    }

    public void DeleteTracker(int id)
    {
        this.trackerRepository.Delete(id);
    }

    public Task<TrackerBoostStatusSummary> GetStatusSummaryAsync()
    {
        var all = this.trackerRepository.All().ToList();
        var settings = this.GetSettings();
        return Task.FromResult(new TrackerBoostStatusSummary
        {
            TotalTrackersMonitored = all.Count,
            AliveTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Alive),
            SlowTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Slow),
            OfflineTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Offline),
            UntestedTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Untested),
            ProwlarrTrackersCount = all.Count(t => t.Source == TrackerSourceType.Prowlarr),
            PublicListTrackersCount = all.Count(t => t.Source == TrackerSourceType.PublicList),
            ActiveTorrentTrackersCount = all.Count(t => t.Source == TrackerSourceType.ActiveTorrent),
            TorrentsBoostedCount = totalTorrentsBoosted,
            ExtraTrackersInjectedCount = totalTrackersInjected,
            TotalVerifiedMatchesCount = totalVerifiedMatchesCount,
            AutoBoostEnabled = settings.AutoBoostEnabled,
            AutoHarvestEnabled = settings.AutoHarvestEnabled,
            LastScanTime = lastScanTime,
            LastHarvestTime = lastHarvestTime,
            LastProwlarrHarvestTime = lastProwlarrHarvestTime,
            LastAutoBoostTime = lastAutoBoostTime,
        });
    }

    public Task<int> HarvestFromActiveDownloadsAsync()
    {
        var discovered = 0;
        try
        {
            var torrentMap = new Dictionary<int, Torrent>();
            try
            {
                var torrents = this.torrentService.GetAll()?.ToList() ?? new List<Torrent>();
                foreach (var t in torrents)
                {
                    torrentMap[t.Id] = t;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to load torrent list for privacy check during harvesting");
            }

            var torrentEntries = this.trackerEntryRepository.All();
            foreach (var entry in torrentEntries)
            {
                // Verify whether parent torrent is private; skip harvesting any trackers from it
                if (entry.TorrentId > 0)
                {
                    if (!torrentMap.TryGetValue(entry.TorrentId, out var torrent))
                    {
                        torrent = this.torrentService.Get(entry.TorrentId);
                        if (torrent != null)
                        {
                            torrentMap[torrent.Id] = torrent;
                        }
                    }

                    if (torrent != null && torrent.IsPrivate)
                    {
                        continue;
                    }
                }

                if (IsValidPublicTrackerUrl(entry.Url))
                {
                    var res = this.AddTrackerInternal(entry.Url, TrackerSourceType.ActiveTorrent, "Leecharr Active Download");
                    if (res != null && res.Id > 0)
                    {
                        discovered++;
                    }
                }
            }

            lastHarvestTime = DateTime.UtcNow;
            if (discovered > 0)
            {
                this.logger.Info("Harvested {0} new public trackers from active download swarms", discovered);
                this.LogActivity("Success", "Discovery", $"Harvested {discovered} new public tracker(s) from active downloads");
            }
            else
            {
                this.LogActivity("Info", "Discovery", "Harvested active download swarms: all trackers up to date");
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error harvesting trackers from active downloads");
            this.LogActivity("Error", "Discovery", $"Error harvesting from active downloads: {ex.Message}");
        }

        return Task.FromResult(discovered);
    }

    public async Task<int> HarvestFromProwlarrAsync()
    {
        var harvestedCount = 0;
        try
        {
            var indexers = this.indexerRepository.All().ToList();
            var prowlarrIndexers = indexers.Where(i =>
                !string.IsNullOrWhiteSpace(i.Url) &&
                !string.IsNullOrWhiteSpace(i.ApiKey) &&
                ((i.Implementation != null && i.Implementation.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(i.Name) && i.Name.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)))).ToList();

            foreach (var prowlarr in prowlarrIndexers)
            {
                if (string.IsNullOrWhiteSpace(prowlarr.Url))
                {
                    continue;
                }

                var baseUrl = prowlarr.Url.TrimEnd('/');
                if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedUri))
                {
                    baseUrl = $"{parsedUri.Scheme}://{parsedUri.Authority}";
                }

                var requestUrl = $"{baseUrl}/api/v1/indexer";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (!string.IsNullOrWhiteSpace(prowlarr.ApiKey))
                {
                    request.Headers.Add("X-Api-Key", prowlarr.ApiKey);
                }

                using var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var indexerElem in doc.RootElement.EnumerateArray())
                {
                    var privacy = indexerElem.TryGetProperty("privacy", out var pProp) ? pProp.GetString() : "public";
                    if (string.Equals(privacy, "private", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var indexerName = indexerElem.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Prowlarr Indexer";

                    if (indexerElem.TryGetProperty("fields", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var field in fieldsProp.EnumerateArray())
                        {
                            if (field.TryGetProperty("name", out var nameProp) &&
                                string.Equals(nameProp.GetString(), "baseUrl", StringComparison.OrdinalIgnoreCase) &&
                                field.TryGetProperty("value", out var valProp) &&
                                valProp.ValueKind == JsonValueKind.String)
                            {
                                var u = valProp.GetString();
                                if (IsValidPublicTrackerUrl(u))
                                {
                                    this.AddTrackerInternal(u, TrackerSourceType.Prowlarr, $"Prowlarr ({indexerName})");
                                    harvestedCount++;
                                }
                            }
                        }
                    }

                    if (indexerElem.TryGetProperty("indexerUrls", out var urlsProp) && urlsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var urlItem in urlsProp.EnumerateArray())
                        {
                            var u = urlItem.GetString();
                            if (IsValidPublicTrackerUrl(u))
                            {
                                this.AddTrackerInternal(u, TrackerSourceType.Prowlarr, $"Prowlarr ({indexerName})");
                                harvestedCount++;
                            }
                        }
                    }
                }
            }

            lastProwlarrHarvestTime = DateTime.UtcNow;
            this.logger.Info("Harvested {0} trackers from connected Prowlarr indexers", harvestedCount);
            this.LogActivity(harvestedCount > 0 ? "Success" : "Info", "Discovery", $"Prowlarr sync complete: {harvestedCount} tracker(s) harvested from indexers");
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to harvest trackers from Prowlarr");
            this.LogActivity("Warn", "Discovery", $"Failed to harvest from Prowlarr: {ex.Message}");
        }

        return harvestedCount;
    }

    public async Task<int> HarvestFromCuratedListsAsync()
    {
        var count = 0;
        var feedUrls = new[]
        {
            "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best.txt",
            "https://raw.githubusercontent.com/XIU2/TrackersListCollection/master/best.txt",
        };

        foreach (var feed in feedUrls)
        {
            try
            {
                var content = await HttpClient.GetStringAsync(feed);
                using var reader = new StringReader(content);
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var clean = line.Trim();
                    if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith("#"))
                    {
                        continue;
                    }

                    if (IsValidPublicTrackerUrl(clean))
                    {
                        this.AddTrackerInternal(clean, TrackerSourceType.PublicList, "Curated Public Feed");
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to download tracker feed from {0}", feed);
                this.LogActivity("Warn", "Discovery", $"Failed to download tracker feed from {feed}: {ex.Message}");
            }
        }

        this.LogActivity(count > 0 ? "Success" : "Info", "Discovery", $"Curated list sync complete: {count} new candidate tracker(s) discovered");
        return count;
    }

    public async Task<int> ProbeTrackerHealthAsync()
    {
        var trackers = this.trackerRepository.All().Where(t => t.Enabled).ToList();
        var testedCount = 0;

        using var semaphore = new SemaphoreSlim(16);
        var tasks = trackers.Select(async tracker =>
        {
            await semaphore.WaitAsync();
            try
            {
                var sw = Stopwatch.StartNew();
                var isAlive = false;

                if (tracker.Protocol == TrackerProtocol.Udp)
                {
                    isAlive = await this.ProbeUdpTrackerAsync(tracker.Host, tracker.Port);
                }
                else
                {
                    isAlive = await this.ProbeHttpTrackerAsync(tracker.Url);
                }

                sw.Stop();
                tracker.LatencyMs = (int)sw.ElapsedMilliseconds;
                tracker.LastScraped = DateTime.UtcNow;

                if (isAlive)
                {
                    tracker.Status = tracker.LatencyMs < 400 ? TrackerHealthStatus.Alive : TrackerHealthStatus.Slow;
                    tracker.LastSuccess = DateTime.UtcNow;
                    tracker.SuccessfulScrapes++;
                    this.LogActivity(tracker.Status == TrackerHealthStatus.Alive ? "Success" : "Warn", "Health", $"Probe succeeded for {tracker.Url} ({tracker.LatencyMs}ms - {tracker.Status})", tracker.Url);
                }
                else
                {
                    tracker.Status = TrackerHealthStatus.Offline;
                    tracker.FailedScrapes++;
                    this.LogActivity("Error", "Health", $"Probe failed / connection timeout for {tracker.Url} - marked Offline", tracker.Url);
                }

                this.trackerRepository.Update(tracker);
                Interlocked.Increment(ref testedCount);
            }
            catch (Exception ex)
            {
                tracker.Status = TrackerHealthStatus.Offline;
                tracker.FailedScrapes++;
                this.trackerRepository.Update(tracker);
                this.LogActivity("Error", "Health", $"Probe exception for {tracker.Url}: {ex.Message} - marked Offline", tracker.Url);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        lastScanTime = DateTime.UtcNow;
        this.LogActivity("Info", "Health", $"Completed health scan of {testedCount} candidate tracker(s)");
        return testedCount;
    }

    public async Task<TorrentTrackerInspectionResult> InspectTorrentTrackersAsync(int torrentId)
    {
        var torrent = this.torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new TorrentTrackerInspectionResult { TorrentId = torrentId };
        }

        return await this.InspectHashInternalAsync(torrent.Id, torrent.Name, torrent.InfoHash, torrent.IsPrivate);
    }

    public async Task<TorrentTrackerInspectionResult> InspectHashTrackersAsync(string infoHash, string name = "")
    {
        var torrent = this.torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await this.InspectTorrentTrackersAsync(torrent.Id);
        }

        return await this.InspectHashInternalAsync(0, !string.IsNullOrWhiteSpace(name) ? name : infoHash, infoHash, false);
    }

    public async Task<SwarmBoostResult> BoostTorrentAsync(int torrentId, bool onlyVerified = true)
    {
        var torrent = this.torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new SwarmBoostResult { TorrentId = torrentId, Boosted = false, Message = "Torrent not found" };
        }

        if (torrent.IsPrivate)
        {
            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                IsPrivate = true,
                Boosted = false,
                Message = "Skipped: Private torrents are protected from external tracker injection.",
            };
        }

        var inspection = await this.InspectTorrentTrackersAsync(torrentId);
        var existingTrackers = this.trackerEntryRepository.GetByTorrentId(torrentId)
            .Select(t => (t.Url ?? string.Empty).Trim().ToLowerInvariant())
            .ToHashSet();

        var settings = this.GetSettings();
        var maxToAdd = settings.MaxTrackersPerTorrent;

        var candidateDetections = inspection.Detections
            .Where(d => IsValidPublicTrackerUrl(d.TrackerUrl))
            .Where(d => !existingTrackers.Contains(d.TrackerUrl.Trim().ToLowerInvariant()))
            .Where(d => !onlyVerified || d.IsVerified)
            .Take(maxToAdd)
            .ToList();

        var addedList = new List<string>();
        var totalSeeders = 0;
        var totalLeechers = 0;

        foreach (var candidate in candidateDetections)
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = candidate.TrackerUrl,
                Tier = 1,
                Status = 0,
                Enabled = true,
                Seeders = candidate.Seeders,
                Leechers = candidate.Leechers,
                AnnounceInterval = 1800,
            };
            this.trackerEntryRepository.Insert(entry);
            addedList.Add(candidate.TrackerUrl);
            totalSeeders += candidate.Seeders;
            totalLeechers += candidate.Leechers;

            var tr = this.trackerRepository.Get(candidate.TrackerId);
            if (tr != null)
            {
                tr.TotalSwarmsFound++;
                tr.TotalVerifiedTorrents++;
                this.trackerRepository.Update(tr);
            }
        }

        if (addedList.Count > 0)
        {
            totalTorrentsBoosted++;
            totalTrackersInjected += addedList.Count;
            totalVerifiedMatchesCount += addedList.Count;

            // In-Engine Injection: Add to MonoTorrent / active download engine
            if (this.downloadEngine != null)
            {
                try
                {
                    await this.downloadEngine.AddTrackersAsync(torrentId, addedList);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to inject trackers directly into download engine for torrent {0}", torrent.Id);
                }
            }

            // Also inject into any configured external download clients
            var clientCount = this.InjectIntoDownloadClients(torrent.InfoHash, addedList);

            var existingHistory = BoostHistory.GetOrAdd(torrent.InfoHash, _ => (DateTime.UtcNow, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            foreach (var url in addedList)
            {
                existingHistory.InjectedTrackers.Add(url);
            }

            BoostHistory[torrent.InfoHash] = (DateTime.UtcNow, existingHistory.InjectedTrackers);

            this.logger.Info(
                "Boosted torrent {0} with {1} verified trackers (+{2} seeds, +{3} leeches)",
                torrent.Name,
                addedList.Count,
                totalSeeders,
                totalLeechers);

            this.LogActivity(
                "Success",
                "Inject",
                $"Boosted torrent '{torrent.Name}': injected {addedList.Count} verified tracker(s) (+{totalSeeders} seeds, +{totalLeechers} leeches) into swarm",
                infoHash: torrent.InfoHash);

            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                IsPrivate = false,
                Boosted = true,
                AddedTrackersCount = addedList.Count,
                AddedTrackers = addedList,
                TotalSeedersFound = totalSeeders,
                TotalLeechersFound = totalLeechers,
                VerifiedCandidateTrackersCount = inspection.VerifiedTrackersCount,
                Message = $"Swarm boosted: injected {addedList.Count} verified tracker(s) (+{totalSeeders} seeds, +{totalLeechers} leeches).",
            };
        }

        return new SwarmBoostResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            IsPrivate = false,
            Boosted = false,
            AddedTrackersCount = 0,
            VerifiedCandidateTrackersCount = inspection.VerifiedTrackersCount,
            Message = inspection.VerifiedTrackersCount == 0
                ? "No verified additional seeders found on candidate public trackers."
                : "Torrent already has all verified candidate trackers attached.",
        };
    }

    public async Task<SwarmBoostResult> BoostHashAsync(string infoHash, string name = "", bool onlyVerified = true)
    {
        var torrent = this.torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await this.BoostTorrentAsync(torrent.Id, onlyVerified);
        }

        var inspection = await this.InspectHashTrackersAsync(infoHash, name);
        var settings = this.GetSettings();
        var maxToAdd = settings.MaxTrackersPerTorrent;

        var candidateDetections = inspection.Detections
            .Where(d => IsValidPublicTrackerUrl(d.TrackerUrl))
            .Where(d => !onlyVerified || d.IsVerified)
            .Take(maxToAdd)
            .ToList();

        var addedList = candidateDetections.Select(d => d.TrackerUrl).ToList();
        var injected = this.InjectIntoDownloadClients(infoHash, addedList);

        if (injected > 0)
        {
            var existingHistory = BoostHistory.GetOrAdd(infoHash, _ => (DateTime.UtcNow, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            foreach (var url in addedList)
            {
                existingHistory.InjectedTrackers.Add(url);
            }

            BoostHistory[infoHash] = (DateTime.UtcNow, existingHistory.InjectedTrackers);

            this.LogActivity(
                "Success",
                "Inject",
                $"Boosted hash '{infoHash}': injected {addedList.Count} verified tracker(s) into {injected} external download client(s)",
                infoHash: infoHash);
        }
        else
        {
            this.LogActivity(
                "Warn",
                "Inject",
                $"Boost hash '{infoHash}': no active download clients to inject trackers",
                infoHash: infoHash);
        }

        return new SwarmBoostResult
        {
            TorrentId = 0,
            TorrentName = !string.IsNullOrWhiteSpace(name) ? name : infoHash,
            InfoHash = infoHash,
            IsPrivate = false,
            Boosted = injected > 0,
            AddedTrackersCount = injected > 0 ? addedList.Count : 0,
            AddedTrackers = injected > 0 ? addedList : new List<string>(),
            Message = injected > 0
                ? $"Injected {addedList.Count} tracker(s) into {injected} active download client(s)."
                : "No active download clients found or injected for this hash.",
        };
    }

    public async Task<SwarmBoostResult> InjectTrackerToTorrentAsync(int torrentId, string trackerUrl, bool force = false)
    {
        var torrent = this.torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new SwarmBoostResult { TorrentId = torrentId, Boosted = false, Message = "Torrent not found" };
        }

        if (torrent.IsPrivate && !force)
        {
            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                IsPrivate = true,
                Boosted = false,
                Message = "Skipped: Private torrents are protected. Set force=true to override.",
            };
        }

        if (!IsValidPublicTrackerUrl(trackerUrl))
        {
            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                Boosted = false,
                Message = "Rejected: Tracker URL is invalid or contains private passkey tokens.",
            };
        }

        var existingTrackers = this.trackerEntryRepository.GetByTorrentId(torrentId)
            .Select(t => (t.Url ?? string.Empty).Trim().ToLowerInvariant())
            .ToHashSet();

        if (!existingTrackers.Contains(trackerUrl.Trim().ToLowerInvariant()))
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = trackerUrl.Trim(),
                Tier = 1,
                Status = 0,
                Enabled = true,
                AnnounceInterval = 1800,
            };
            this.trackerEntryRepository.Insert(entry);
            totalTrackersInjected++;
        }

        if (this.downloadEngine != null)
        {
            try
            {
                await this.downloadEngine.AddTrackersAsync(torrentId, new[] { trackerUrl.Trim() });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inject tracker into engine for torrent {0}", torrentId);
            }
        }

        this.InjectIntoDownloadClients(torrent.InfoHash, new[] { trackerUrl.Trim() });
        this.LogActivity("Success", "Inject", $"Injected tracker {trackerUrl} into torrent '{torrent.Name}'", trackerUrl, torrent.InfoHash);

        return new SwarmBoostResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            Boosted = true,
            AddedTrackersCount = 1,
            AddedTrackers = new List<string> { trackerUrl.Trim() },
            Message = $"Injected {trackerUrl} and announced to download engine.",
        };
    }

    public async Task<SwarmBoostResult> InjectTrackerToHashAsync(string infoHash, string trackerUrl, bool force = false)
    {
        var torrent = this.torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await this.InjectTrackerToTorrentAsync(torrent.Id, trackerUrl, force);
        }

        if (!IsValidPublicTrackerUrl(trackerUrl))
        {
            return new SwarmBoostResult
            {
                TorrentId = 0,
                TorrentName = infoHash,
                InfoHash = infoHash,
                Boosted = false,
                Message = "Rejected: Tracker URL is invalid or contains private passkey tokens.",
            };
        }

        var trackerTrimmed = trackerUrl.Trim();
        var injected = this.InjectIntoDownloadClients(infoHash, new[] { trackerTrimmed });
        if (injected > 0)
        {
            var existingHistory = BoostHistory.GetOrAdd(infoHash, _ => (DateTime.UtcNow, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            existingHistory.InjectedTrackers.Add(trackerTrimmed);
            BoostHistory[infoHash] = (DateTime.UtcNow, existingHistory.InjectedTrackers);

            this.LogActivity("Success", "Inject", $"Injected tracker {trackerTrimmed} into hash {infoHash} across {injected} client(s)", trackerTrimmed, infoHash);
        }
        else
        {
            this.LogActivity("Warn", "Inject", $"Failed to inject tracker {trackerTrimmed} into hash {infoHash}: no active download clients", trackerTrimmed, infoHash);
        }

        return new SwarmBoostResult
        {
            TorrentId = 0,
            TorrentName = infoHash,
            InfoHash = infoHash,
            Boosted = injected > 0,
            AddedTrackersCount = injected > 0 ? 1 : 0,
            AddedTrackers = injected > 0 ? new List<string> { trackerTrimmed } : new List<string>(),
            Message = injected > 0 ? $"Injected tracker into {injected} download client(s)." : "No active download clients found or injected for hash.",
        };
    }

    public async Task<List<SwarmBoostResult>> BoostAllTorrentsAsync(bool onlyVerified = true)
    {
        var results = new List<SwarmBoostResult>();
        var torrents = this.torrentService.GetAll().Where(t => !t.IsPrivate).ToList();

        foreach (var t in torrents)
        {
            var res = await this.BoostTorrentAsync(t.Id, onlyVerified);
            results.Add(res);
        }

        lastAutoBoostTime = DateTime.UtcNow;
        return results;
    }

    public void ClearScrapeCache()
    {
        this.scrapeCache.Clear();
    }

    public int ScrapeCacheCount => this.scrapeCache.Count;

    public async Task<TrackerCrossMatrixResult> GetCrossMatrixAsync()
    {
        var torrents = this.torrentService.GetAll().ToList();
        var allTrackers = this.trackerRepository.All().Where(t => t.Enabled).ToList();

        var torrentMatrix = new ConcurrentBag<TorrentMatrixItem>();
        var trackerTorrentsMap = new ConcurrentDictionary<int, ConcurrentBag<string>>();
        foreach (var tr in allTrackers)
        {
            trackerTorrentsMap[tr.Id] = new ConcurrentBag<string>();
        }

        using var matrixSemaphore = new SemaphoreSlim(10);
        var tasks = torrents.Select(async t =>
        {
            await matrixSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var inspection = await this.InspectTorrentTrackersAsync(t.Id).ConfigureAwait(false);
                var item = new TorrentMatrixItem
                {
                    TorrentId = t.Id,
                    TorrentName = t.Name,
                    InfoHash = t.InfoHash,
                    IsPrivate = t.IsPrivate,
                    IsBoosted = inspection.IsBoosted,
                    AttachedTrackersCount = inspection.AttachedTrackersCount,
                    VerifiedTrackersCount = inspection.VerifiedTrackersCount,
                    Trackers = inspection.Detections.Where(d => d.IsAttached || d.IsVerified).ToList(),
                };

                foreach (var d in item.Trackers)
                {
                    if (trackerTorrentsMap.TryGetValue(d.TrackerId, out var bag))
                    {
                        bag.Add(t.Name);
                    }
                }

                torrentMatrix.Add(item);
            }
            finally
            {
                matrixSemaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var orderedTorrentMatrix = torrentMatrix.OrderBy(t => t.TorrentId).ToList();

        var trackerMatrix = allTrackers.Select(tr => new TrackerMatrixItem
        {
            TrackerId = tr.Id,
            TrackerUrl = tr.Url,
            Host = tr.Host,
            Protocol = tr.Protocol,
            Status = tr.Status,
            LatencyMs = tr.LatencyMs,
            RegisteredTorrentsCount = trackerTorrentsMap.TryGetValue(tr.Id, out var b) ? b.Count : 0,
            RegisteredTorrentNames = trackerTorrentsMap.TryGetValue(tr.Id, out var b2) ? b2.ToList() : new List<string>(),
        }).OrderByDescending(tr => tr.RegisteredTorrentsCount)
            .ThenByDescending(tr => tr.Status == TrackerHealthStatus.Alive)
            .ToList();

        return new TrackerCrossMatrixResult
        {
            Torrents = orderedTorrentMatrix,
            Trackers = trackerMatrix,
        };
    }

    private void PruneScrapeCache()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in this.scrapeCache)
        {
            if (now - kvp.Value.CachedUtc > ScrapeCacheTtl)
            {
                this.scrapeCache.TryRemove(kvp.Key, out _);
            }
        }
    }

    public async Task<int> RecoverMissingTrackersAsync()
    {
        var recoveredCount = 0;
        try
        {
            var torrents = this.torrentService.GetAll().Where(t => !t.IsPrivate).ToList();
            foreach (var torrent in torrents)
            {
                var existingEntries = this.trackerEntryRepository.GetByTorrentId(torrent.Id).ToList();
                if (existingEntries.Count == 0)
                {
                    var boostRes = await this.BoostTorrentAsync(torrent.Id, onlyVerified: true);
                    if (boostRes.Boosted && boostRes.AddedTrackersCount > 0)
                    {
                        recoveredCount++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to run RecoverMissingTrackersAsync");
        }

        this.LogActivity("Info", "Discovery", $"Missing tracker recovery finished: {recoveredCount} torrent tracker swarm(s) recovered");
        return recoveredCount;
    }

    public int InjectIntoDownloadClients(string infoHash, IEnumerable<string> trackers)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || trackers == null || this.downloadClientRepository == null)
        {
            return 0;
        }

        var trackerList = trackers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
        if (trackerList.Count == 0)
        {
            return 0;
        }

        var clients = this.downloadClientRepository.GetEnabled()?.ToList();
        if (clients == null || clients.Count == 0)
        {
            return 0;
        }

        var successCount = 0;
        foreach (var client in clients)
        {
            try
            {
                if (this.InjectIntoClient(client, infoHash, trackerList))
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to inject trackers into download client {0}", client.Name);
            }
        }

        return successCount;
    }

    private bool InjectIntoClient(DownloadClientDefinition client, string infoHash, List<string> trackerList)
    {
        if (client == null || string.IsNullOrWhiteSpace(client.Host))
        {
            return false;
        }

        var port = client.Port > 0 ? client.Port : 8080;
        var scheme = client.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{client.Host}:{port}";

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            CheckCertificateRevocationList = true,
        };
        using var clientHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

        if (string.Equals(client.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(client.Password))
            {
                var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "username", client.Username ?? string.Empty },
                    { "password", client.Password ?? string.Empty },
                });

                var loginResp = clientHttp.PostAsync($"{baseUrl}/api/v2/auth/login", loginContent).GetAwaiter().GetResult();
                if (!loginResp.IsSuccessStatusCode)
                {
                    this.logger.Warn("qBittorrent login failed with status {0} for {1}", loginResp.StatusCode, baseUrl);
                    return false;
                }

                var loginResult = loginResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.Equals(loginResult.Trim(), "Fails.", StringComparison.OrdinalIgnoreCase))
                {
                    this.logger.Warn("qBittorrent authentication failed (Fails.) for {0}", baseUrl);
                    return false;
                }
            }

            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", infoHash.ToLowerInvariant() },
                { "urls", string.Join("\n", trackerList) },
            });

            var resp = clientHttp.PostAsync($"{baseUrl}/api/v2/torrents/addTrackers", formContent).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode)
            {
                this.logger.Info("Successfully injected {0} tracker(s) into qBittorrent ({1}) for hash {2}", trackerList.Count, client.Name, infoHash);
                return true;
            }

            this.logger.Warn("qBittorrent addTrackers failed with status {0} for {1}", resp.StatusCode, baseUrl);
            return false;
        }
        else if (string.Equals(client.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonSerializer.Serialize(new
            {
                method = "torrent-set",
                arguments = new
                {
                    ids = new[] { infoHash },
                    trackerAdd = trackerList,
                },
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(client.Password))
            {
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{client.Password}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
            }

            var resp = clientHttp.SendAsync(req).GetAwaiter().GetResult();
            if (resp.StatusCode == HttpStatusCode.Conflict && resp.Headers.TryGetValues("X-Transmission-Session-Id", out var sessValues))
            {
                var sessionId = sessValues.FirstOrDefault();
                using var req2 = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transmission/rpc")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrWhiteSpace(client.Username) || !string.IsNullOrWhiteSpace(client.Password))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.Username}:{client.Password}"));
                    req2.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                }

                req2.Headers.Add("X-Transmission-Session-Id", sessionId);
                resp = clientHttp.SendAsync(req2).GetAwaiter().GetResult();
            }

            if (resp.IsSuccessStatusCode)
            {
                this.logger.Info("Successfully injected {0} tracker(s) into Transmission ({1}) for hash {2}", trackerList.Count, client.Name, infoHash);
                return true;
            }

            this.logger.Warn("Transmission addTrackers failed with status {0} for {1}", resp.StatusCode, baseUrl);
            return false;
        }
        else if (string.Equals(client.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(client.Password))
            {
                var loginContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        method = "auth.login",
                        @params = new object[] { client.Password },
                        id = 1,
                    }),
                    Encoding.UTF8,
                    "application/json");

                var loginResp = clientHttp.PostAsync($"{baseUrl}/json", loginContent).GetAwaiter().GetResult();
                if (!loginResp.IsSuccessStatusCode)
                {
                    this.logger.Warn("Deluge login failed with status code {0} for {1}", loginResp.StatusCode, baseUrl);
                    return false;
                }

                var loginJson = loginResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var loginDoc = JsonDocument.Parse(loginJson);
                if (loginDoc.RootElement.TryGetProperty("result", out var resElem) &&
                    resElem.ValueKind == JsonValueKind.False)
                {
                    this.logger.Warn("Deluge authentication failed for {0}", baseUrl);
                    return false;
                }
            }

            var trackerObjects = trackerList.Select(u => new { tier = 0, url = u }).ToArray();
            var body = new StringContent(
                JsonSerializer.Serialize(new
                {
                    method = "core.add_torrent_trackers",
                    @params = new object[] { infoHash.ToLowerInvariant(), trackerObjects },
                    id = 2,
                }),
                Encoding.UTF8,
                "application/json");

            var resp = clientHttp.PostAsync($"{baseUrl}/json", body).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode)
            {
                var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var errElem) && errElem.ValueKind != JsonValueKind.Null)
                {
                    this.logger.Warn("Deluge returned error adding trackers: {0} for {1}", errElem.ToString(), baseUrl);
                    return false;
                }

                this.logger.Info("Successfully injected {0} tracker(s) into Deluge ({1}) for hash {2}", trackerList.Count, client.Name, infoHash);
                return true;
            }

            this.logger.Warn("Deluge addTrackers failed with status {0} for {1}", resp.StatusCode, baseUrl);
            return false;
        }

        return false;
    }

    public async Task RunOptimizationCycleAsync()
    {
        this.LogActivity("Info", "Cycle", "Background tracker optimization cycle started");

        await this.RecoverMissingTrackersAsync();

        var settings = this.GetSettings();
        if (settings.AutoHarvestEnabled)
        {
            await this.HarvestFromActiveDownloadsAsync();
        }

        var hasUntested = this.trackerRepository.All().Any(t => t.Enabled && t.Status == TrackerHealthStatus.Untested);
        if (hasUntested || lastScanTime == null || DateTime.UtcNow.Subtract(lastScanTime.Value).TotalMinutes > 5)
        {
            await this.ProbeTrackerHealthAsync();
        }

        if (settings.AutoBoostEnabled)
        {
            await this.BoostAllTorrentsAsync(onlyVerified: settings.OnlyVerified);
        }

        this.LogActivity("Info", "Cycle", "Background tracker optimization cycle completed successfully");
    }

    private void EnsureDefaultTrackersBootstrapped()
    {
        try
        {
            var existing = this.trackerRepository.All().ToList();
            if (existing.Count == 0)
            {
                foreach (var url in DefaultBootstrapTrackers)
                {
                    this.AddTrackerInternal(url, TrackerSourceType.PublicList, "Builtin Curated List");
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to bootstrap default tracker list");
        }
    }

    private TrackerBoostTracker AddTrackerInternal(string url, TrackerSourceType source, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Tracker URL cannot be empty");
        }

        var cleanUrl = url.Trim();
        if (!IsValidPublicTrackerUrl(cleanUrl))
        {
            this.logger.Warn("Refusing to add private or invalid tracker URL: {0}", cleanUrl);
            return null;
        }

        var existing = this.trackerRepository.FindByUrl(cleanUrl);
        if (existing != null)
        {
            return existing;
        }

        var protocol = TrackerProtocol.Udp;
        if (cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TrackerProtocol.Https;
        }
        else if (cleanUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TrackerProtocol.Http;
        }

        var host = cleanUrl;
        var port = protocol == TrackerProtocol.Https ? 443 : 80;

        try
        {
            if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri))
            {
                host = uri.Host;
                port = uri.Port > 0 ? uri.Port : (protocol == TrackerProtocol.Https ? 443 : 80);
            }
        }
        catch
        {
            // fallback
        }

        var tracker = new TrackerBoostTracker
        {
            Url = cleanUrl,
            Host = host,
            Port = port,
            Protocol = protocol,
            Status = TrackerHealthStatus.Untested,
            Source = source,
            SourceName = sourceName,
            LatencyMs = 0,
            Enabled = true,
        };

        return this.trackerRepository.Insert(tracker);
    }

    private async Task<bool> ProbeUdpTrackerAsync(string host, int port)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
            {
                return false;
            }

            var targetAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            using var client = new UdpClient(targetAddress.AddressFamily);
            client.Client.ReceiveTimeout = 2000;
            client.Client.SendTimeout = 2000;

            var transactionId = Random.Shared.Next();
            var packet = new byte[16];
            BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(0, 8), 0x41727101980L);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12, 4), transactionId);

            var endpoint = new IPEndPoint(targetAddress, port);
            await client.SendAsync(packet, packet.Length, endpoint);

            var receiveTask = client.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(2500));

            if (completedTask == receiveTask)
            {
                var result = await receiveTask;
                if (result.Buffer.Length >= 16)
                {
                    var action = BinaryPrimitives.ReadInt32BigEndian(result.Buffer.AsSpan(0, 4));
                    var respTxId = BinaryPrimitives.ReadInt32BigEndian(result.Buffer.AsSpan(4, 4));
                    if (action == 0 && respTxId == transactionId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ProbeHttpTrackerAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var resp = await HttpClient.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.BadRequest;
        }
        catch
        {
            return false;
        }
    }

    private async Task<TorrentTrackerInspectionResult> InspectHashInternalAsync(int torrentId, string torrentName, string infoHash, bool isPrivate)
    {
        var attachedMap = new Dictionary<string, TrackerEntry>(StringComparer.OrdinalIgnoreCase);
        if (torrentId > 0)
        {
            foreach (var entry in this.trackerEntryRepository.GetByTorrentId(torrentId))
            {
                var clean = (entry.Url ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(clean))
                {
                    attachedMap[clean] = entry;
                }
            }
        }

        var allKnownTrackers = this.trackerRepository.All().Where(t => t.Enabled).ToList();
        var detections = new List<TorrentTrackerDetection>();

        using var semaphore = new SemaphoreSlim(12);
        var tasks = allKnownTrackers.Select(async tracker =>
        {
            await semaphore.WaitAsync();
            try
            {
                var cleanUrl = (tracker.Url ?? string.Empty).Trim().ToLowerInvariant();
                var isAttached = attachedMap.TryGetValue(cleanUrl, out var entry);

                var detection = new TorrentTrackerDetection
                {
                    TrackerId = tracker.Id,
                    TrackerUrl = tracker.Url,
                    TrackerHost = tracker.Host,
                    Protocol = tracker.Protocol,
                    Source = tracker.Source,
                    SourceName = tracker.SourceName,
                    IsAttached = isAttached,
                    LatencyMs = tracker.LatencyMs,
                    HealthStatus = tracker.Status,
                    Seeders = entry?.Seeders ?? 0,
                    Leechers = entry?.Leechers ?? 0,
                };

                if (!string.IsNullOrWhiteSpace(infoHash) && !isPrivate)
                {
                    var scrape = await this.ScrapeTrackerForHashAsync(tracker, infoHash);
                    if (scrape.Success)
                    {
                        detection.Seeders = Math.Max(detection.Seeders, scrape.Seeders);
                        detection.Leechers = Math.Max(detection.Leechers, scrape.Leechers);
                        detection.Downloaded = scrape.Downloaded;
                        detection.IsVerified = scrape.Seeders > 0 || scrape.Leechers > 0 || scrape.Downloaded > 0;

                        if (tracker.Status == TrackerHealthStatus.Untested)
                        {
                            tracker.Status = TrackerHealthStatus.Alive;
                            tracker.LastSuccess = DateTime.UtcNow;
                            tracker.LastScraped = DateTime.UtcNow;
                            this.trackerRepository.Update(tracker);
                        }

                        if (detection.IsVerified)
                        {
                            detection.IsDetected = true;
                            detection.DetectionStatus = isAttached
                                ? $"Attached & Active ({detection.Seeders} seeds, {detection.Leechers} leeches)"
                                : $"Verified on Tracker ({detection.Seeders} seeds, {detection.Leechers} leeches)";
                        }
                        else
                        {
                            detection.IsDetected = false;
                            detection.DetectionStatus = isAttached ? "Attached (0 Peers Scraped)" : "Not Registered (0 Peers)";
                        }
                    }
                    else
                    {
                        detection.DetectionStatus = isAttached ? "Attached (Scrape Failed)" : (tracker.Status == TrackerHealthStatus.Offline ? "Offline" : "Unresponsive");
                    }
                }
                else
                {
                    detection.DetectionStatus = isPrivate ? "Protected (Private Torrent)" : (isAttached ? "Attached" : "Available");
                }

                lock (detections)
                {
                    detections.Add(detection);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        foreach (var entry in attachedMap.Values)
        {
            var cleanUrl = (entry.Url ?? string.Empty).Trim().ToLowerInvariant();
            if (!detections.Any(d => (d.TrackerUrl ?? string.Empty).Trim().ToLowerInvariant() == cleanUrl))
            {
                var host = !string.IsNullOrEmpty(entry.Url) && Uri.TryCreate(entry.Url, UriKind.Absolute, out var u) ? u.Host : entry.Url;
                detections.Add(new TorrentTrackerDetection
                {
                    TrackerId = 0,
                    TrackerUrl = entry.Url ?? string.Empty,
                    TrackerHost = host ?? string.Empty,
                    Protocol = (entry.Url != null && entry.Url.StartsWith("udp", StringComparison.OrdinalIgnoreCase)) ? TrackerProtocol.Udp : TrackerProtocol.Http,
                    Source = TrackerSourceType.ActiveTorrent,
                    SourceName = "Torrent Attached Tracker",
                    IsAttached = true,
                    HealthStatus = TrackerHealthStatus.Alive,
                    Seeders = entry.Seeders,
                    Leechers = entry.Leechers,
                    DetectionStatus = isPrivate ? "Protected (Private Tracker Attached)" : "Attached",
                });
            }
        }

        var hasBoost = BoostHistory.TryGetValue(infoHash, out var boostInfo);

        return new TorrentTrackerInspectionResult
        {
            TorrentId = torrentId,
            TorrentName = torrentName,
            InfoHash = infoHash,
            IsPrivate = isPrivate,
            IsBoosted = hasBoost,
            BoostedAt = hasBoost ? boostInfo.BoostedAt : null,
            InjectedTrackersCount = hasBoost ? boostInfo.InjectedTrackers.Count : 0,
            TotalTrackersChecked = detections.Count,
            AttachedTrackersCount = detections.Count(d => d.IsAttached),
            DetectedTrackersCount = detections.Count(d => d.IsDetected),
            VerifiedTrackersCount = detections.Count(d => d.IsVerified && !d.IsAttached),
            Detections = detections.OrderByDescending(d => d.IsAttached)
                .ThenByDescending(d => d.IsVerified)
                .ThenByDescending(d => d.Seeders + d.Leechers)
                .ThenBy(d => d.LatencyMs > 0 ? d.LatencyMs : 9999)
                .ToList(),
        };
    }

    private async Task<(bool Success, int Seeders, int Leechers, int Downloaded)> ScrapeTrackerForHashAsync(
        TrackerBoostTracker tracker,
        string infoHash,
        CancellationToken cancellationToken = default)
    {
        if (tracker == null || string.IsNullOrWhiteSpace(infoHash))
        {
            return (false, 0, 0, 0);
        }

        var cleanHash = infoHash.Trim();
        if (cleanHash.Length != 40)
        {
            return (false, 0, 0, 0);
        }

        var cacheKey = $"{tracker.Id}:{cleanHash.ToUpperInvariant()}";
        if (this.scrapeCache.TryGetValue(cacheKey, out var cached) &&
            (DateTime.UtcNow - cached.CachedUtc) < ScrapeCacheTtl)
        {
            return (cached.Success, cached.Seeders, cached.Leechers, cached.Downloaded);
        }

        // Bounded concurrency: max 10 concurrent outgoing scrape requests globally
        try
        {
            await this.globalScrapeThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return (false, 0, 0, 0);
        }

        try
        {
            // Strict timeout per scrape request (3.5 seconds max) to prevent hanging
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(3500));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            (bool Success, int Seeders, int Leechers, int Downloaded) result;
            try
            {
                if (tracker.Protocol == TrackerProtocol.Udp)
                {
                    result = await this.ScrapeUdpTrackerAsync(tracker.Host, tracker.Port, cleanHash, linkedCts.Token).ConfigureAwait(false);
                }
                else
                {
                    result = await this.ScrapeHttpTrackerAsync(tracker.Url, cleanHash, linkedCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                result = (false, 0, 0, 0);
            }
            catch
            {
                result = (false, 0, 0, 0);
            }

            this.scrapeCache[cacheKey] = (result.Success, result.Seeders, result.Leechers, result.Downloaded, DateTime.UtcNow);

            if (this.scrapeCache.Count > 10000)
            {
                this.PruneScrapeCache();
            }

            return result;
        }
        finally
        {
            this.globalScrapeThrottle.Release();
        }
    }

    private async Task<(bool Success, int Seeders, int Leechers, int Downloaded)> ScrapeUdpTrackerAsync(
        string host,
        int port,
        string hexHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return (false, 0, 0, 0);
            }

            var targetAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            using var client = new UdpClient(targetAddress.AddressFamily);
            var endpoint = new IPEndPoint(targetAddress, port);

            var connectTxId = Random.Shared.Next();
            var connectPacket = new byte[16];
            BinaryPrimitives.WriteInt64BigEndian(connectPacket.AsSpan(0, 8), 0x41727101980L);
            BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(connectPacket.AsSpan(12, 4), connectTxId);

            await client.SendAsync(connectPacket, connectPacket.Length, endpoint).ConfigureAwait(false);

            var connectResult = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (connectResult.Buffer.Length < 16)
            {
                return (false, 0, 0, 0);
            }

            var action = BinaryPrimitives.ReadInt32BigEndian(connectResult.Buffer.AsSpan(0, 4));
            var respTxId = BinaryPrimitives.ReadInt32BigEndian(connectResult.Buffer.AsSpan(4, 4));
            if (action != 0 || respTxId != connectTxId)
            {
                return (false, 0, 0, 0);
            }

            var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectResult.Buffer.AsSpan(8, 8));

            var scrapeTxId = Random.Shared.Next();
            var hashBytes = Convert.FromHexString(hexHash);
            var scrapePacket = new byte[36];
            BinaryPrimitives.WriteInt64BigEndian(scrapePacket.AsSpan(0, 8), connectionId);
            BinaryPrimitives.WriteInt32BigEndian(scrapePacket.AsSpan(8, 4), 2);
            BinaryPrimitives.WriteInt32BigEndian(scrapePacket.AsSpan(12, 4), scrapeTxId);
            Array.Copy(hashBytes, 0, scrapePacket, 16, 20);

            await client.SendAsync(scrapePacket, scrapePacket.Length, endpoint).ConfigureAwait(false);

            var scrapeResult = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (scrapeResult.Buffer.Length < 20)
            {
                return (false, 0, 0, 0);
            }

            var scrapeRespAction = BinaryPrimitives.ReadInt32BigEndian(scrapeResult.Buffer.AsSpan(0, 4));
            var scrapeRespTxId = BinaryPrimitives.ReadInt32BigEndian(scrapeResult.Buffer.AsSpan(4, 4));
            if (scrapeRespAction != 2 || scrapeRespTxId != scrapeTxId)
            {
                return (false, 0, 0, 0);
            }

            var seeders = BinaryPrimitives.ReadInt32BigEndian(scrapeResult.Buffer.AsSpan(8, 4));
            var completed = BinaryPrimitives.ReadInt32BigEndian(scrapeResult.Buffer.AsSpan(12, 4));
            var leechers = BinaryPrimitives.ReadInt32BigEndian(scrapeResult.Buffer.AsSpan(16, 4));

            return (true, Math.Max(0, seeders), Math.Max(0, leechers), Math.Max(0, completed));
        }
        catch (OperationCanceledException)
        {
            return (false, 0, 0, 0);
        }
        catch
        {
            return (false, 0, 0, 0);
        }
    }

    private async Task<(bool Success, int Seeders, int Leechers, int Downloaded)> ScrapeHttpTrackerAsync(
        string announceUrl,
        string hexHash,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!announceUrl.Contains("/announce"))
            {
                return (false, 0, 0, 0);
            }

            var hashBytes = Convert.FromHexString(hexHash);
            var encodedHash = string.Concat(hashBytes.Select(b => $"%{b:X2}"));
            var scrapeUrl = announceUrl.Replace("/announce", "/scrape");

            var separator = scrapeUrl.Contains('?') ? "&" : "?";
            var requestUrl = $"{scrapeUrl}{separator}info_hash={encodedHash}";

            using var resp = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return (false, 0, 0, 0);
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return (false, 0, 0, 0);
            }

            var bObject = BParser.Parse(bytes);
            if (bObject is BDictionary dict && dict.ContainsKey("files") && dict["files"] is BDictionary filesDict)
            {
                foreach (var entry in filesDict)
                {
                    var keyBytes = entry.Key?.Value.ToArray();
                    var keyHex = entry.Key?.ToString();
                    var isMatch = (keyBytes != null && keyBytes.SequenceEqual(hashBytes)) ||
                                  string.Equals(keyHex, hexHash, StringComparison.OrdinalIgnoreCase);

                    if (isMatch && entry.Value is BDictionary fileStats)
                    {
                        var complete = fileStats.ContainsKey("complete") && fileStats["complete"] is BNumber c ? (int)c.Value : 0;
                        var incomplete = fileStats.ContainsKey("incomplete") && fileStats["incomplete"] is BNumber ic ? (int)ic.Value : 0;
                        var downloaded = fileStats.ContainsKey("downloaded") && fileStats["downloaded"] is BNumber dl ? (int)dl.Value : 0;

                        return (true, complete, incomplete, downloaded);
                    }
                }
            }

            return (false, 0, 0, 0);
        }
        catch (OperationCanceledException)
        {
            return (false, 0, 0, 0);
        }
        catch
        {
            return (false, 0, 0, 0);
        }
    }
}
