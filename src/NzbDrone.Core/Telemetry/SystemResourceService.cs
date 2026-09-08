// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
    private static DateTime lastSampleTime = DateTime.UtcNow;
    private static TimeSpan lastTotalProcessorTime = CurrentProcess.TotalProcessorTime;
    private static double cachedCpuPercent;

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
    {
        this.torrentEngineManager = torrentEngineManager;
        this.extractorManager = extractorManager;
        this.mediaInspectorManager = mediaInspectorManager;
        this.geoIpManager = geoIpManager;
        this.blocklistManager = blocklistManager;
        this.networkBindingManager = networkBindingManager;
        this.mediaMetadataManager = mediaMetadataManager;
        this.httpTransportManager = httpTransportManager;
        this.aiManager = aiManager;
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public HostProcessResourceMetrics GetHostMetrics()
    {
        var now = DateTime.UtcNow;
        double cpuPercent;

        try
        {
            CurrentProcess.Refresh();
        }
        catch
        {
        }

        lock (CpuLock)
        {
            var elapsed = (now - lastSampleTime).TotalMilliseconds;
            if (elapsed >= 350)
            {
                try
                {
                    var totalTime = CurrentProcess.TotalProcessorTime;
                    var cpuUsedMs = (totalTime - lastTotalProcessorTime).TotalMilliseconds;
                    lastSampleTime = now;
                    lastTotalProcessorTime = totalTime;
                    var cores = Math.Max(1, Environment.ProcessorCount);
                    cachedCpuPercent = Math.Clamp((cpuUsedMs / (elapsed * cores)) * 100.0, 0.0, 100.0);
                }
                catch
                {
                }
            }

            cpuPercent = Math.Round(cachedCpuPercent, 1);
        }

        ThreadPool.GetAvailableThreads(out var availWorker, out var availCompletion);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxCompletion);

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
        catch
        {
        }

        long uptimeSec = 0;
        try
        {
            uptimeSec = (long)(DateTime.UtcNow - CurrentProcess.StartTime.ToUniversalTime()).TotalSeconds;
        }
        catch
        {
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

    public List<SubsystemTelemetryReport> GetSubsystemTelemetry()
    {
        var reports = new List<SubsystemTelemetryReport>();

        // 1. BitTorrent Engine Subsystem
        var engineMetrics = this.GetTorrentEngineMetrics();
        var engine = this.torrentEngineManager.ActiveEngine;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "bittorrent",
            SubsystemName = "BitTorrent Engine",
            ActiveProvider = this.torrentEngineManager.ActiveEngineId,
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
        });

        // 2. Archive Extractor Subsystem
        var extractor = this.extractorManager.ActiveProvider;
        bool extractorHealthy;
        try
        {
            var probe = extractor != null ? extractor.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            extractorHealthy = probe?.IsHealthy ?? (extractor?.IsAvailable ?? false);
        }
        catch
        {
            extractorHealthy = extractor?.IsAvailable ?? false;
        }

        var extractorCaps = extractor?.Capabilities;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "extractor",
            SubsystemName = "Archive Extractor Pipeline",
            ActiveProvider = this.extractorManager.ActiveProviderId,
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
        });

        // 3. Media Container Inspector Subsystem
        var inspector = this.mediaInspectorManager.ActiveProvider;
        bool inspectorHealthy;
        try
        {
            var probe = inspector != null ? inspector.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            inspectorHealthy = probe?.IsHealthy ?? (inspector?.IsAvailable ?? false);
        }
        catch
        {
            inspectorHealthy = inspector?.IsAvailable ?? false;
        }

        var inspectorCaps = inspector?.Capabilities;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "mediainspector",
            SubsystemName = "Media Container & Stream Inspector",
            ActiveProvider = this.mediaInspectorManager.ActiveProviderId,
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
        });

        // 4. Swarm GeoIP Geolocation Subsystem
        var geoIp = this.geoIpManager.ActiveProvider;
        bool geoIpHealthy;
        try
        {
            var probe = geoIp != null ? geoIp.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            geoIpHealthy = probe?.IsHealthy ?? (geoIp?.IsAvailable ?? false);
        }
        catch
        {
            geoIpHealthy = geoIp?.IsAvailable ?? false;
        }

        var geoCaps = geoIp?.Capabilities ?? GeoIpCapabilities.None;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "geoip",
            SubsystemName = "Swarm GeoIP Geolocation",
            ActiveProvider = this.geoIpManager.ActiveProviderId,
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
        });

        // 5. Swarm IP Blocklist Subsystem
        var blocklist = this.blocklistManager.ActiveProvider;
        bool blocklistHealthy;
        try
        {
            var probe = blocklist != null ? blocklist.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            blocklistHealthy = probe?.IsHealthy ?? (blocklist?.IsAvailable ?? false);
        }
        catch
        {
            blocklistHealthy = blocklist?.IsAvailable ?? false;
        }

        var blockCaps = blocklist?.Capabilities ?? BlocklistCapabilities.None;
        var totalRules = blocklist?.RuleCount ?? 0;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "blocklist",
            SubsystemName = "Swarm IP Blocklist & Filter",
            ActiveProvider = this.blocklistManager.ActiveProviderId,
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
        });

        // 6. Network Interface Binding Subsystem
        var netBinding = this.networkBindingManager.ActiveProvider;
        bool netBindingHealthy;
        try
        {
            var probe = netBinding != null ? netBinding.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            netBindingHealthy = probe?.IsHealthy ?? (netBinding?.IsAvailable ?? false);
        }
        catch
        {
            netBindingHealthy = netBinding?.IsAvailable ?? false;
        }

        var boundIface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : (this.configService.BindInterface ?? "All Interfaces");
        var netCaps = netBinding?.Capabilities;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "networkbinding",
            SubsystemName = "Network Interface Binding & Kill Switch",
            ActiveProvider = this.networkBindingManager.ActiveProviderId,
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
        });

        // 7. Media Enrichment Metadata Subsystem
        var mediaMeta = this.mediaMetadataManager.ActiveProvider;
        bool mediaMetaHealthy;
        try
        {
            var probe = mediaMeta != null ? mediaMeta.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            mediaMetaHealthy = probe?.IsHealthy ?? (mediaMeta?.IsAvailable ?? false);
        }
        catch
        {
            mediaMetaHealthy = mediaMeta?.IsAvailable ?? false;
        }

        var mediaMetaCaps = mediaMeta?.Capabilities;
        var mediaCacheDir = !string.IsNullOrWhiteSpace(this.configService.MediaCachePath)
            ? this.configService.MediaCachePath
            : (this.appFolderInfo != null ? Path.Combine(this.appFolderInfo.AppDataFolder, "MediaCache") : "/config/MediaCache");

        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "mediametadata",
            SubsystemName = "Media Enrichment & Servarr Metadata",
            ActiveProvider = this.mediaMetadataManager.ActiveProviderId,
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
        });

        // 8. HTTP Transport & Proxy Subsystem
        var httpTransport = this.httpTransportManager.ActiveProvider;
        bool httpHealthy;
        try
        {
            var probe = httpTransport != null ? httpTransport.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            httpHealthy = probe?.IsHealthy ?? (httpTransport?.IsAvailable ?? false);
        }
        catch
        {
            httpHealthy = httpTransport?.IsAvailable ?? false;
        }

        var httpCaps = httpTransport?.Capabilities;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "httptransport",
            SubsystemName = "HTTP Transport & Anti-Bot Engine",
            ActiveProvider = this.httpTransportManager.ActiveProviderId,
            Status = httpHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["connectionPooling"] = true,
                ["http3QuicSupported"] = httpCaps?.SupportsHttp3Quic ?? false,
                ["tlsFingerprintEmulation"] = httpCaps?.SupportsBrowserFingerprintEmulation ?? false,
                ["supportsFlareSolverr"] = httpCaps?.SupportsFlareSolverr ?? false,
            },
        });

        // 9. AI Intelligence Subsystem
        var ai = this.aiManager.ActiveProvider;
        bool aiHealthy;
        try
        {
            var probe = ai != null ? ai.ProbeHealthAsync().GetAwaiter().GetResult() : null;
            aiHealthy = probe?.IsHealthy ?? (ai?.IsAvailable ?? false);
        }
        catch
        {
            aiHealthy = ai?.IsAvailable ?? false;
        }

        var aiCaps = ai?.Capabilities ?? AiCapabilities.None;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "ai",
            SubsystemName = "Artificial Intelligence & Swarm Copilot",
            ActiveProvider = this.aiManager.ActiveProviderId,
            Status = aiHealthy ? "Healthy" : "Degraded",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["swarmDiagnostics"] = aiCaps.HasFlag(AiCapabilities.SupportsDiagnosticCopilot),
                ["releaseParsing"] = aiCaps.HasFlag(AiCapabilities.SupportsReleaseNameParsing),
                ["heuristicOptimization"] = aiCaps.HasFlag(AiCapabilities.SupportsSwarmOptimization),
                ["naturalLanguageSearch"] = aiCaps.HasFlag(AiCapabilities.SupportsNaturalLanguageSearch),
            },
        });

        return reports;
    }

    public SystemResourceTelemetrySnapshot GetFullTelemetrySnapshot()
    {
        return new SystemResourceTelemetrySnapshot
        {
            Host = this.GetHostMetrics(),
            TorrentEngine = this.GetTorrentEngineMetrics(),
            PerTorrent = this.GetPerTorrentMetrics().ToList(),
            Subsystems = this.GetSubsystemTelemetry(),
            Timestamp = DateTime.UtcNow,
        };
    }
}
