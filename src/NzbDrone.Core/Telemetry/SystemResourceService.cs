// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;

namespace NzbDrone.Core.Telemetry;

public class SystemResourceService : ISystemResourceService
{
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();
    private static readonly object CpuLock = new();
    private static readonly object DriveLock = new();
    private static readonly object SubsystemLock = new();
    private static readonly TimeSpan DriveCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SubsystemCacheDuration = TimeSpan.FromSeconds(20);
    private static readonly Dictionary<string, (SubsystemTelemetryReport Report, DateTime CachedAt)> SubsystemTelemetryCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] KnownSubsystems =
    [
        "bittorrent",
        "extractor",
        "mediainspector",
        "geoip",
        "blocklist",
        "networkbinding",
        "mediametadata",
        "httptransport",
        "ai",
    ];

    private static long lastSampleTimestamp = Stopwatch.GetTimestamp();
    private static TimeSpan lastTotalProcessorTime = CurrentProcess.TotalProcessorTime;
    private static double cachedCpuPercent;
    private static DateTime lastDriveSampleTime = DateTime.MinValue;
    private static List<DiskMountPointMetrics> cachedDriveMetrics = new();

    private readonly ISubsystemManagerRegistry subsystemManagers;
    private readonly ITorrentEngineManager torrentEngineManager;
    private readonly IArchiveExtractorManager extractorManager;
    private readonly IMediaInspectorManager mediaInspectorManager;
    private readonly IGeoIpManager geoIpManager;
    private readonly IBlocklistManager blocklistManager;
    private readonly INetworkBindingManager networkBindingManager;
    private readonly IMediaMetadataManager mediaMetadataManager;
    private readonly IHttpTransportManager httpTransportManager;
    private readonly IAiManager aiManager;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;

    public SystemResourceService(
        ISubsystemManagerRegistry subsystemManagers,
        IConfigService configService,
        IAppFolderInfo appFolderInfo = null)
    {
        this.subsystemManagers = subsystemManagers;
        this.torrentEngineManager = subsystemManagers?.TorrentEngineManager;
        this.extractorManager = subsystemManagers?.ExtractorManager;
        this.mediaInspectorManager = subsystemManagers?.MediaInspectorManager;
        this.geoIpManager = geoIpManager ?? subsystemManagers?.GeoIpManager;
        this.blocklistManager = subsystemManagers?.BlocklistManager;
        this.networkBindingManager = subsystemManagers?.NetworkBindingManager;
        this.mediaMetadataManager = subsystemManagers?.MediaMetadataManager;
        this.httpTransportManager = subsystemManagers?.HttpTransportManager;
        this.aiManager = subsystemManagers?.AiManager;
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public SystemResourceService(
        ITorrentEngineManager torrentEngineManager,
        IArchiveExtractorManager extractorManager,
        IMediaInspectorManager mediaInspectorManager,
        IGeoIpManager geoIpManager,
        IBlocklistManager blocklistManager,
        INetworkBindingManager networkBindingManager,
        IMediaMetadataManager mediaMetadataManager,
        IHttpTransportManager httpTransportManager,
        IAiManager aiManager,
        IConfigService configService,
        IAppFolderInfo appFolderInfo = null)
        : this(
            new SubsystemManagerRegistry(
                torrentEngineManager,
                extractorManager,
                mediaInspectorManager,
                geoIpManager,
                blocklistManager,
                networkBindingManager,
                mediaMetadataManager,
                httpTransportManager,
                aiManager),
            configService,
            appFolderInfo)
    {
    }

    internal static Func<List<DiskMountPointMetrics>> DriveMetricsProvider { get; set; } = QuerySystemDrives;

    public static void ResetCpuMetricsCache()
    {
        lock (CpuLock)
        {
            lastSampleTimestamp = Stopwatch.GetTimestamp();
            try
            {
                lastTotalProcessorTime = CurrentProcess.TotalProcessorTime;
            }
            catch (Exception)
            {
                // Fallback to zero processor time if process telemetry is inaccessible
            }

            cachedCpuPercent = 0.0;
        }
    }

    public static void ResetDriveMetricsCache()
    {
        lock (DriveLock)
        {
            lastDriveSampleTime = DateTime.MinValue;
            cachedDriveMetrics = new List<DiskMountPointMetrics>();
        }
    }

    public static void ResetSubsystemMetricsCache()
    {
        lock (SubsystemLock)
        {
            SubsystemTelemetryCache.Clear();
        }
    }

    public static void ClearSubsystemCache()
    {
        ResetSubsystemMetricsCache();
    }

    public HostProcessResourceMetrics GetHostMetrics()
    {
        var now = DateTime.UtcNow;
        double cpuPercent;

        try
        {
            CurrentProcess.Refresh();
        }
        catch (Exception)
        {
            // Process inspection might be restricted or exited
        }

        lock (CpuLock)
        {
            var currentTimestamp = Stopwatch.GetTimestamp();
            var elapsedMs = Stopwatch.GetElapsedTime(lastSampleTimestamp, currentTimestamp).TotalMilliseconds;

            if (elapsedMs < 0)
            {
                lastSampleTimestamp = currentTimestamp;
                try
                {
                    lastTotalProcessorTime = CurrentProcess.TotalProcessorTime;
                }
                catch (Exception)
                {
                    // Fallback if process timing query is disallowed
                }
            }
            else if (elapsedMs >= 350)
            {
                try
                {
                    var totalTime = CurrentProcess.TotalProcessorTime;
                    var cpuUsedMs = (totalTime - lastTotalProcessorTime).TotalMilliseconds;
                    lastSampleTimestamp = currentTimestamp;
                    lastTotalProcessorTime = totalTime;
                    var cores = Math.Max(1, Environment.ProcessorCount);
                    if (cpuUsedMs >= 0)
                    {
                        cachedCpuPercent = Math.Clamp((cpuUsedMs / (elapsedMs * cores)) * 100.0, 0.0, 100.0);
                    }
                }
                catch (Exception)
                {
                    // Fallback to cached CPU percent if CPU sample query fails
                }
            }

            cpuPercent = Math.Round(cachedCpuPercent, 1);
        }

        ThreadPool.GetAvailableThreads(out var availWorker, out var availCompletion);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxCompletion);

        var drives = GetDiskMetrics(now);

        long uptimeSec = 0;
        try
        {
            uptimeSec = (long)(DateTime.UtcNow - CurrentProcess.StartTime.ToUniversalTime()).TotalSeconds;
        }
        catch (Exception)
        {
            // StartTime may throw PlatformNotSupportedException or Win32Exception on restricted environments
        }

        return new HostProcessResourceMetrics
        {
            CpuProcessPercent = cpuPercent,
            CpuCores = Environment.ProcessorCount,
            WorkingSetBytes = CurrentProcess.WorkingSet64,
            PrivateMemoryBytes = CurrentProcess.PrivateMemorySize64,
            VirtualMemoryBytes = CurrentProcess.VirtualMemorySize64,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            GcGen0Collections = GC.CollectionCount(0),
            GcGen1Collections = GC.CollectionCount(1),
            GcGen2Collections = GC.CollectionCount(2),
            ThreadCount = CurrentProcess.Threads.Count,
            ThreadPoolWorkerThreads = Math.Max(0, maxWorker - availWorker),
            ThreadPoolCompletionPortThreads = Math.Max(0, maxCompletion - availCompletion),
            HandleCount = CurrentProcess.HandleCount,
            UptimeSeconds = uptimeSec,
            DiskDrives = drives,
            Timestamp = now,
        };
    }

    public TorrentEngineMetrics GetTorrentEngineMetrics()
    {
        try
        {
            return this.torrentEngineManager.ActiveEngine?.GetEngineMetrics() ?? new TorrentEngineMetrics();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to retrieve engine metrics");
            return new TorrentEngineMetrics();
        }
    }

    public IReadOnlyList<TorrentResourceMetrics> GetPerTorrentMetrics()
    {
        try
        {
            return this.torrentEngineManager.ActiveEngine?.GetAllTorrentResourceMetrics()
                   ?? Array.Empty<TorrentResourceMetrics>();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to retrieve per-torrent metrics");
            return Array.Empty<TorrentResourceMetrics>();
        }
    }

    public TorrentResourceMetrics GetTorrentMetrics(int torrentId)
    {
        try
        {
            return this.torrentEngineManager.ActiveEngine?.GetTorrentResourceMetrics(torrentId);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to retrieve metrics for torrent {0}", torrentId);
            return null;
        }
    }

    public async Task<List<SubsystemTelemetryReport>> GetSubsystemTelemetryAsync(string subsystemId = null, CancellationToken cancellationToken = default)
    {
        var targetId = NormalizeSubsystemId(subsystemId);
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            if (!KnownSubsystems.Contains(targetId, StringComparer.OrdinalIgnoreCase))
            {
                return new List<SubsystemTelemetryReport>();
            }

            lock (SubsystemLock)
            {
                if (SubsystemTelemetryCache.TryGetValue(targetId, out var cached) &&
                    (now - cached.CachedAt) < SubsystemCacheDuration &&
                    (now - cached.CachedAt) >= TimeSpan.Zero)
                {
                    return new List<SubsystemTelemetryReport> { cached.Report };
                }
            }

            var report = await this.ProbeSingleSubsystemAsync(targetId, cancellationToken);
            lock (SubsystemLock)
            {
                SubsystemTelemetryCache[targetId] = (report, DateTime.UtcNow);
            }

            return new List<SubsystemTelemetryReport> { report };
        }

        var toProbe = new List<string>();
        var results = new Dictionary<string, SubsystemTelemetryReport>(StringComparer.OrdinalIgnoreCase);

        lock (SubsystemLock)
        {
            foreach (var id in KnownSubsystems)
            {
                if (SubsystemTelemetryCache.TryGetValue(id, out var cached) &&
                    (now - cached.CachedAt) < SubsystemCacheDuration &&
                    (now - cached.CachedAt) >= TimeSpan.Zero)
                {
                    results[id] = cached.Report;
                }
                else
                {
                    toProbe.Add(id);
                }
            }
        }

        if (toProbe.Count > 0)
        {
            var tasks = toProbe.Select(id => this.ProbeSingleSubsystemAsync(id, cancellationToken)).ToList();
            var probedReports = await Task.WhenAll(tasks);

            lock (SubsystemLock)
            {
                for (var i = 0; i < toProbe.Count; i++)
                {
                    var id = toProbe[i];
                    var report = probedReports[i];
                    SubsystemTelemetryCache[id] = (report, DateTime.UtcNow);
                    results[id] = report;
                }
            }
        }

        return KnownSubsystems
            .Where(id => results.ContainsKey(id))
            .Select(id => results[id])
            .ToList();
    }

    public List<SubsystemTelemetryReport> GetSubsystemTelemetry()
    {
        return this.GetSubsystemTelemetryAsync().GetAwaiter().GetResult();
    }

    public async Task<SystemResourceTelemetrySnapshot> GetFullTelemetrySnapshotAsync(CancellationToken cancellationToken = default)
    {
        return new SystemResourceTelemetrySnapshot
        {
            Host = this.GetHostMetrics(),
            TorrentEngine = this.GetTorrentEngineMetrics(),
            PerTorrent = this.GetPerTorrentMetrics().ToList(),
            Subsystems = await this.GetSubsystemTelemetryAsync(null, cancellationToken),
            Timestamp = DateTime.UtcNow,
        };
    }

    public SystemResourceTelemetrySnapshot GetFullTelemetrySnapshot()
    {
        return this.GetFullTelemetrySnapshotAsync().GetAwaiter().GetResult();
    }

    private static string NormalizeSubsystemId(string subsystemId)
    {
        if (string.IsNullOrWhiteSpace(subsystemId))
        {
            return null;
        }

        var normalized = subsystemId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "bittorrent" or "torrentengine" => "bittorrent",
            "extractor" or "archiveextractor" => "extractor",
            "mediainspector" or "inspector" => "mediainspector",
            "geoip" => "geoip",
            "blocklist" => "blocklist",
            "networkbinding" or "binding" => "networkbinding",
            "mediametadata" or "metadata" => "mediametadata",
            "httptransport" or "transport" => "httptransport",
            "ai" or "intelligence" => "ai",
            _ => normalized,
        };
    }

    private async Task<SubsystemTelemetryReport> ProbeSingleSubsystemAsync(string subsystemId, CancellationToken cancellationToken)
    {
        return subsystemId switch
        {
            "bittorrent" => this.GetBitTorrentTelemetry(),
            "extractor" => await this.GetExtractorTelemetryAsync(cancellationToken),
            "mediainspector" => await this.GetMediaInspectorTelemetryAsync(cancellationToken),
            "geoip" => await this.GetGeoIpTelemetryAsync(cancellationToken),
            "blocklist" => await this.GetBlocklistTelemetryAsync(cancellationToken),
            "networkbinding" => await this.GetNetworkBindingTelemetryAsync(cancellationToken),
            "mediametadata" => await this.GetMediaMetadataTelemetryAsync(cancellationToken),
            "httptransport" => await this.GetHttpTransportTelemetryAsync(cancellationToken),
            "ai" => await this.GetAiTelemetryAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(subsystemId), $"Unknown subsystem ID '{subsystemId}'"),
        };
    }

    private SubsystemTelemetryReport GetBitTorrentTelemetry()
    {
        var engineMetrics = this.GetTorrentEngineMetrics();
        var engine = this.torrentEngineManager?.ActiveEngine;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "bittorrent",
            SubsystemName = "BitTorrent Engine",
            ActiveProvider = this.torrentEngineManager?.ActiveEngineId ?? "Unknown",
            Status = engineMetrics.IsRunning ? "Healthy" : (engine?.IsAvailable == true ? "Healthy" : "Stopped"),
            ResourceLoad = engineMetrics.ActiveTorrents > 20 ? "High" : (engineMetrics.ActiveTorrents > 0 ? "Nominal" : "Low"),
            Metrics = new Dictionary<string, object>
            {
                ["activeTorrents"] = engineMetrics.ActiveTorrents,
                ["downloadSpeed"] = engineMetrics.TotalDownloadSpeed,
                ["uploadSpeed"] = engineMetrics.TotalUploadSpeed,
                ["openConnections"] = engineMetrics.OpenConnections,
                ["dhtNodes"] = engineMetrics.DhtNodeCount,
                ["diskCacheHitRatio"] = engineMetrics.DiskCacheHitRatio,
                ["diskPendingWrites"] = engineMetrics.DiskPendingWrites,
                ["hashFails"] = engineMetrics.HashFailsTotal,
                ["protocolOverheadPercentage"] = engineMetrics.ProtocolOverheadPercentage,
                ["encryptedConnections"] = engineMetrics.EncryptedConnectionsCount,
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetExtractorTelemetryAsync(CancellationToken cancellationToken)
    {
        var extractor = this.extractorManager?.ActiveProvider;
        var extractorHealthy = false;
        try
        {
            if (extractor != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await extractor.ProbeHealthAsync().WaitAsync(cts.Token);
                extractorHealthy = probe?.IsHealthy ?? extractor.IsAvailable;
            }
        }
        catch
        {
            extractorHealthy = extractor?.IsAvailable ?? false;
        }

        var extractorCaps = extractor?.Capabilities;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "extractor",
            SubsystemName = "Archive Extractor Pipeline",
            ActiveProvider = this.extractorManager?.ActiveProviderId ?? "Unknown",
            Status = extractorHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["supportsRar5"] = extractorCaps?.SupportsRar5 ?? false,
                ["supports7z"] = extractorCaps?.Supports7z ?? false,
                ["supportsMultiPart"] = extractorCaps?.SupportsMultiPart ?? false,
                ["supportsPasswordProtected"] = extractorCaps?.SupportsPasswordProtected ?? false,
                ["activeExtractions"] = 0,
                ["isAvailable"] = extractor?.IsAvailable ?? false,
                ["mode"] = "NonBlockingWorker",
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetMediaInspectorTelemetryAsync(CancellationToken cancellationToken)
    {
        var inspector = this.mediaInspectorManager?.ActiveProvider;
        var inspectorHealthy = false;
        try
        {
            if (inspector != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await inspector.ProbeHealthAsync().WaitAsync(cts.Token);
                inspectorHealthy = probe?.IsHealthy ?? inspector.IsAvailable;
            }
        }
        catch
        {
            inspectorHealthy = inspector?.IsAvailable ?? false;
        }

        var inspectorCaps = inspector?.Capabilities;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "mediainspector",
            SubsystemName = "Media Container & Stream Inspector",
            ActiveProvider = this.mediaInspectorManager?.ActiveProviderId ?? "Unknown",
            Status = inspectorHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["pureEbmlParser"] = inspectorCaps?.SupportsPureManagedStreams ?? false,
                ["supportsDolbyVision"] = inspectorCaps?.SupportsDolbyVision ?? false,
                ["supportsHdr10Plus"] = inspectorCaps?.SupportsHdr10Plus ?? false,
                ["supportsEac3Atmos"] = inspectorCaps?.SupportsEac3Atmos ?? false,
                ["supportsSubtitleTracks"] = inspectorCaps?.SupportsSubtitleTracks ?? false,
                ["isAvailable"] = inspector?.IsAvailable ?? false,
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetGeoIpTelemetryAsync(CancellationToken cancellationToken)
    {
        var geoIp = this.geoIpManager?.ActiveProvider;
        var geoIpHealthy = false;
        try
        {
            if (geoIp != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await geoIp.ProbeHealthAsync().WaitAsync(cts.Token);
                geoIpHealthy = probe?.IsHealthy ?? geoIp.IsAvailable;
            }
        }
        catch
        {
            geoIpHealthy = geoIp?.IsAvailable ?? false;
        }

        var geoCaps = geoIp?.Capabilities ?? GeoIpCapabilities.None;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "geoip",
            SubsystemName = "Swarm GeoIP Geolocation",
            ActiveProvider = this.geoIpManager?.ActiveProviderId ?? "Unknown",
            Status = geoIpHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["databaseLoaded"] = geoIp?.IsAvailable ?? false,
                ["fastResolutionCache"] = true,
                ["supportsCountry"] = geoCaps.HasFlag(GeoIpCapabilities.Country),
                ["supportsCity"] = geoCaps.HasFlag(GeoIpCapabilities.City),
                ["supportsAsn"] = geoCaps.HasFlag(GeoIpCapabilities.Asn),
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetBlocklistTelemetryAsync(CancellationToken cancellationToken)
    {
        var blocklist = this.blocklistManager?.ActiveProvider;
        var blocklistHealthy = false;
        try
        {
            if (blocklist != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await blocklist.ProbeHealthAsync().WaitAsync(cts.Token);
                blocklistHealthy = probe?.IsHealthy ?? blocklist.IsAvailable;
            }
        }
        catch
        {
            blocklistHealthy = blocklist?.IsAvailable ?? false;
        }

        var blockCaps = blocklist?.Capabilities ?? BlocklistCapabilities.None;
        var totalRules = blocklist?.RuleCount ?? 0;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "blocklist",
            SubsystemName = "Swarm IP Blocklist & Filter",
            ActiveProvider = this.blocklistManager?.ActiveProviderId ?? "Unknown",
            Status = blocklistHealthy ? "Healthy" : "Degraded",
            ResourceLoad = totalRules > 100000 ? "High" : "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["rulesActive"] = totalRules > 0,
                ["ruleCount"] = totalRules,
                ["supportsIPv4"] = blockCaps.HasFlag(BlocklistCapabilities.IPv4),
                ["supportsIPv6"] = blockCaps.HasFlag(BlocklistCapabilities.IPv6),
                ["supportsCidr"] = blockCaps.HasFlag(BlocklistCapabilities.Cidr),
                ["lookupMode"] = "RadixTreeBinarySearch",
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetNetworkBindingTelemetryAsync(CancellationToken cancellationToken)
    {
        var netBinding = this.networkBindingManager?.ActiveProvider;
        var netBindingHealthy = false;
        try
        {
            if (netBinding != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await netBinding.ProbeHealthAsync().WaitAsync(cts.Token);
                netBindingHealthy = probe?.IsHealthy ?? netBinding.IsAvailable;
            }
        }
        catch
        {
            netBindingHealthy = netBinding?.IsAvailable ?? false;
        }

        var boundIface = !string.IsNullOrWhiteSpace(this.configService?.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : (this.configService?.BindInterface ?? "All Interfaces");
        var netCaps = netBinding?.Capabilities;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "networkbinding",
            SubsystemName = "Network Interface Binding & Kill Switch",
            ActiveProvider = this.networkBindingManager?.ActiveProviderId ?? "Unknown",
            Status = netBindingHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["boundInterface"] = boundIface,
                ["killSwitchArmed"] = !string.Equals(boundIface, "All Interfaces", StringComparison.OrdinalIgnoreCase) && !string.Equals(boundIface, "Any", StringComparison.OrdinalIgnoreCase),
                ["supportsInterfaceBinding"] = netCaps?.SupportsInterfaceBinding ?? false,
                ["supportsKernelLock"] = netCaps?.SupportsSoBindToDevice ?? false,
                ["supportsVpnKillSwitch"] = netCaps?.SupportsVpnKillSwitch ?? false,
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetMediaMetadataTelemetryAsync(CancellationToken cancellationToken)
    {
        var mediaMeta = this.mediaMetadataManager?.ActiveProvider;
        var mediaMetaHealthy = false;
        try
        {
            if (mediaMeta != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await mediaMeta.ProbeHealthAsync().WaitAsync(cts.Token);
                mediaMetaHealthy = probe?.IsHealthy ?? mediaMeta.IsAvailable;
            }
        }
        catch
        {
            mediaMetaHealthy = mediaMeta?.IsAvailable ?? false;
        }

        var mediaMetaCaps = mediaMeta?.Capabilities;
        var mediaCacheDir = !string.IsNullOrWhiteSpace(this.configService?.MediaCachePath)
            ? this.configService.MediaCachePath
            : (this.appFolderInfo != null ? Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache") : "/config/MediaCache");

        return new SubsystemTelemetryReport
        {
            SubsystemId = "mediametadata",
            SubsystemName = "Media Enrichment & Servarr Metadata",
            ActiveProvider = this.mediaMetadataManager?.ActiveProviderId ?? "Unknown",
            Status = mediaMetaHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["cacheDirectory"] = mediaCacheDir,
                ["supportsHighResPosters"] = mediaMetaCaps?.SupportsPosters ?? false,
                ["supportsMovies"] = mediaMetaCaps?.SupportsMovies ?? false,
                ["supportsTvSeries"] = mediaMetaCaps?.SupportsTvSeries ?? false,
                ["autoCleanupOnDelete"] = true,
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetHttpTransportTelemetryAsync(CancellationToken cancellationToken)
    {
        var httpTransport = this.httpTransportManager?.ActiveProvider;
        var httpHealthy = false;
        try
        {
            if (httpTransport != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await httpTransport.ProbeHealthAsync().WaitAsync(cts.Token);
                httpHealthy = probe?.IsHealthy ?? httpTransport.IsAvailable;
            }
        }
        catch
        {
            httpHealthy = httpTransport?.IsAvailable ?? false;
        }

        var httpCaps = httpTransport?.Capabilities;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "httptransport",
            SubsystemName = "HTTP Transport & Anti-Bot Engine",
            ActiveProvider = this.httpTransportManager?.ActiveProviderId ?? "Unknown",
            Status = httpHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["connectionPooling"] = true,
                ["http3QuicSupported"] = httpCaps?.SupportsHttp3Quic ?? false,
                ["tlsFingerprintEmulation"] = httpCaps?.SupportsBrowserFingerprintEmulation ?? false,
                ["supportsFlareSolverr"] = httpCaps?.SupportsFlareSolverr ?? false,
            },
        };
    }

    private async Task<SubsystemTelemetryReport> GetAiTelemetryAsync(CancellationToken cancellationToken)
    {
        var ai = this.aiManager?.ActiveProvider;
        var aiHealthy = false;
        try
        {
            if (ai != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var probe = await ai.ProbeHealthAsync().WaitAsync(cts.Token);
                aiHealthy = probe?.IsHealthy ?? ai.IsAvailable;
            }
        }
        catch
        {
            aiHealthy = ai?.IsAvailable ?? false;
        }

        var aiCaps = ai?.Capabilities ?? AiCapabilities.None;
        return new SubsystemTelemetryReport
        {
            SubsystemId = "ai",
            SubsystemName = "Artificial Intelligence & Swarm Copilot",
            ActiveProvider = this.aiManager?.ActiveProviderId ?? "Unknown",
            Status = aiHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["swarmDiagnostics"] = aiCaps.HasFlag(AiCapabilities.SupportsDiagnosticCopilot),
                ["releaseParsing"] = aiCaps.HasFlag(AiCapabilities.SupportsReleaseNameParsing),
                ["heuristicOptimization"] = aiCaps.HasFlag(AiCapabilities.SupportsSwarmOptimization),
                ["naturalLanguageSearch"] = aiCaps.HasFlag(AiCapabilities.SupportsNaturalLanguageSearch),
            },
        };
    }

    private static List<DiskMountPointMetrics> GetDiskMetrics(DateTime now)
    {
        lock (DriveLock)
        {
            var elapsed = now - lastDriveSampleTime;
            if (lastDriveSampleTime != DateTime.MinValue && elapsed < DriveCacheDuration && elapsed >= TimeSpan.Zero)
            {
                return new List<DiskMountPointMetrics>(cachedDriveMetrics);
            }

            try
            {
                cachedDriveMetrics = DriveMetricsProvider() ?? new List<DiskMountPointMetrics>();
            }
            catch
            {
                cachedDriveMetrics = new List<DiskMountPointMetrics>();
            }
            finally
            {
                lastDriveSampleTime = now;
            }

            return new List<DiskMountPointMetrics>(cachedDriveMetrics);
        }
    }

    internal static List<DiskMountPointMetrics> QuerySystemDrives()
    {
        var drives = new List<DiskMountPointMetrics>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    var total = drive.TotalSize;
                    var free = drive.AvailableFreeSpace;
                    var used = total - free;
                    var pct = total > 0 ? Math.Round(((double)used / total) * 100.0, 1) : 0.0;
                    drives.Add(new DiskMountPointMetrics
                    {
                        MountPoint = drive.RootDirectory.FullName,
                        DriveType = drive.DriveType.ToString(),
                        TotalSpaceBytes = total,
                        FreeSpaceBytes = free,
                        UsedSpaceBytes = used,
                        UsedPercent = pct,
                    });
                }
            }
        }
        catch (Exception)
        {
            // Ignore drive enumeration errors (e.g. permission/unmounted filesystems)
        }

        return drives;
    }
}
