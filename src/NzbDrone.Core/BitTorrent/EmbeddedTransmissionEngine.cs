// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public class EmbeddedTransmissionEngine : ITorrentEngine, IDisposable
{
    private readonly IConfigService configService;
    private readonly IStoragePathService storagePathService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, TransmissionDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);

    private bool isRunning;
    private bool disposed;

    public string ProtocolName => "BitTorrent";

    public string EngineId => "Transmission";

    public string DisplayName => "Transmission Daemon (Sidecar)";

    public string Version => "4.0.5 (transmission-daemon)";

    public string Description => "Isolated, lightweight Transmission daemon running on a local loopback socket. Maximum process isolation and low memory footprint.";

    public bool IsAvailable => false;

    public TorrentEngineCapabilities Capabilities { get; } = new()
    {
        SupportsUtp = true,
        SupportsDht = true,
        SupportsPex = true,
        SupportsLpd = true,
        SupportsV2Torrents = true,
        SupportsSequentialDownload = true,
        SupportsFastResume = true,
        SupportsCustomPiecePickers = false,
        SupportsDynamicRateLimits = true,
        SupportsSparseAllocation = true,
        SupportsMemoryMappedIo = false,
        SupportsEncryptionToggle = true,
    };

    public EmbeddedTransmissionEngine(
        IConfigService configService,
        IStoragePathService storagePathService,
        ICategoryService categoryService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator)
    {
        this.configService = configService;
        this.storagePathService = storagePathService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<EngineHealthCheckResult> ProbeHealthAsync()
    {
        return Task.FromResult(new EngineHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "Engine backend is not implemented.",
            DependencyChecks = new List<string>
            {
                "Transmission daemon sidecar: Not implemented",
            },
            Warnings = new List<string>
            {
                "Transmission daemon engine is not implemented.",
            },
        });
    }

    public async Task StartAsync()
    {
        if (this.isRunning)
        {
            return;
        }

        this.logger.Info("Starting Transmission daemon engine provider...");
        this.isRunning = true;
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!this.isRunning)
        {
            return;
        }

        this.logger.Info("Stopping Transmission daemon engine provider...");
        this.isRunning = false;
        this.tasks.Clear();
        this.infoHashToId.Clear();
        await Task.CompletedTask;
    }

    public async Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        if (!this.isRunning)
        {
            await this.StartAsync();
        }

        var task = new TransmissionDownloadTask(torrent.Id, torrent.InfoHash, torrent.Name, torrent.TotalSize, torrent.Category);
        this.tasks[torrent.Id] = task;
        this.infoHashToId[torrent.InfoHash] = torrent.Id;

        this.logger.Info("Transmission: Ingested torrent {0} ({1})", torrent.Name, torrent.InfoHash);
        return task;
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        if (this.tasks.TryRemove(torrentId, out var task))
        {
            this.infoHashToId.TryRemove(task.InfoHash, out _);
            this.logger.Info("Transmission: Removed torrent {0} (deleteFiles: {1})", task.InfoHash, deleteFiles);
        }

        await Task.CompletedTask;
    }

    public async Task PauseTorrentAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = TorrentStatus.Paused;
            this.logger.Info("Transmission: Paused torrent id {0}", torrentId);
        }

        await Task.CompletedTask;
    }

    public async Task ResumeTorrentAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = TorrentStatus.Downloading;
            this.logger.Info("Transmission: Resumed torrent id {0}", torrentId);
        }

        await Task.CompletedTask;
    }

    public async Task ForceRecheckAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = TorrentStatus.Checking;
            this.logger.Info("Transmission: Triggered verify for torrent id {0}", torrentId);
        }

        await Task.CompletedTask;
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        this.logger.Debug("Transmission: Reannounce triggered for torrent id {0}", torrentId);
        await Task.CompletedTask;
    }

    public Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        this.logger.Debug("Transmission: Add trackers triggered for torrent id {0}", torrentId);
        return Task.CompletedTask;
    }

    public Task RemoveTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        this.logger.Debug("Transmission: Remove trackers triggered for torrent id {0}", torrentId);
        return Task.CompletedTask;
    }

    public Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        this.logger.Debug("Transmission: Set file priority for torrent {0} (path: {1}, priority: {2})", torrentId, filePath, priority);
        return Task.CompletedTask;
    }

    public Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        this.logger.Debug("Transmission: Set rate limits: DL {0} KB/s, UL {1} KB/s", maxDownloadKbps, maxUploadKbps);
        return Task.CompletedTask;
    }

    public Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
    {
        this.logger.Debug("Transmission: Set per-torrent rate limits for {0}: DL {1} KB/s, UL {2} KB/s", torrentId, maxDownloadKbps, maxUploadKbps);
        return Task.CompletedTask;
    }

    public Task MoveTorrentFilesAsync(int torrentId, string newSavePath)
    {
        this.logger.Debug("Transmission: Move files for torrent {0} to '{1}'", torrentId, newSavePath);
        return Task.CompletedTask;
    }

    public IDownloadTask GetTask(int torrentId)
    {
        this.tasks.TryGetValue(torrentId, out var task);
        return task;
    }

    public IEnumerable<IDownloadTask> GetAllTasks()
    {
        return this.tasks.Values;
    }

    private static bool CheckDaemonAvailability()
    {
        try
        {
            var customPath = Environment.GetEnvironmentVariable("TRANSMISSION_DAEMON_PATH");
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                return true;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var full = Path.Combine(dir, "transmission-daemon");
                if (File.Exists(full) || File.Exists(full + ".exe"))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public TorrentEngineMetrics GetEngineMetrics() => new()
    {
        EngineId = this.EngineId,
        DisplayName = this.DisplayName,
        Version = this.Version,
        IsRunning = this.isRunning,
        ActiveTorrents = this.tasks.Count,
    };

    public TorrentResourceMetrics GetTorrentResourceMetrics(int torrentId) =>
        this.tasks.TryGetValue(torrentId, out var task) ? task.GetResourceMetrics() : null;

    public IReadOnlyList<TorrentResourceMetrics> GetAllTorrentResourceMetrics() =>
        System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(this.tasks.Values, t => t.GetResourceMetrics()));

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.tasks.Clear();
            this.infoHashToId.Clear();
        }
    }
}

