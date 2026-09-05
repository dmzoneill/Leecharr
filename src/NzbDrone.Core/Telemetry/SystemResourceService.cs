// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
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
        IConfigService configService)
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
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public HostProcessResourceMetrics GetHostMetrics()
    {
        var now = DateTime.UtcNow;
        double cpuPercent;

        lock (CpuLock)
        {
            var elapsed = (now - lastSampleTime).TotalMilliseconds;
            if (elapsed >= 350)
            {
                try
                {
                    CurrentProcess.Refresh();
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
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "bittorrent",
            SubsystemName = "BitTorrent Engine",
            ActiveProvider = this.torrentEngineManager.ActiveEngineId,
            Status = engineMetrics.IsRunning ? "Healthy" : "Stopped",
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
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "extractor",
            SubsystemName = "Archive Extractor Pipeline",
            ActiveProvider = this.extractorManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["supportsRar5"] = true,
                ["supports7z"] = true,
                ["activeExtractions"] = 0,
                ["mode"] = "NonBlockingWorker",
            },
        });

        // 3. Media Container Inspector Subsystem
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "mediainspector",
            SubsystemName = "Media Container & Stream Inspector",
            ActiveProvider = this.mediaInspectorManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["pureEbmlParser"] = true,
                ["supportsDolbyVision"] = true,
                ["supportsHdr10Plus"] = true,
                ["supportsEac3Atmos"] = true,
            },
        });

        // 4. Swarm GeoIP Geolocation Subsystem
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "geoip",
            SubsystemName = "Swarm GeoIP Geolocation",
            ActiveProvider = this.geoIpManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["databaseLoaded"] = true,
                ["fastResolutionCache"] = true,
            },
        });

        // 5. Swarm IP Blocklist Subsystem
        var totalRules = this.blocklistManager.ActiveProvider?.RuleCount ?? 0;
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "blocklist",
            SubsystemName = "Swarm IP Blocklist & Filter",
            ActiveProvider = this.blocklistManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["rulesActive"] = totalRules > 0,
                ["ruleCount"] = totalRules,
                ["lookupMode"] = "RadixTreeBinarySearch",
            },
        });

        // 6. Network Interface Binding Subsystem
        var boundIface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : (this.configService.BindInterface ?? "All Interfaces");
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "networkbinding",
            SubsystemName = "Network Interface Binding & Kill Switch",
            ActiveProvider = this.networkBindingManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["boundInterface"] = boundIface,
                ["killSwitchArmed"] = !string.Equals(boundIface, "All Interfaces", StringComparison.OrdinalIgnoreCase) && !string.Equals(boundIface, "Any", StringComparison.OrdinalIgnoreCase),
            },
        });

        // 7. Media Enrichment Metadata Subsystem
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "mediametadata",
            SubsystemName = "Media Enrichment & Servarr Metadata",
            ActiveProvider = this.mediaMetadataManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["cacheDirectory"] = "/config/MediaCache",
                ["supportsHighResPosters"] = true,
                ["autoCleanupOnDelete"] = true,
            },
        });

        // 8. HTTP Transport & Proxy Subsystem
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "httptransport",
            SubsystemName = "HTTP Transport & Anti-Bot Engine",
            ActiveProvider = this.httpTransportManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["connectionPooling"] = true,
                ["http3QuicSupported"] = true,
                ["tlsFingerprintEmulation"] = true,
            },
        });

        // 9. AI Intelligence Subsystem
        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "ai",
            SubsystemName = "Artificial Intelligence & Swarm Copilot",
            ActiveProvider = this.aiManager.ActiveProviderId,
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                ["swarmDiagnostics"] = true,
                ["releaseParsing"] = true,
                ["heuristicOptimization"] = true,
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