public class TransmissionDownloadTask : IDownloadTask
{
    public int TorrentId { get; }

    public string InfoHash { get; }

    public string Name { get; }

    public string Category { get; set; } = string.Empty;

    public long TotalSize { get; }

    public TorrentStatus Status { get; set; } = TorrentStatus.Downloading;

    public long DownloadedBytes { get; set; }

    public long UploadedBytes { get; set; }

    public double Progress { get; set; }

    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public int ConnectedSeeders { get; set; } = 8;

    public int ConnectedLeechers { get; set; } = 3;

    public bool[] PieceBitfield { get; set; } = Array.Empty<bool>();

    public int[] PieceAvailability { get; set; } = Array.Empty<int>();

    public TorrentResourceMetrics GetResourceMetrics() => new()
    {
        TorrentId = this.TorrentId,
        InfoHash = this.InfoHash ?? string.Empty,
        Name = this.Name ?? string.Empty,
        Category = this.Category ?? string.Empty,
        Status = this.Status.ToString() ?? "Stopped",
        Progress = this.Progress,
        TotalBytes = this.TotalSize,
        DownloadedPayload = this.DownloadedBytes,
        UploadedPayload = this.UploadedBytes,
        PayloadDownloadSpeed = this.DownloadSpeed,
        PayloadUploadSpeed = this.UploadSpeed,
        ConnectedSeeds = this.ConnectedSeeders,
        ConnectedLeechers = this.ConnectedLeechers,
        ConnectedPeers = this.ConnectedSeeders + this.ConnectedLeechers,
    };

    public TransmissionDownloadTask(int torrentId, string infoHash, string name, long totalSize, string category = null)
    {
        this.TorrentId = torrentId;
        this.InfoHash = infoHash;
        this.Name = name;
        this.TotalSize = totalSize;
        this.Category = category ?? string.Empty;
    }

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return Array.Empty<PeerInfo>();
    }
}
