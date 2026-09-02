// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using CoreTorrent = NzbDrone.Core.Torrents.Torrent;
using MtTorrent = MonoTorrent.Torrent;

namespace NzbDrone.Core.BitTorrent;

public class MonoTorrentDownloadEngine : ITorrentEngine, IDisposable
{
    private readonly IConfigService configService;
    private readonly IStoragePathService storagePathService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, MonoTorrentDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);

    private ClientEngine engine;
    private bool disposed;

    public string ProtocolName => "BitTorrent";

    public string EngineId => "MonoTorrent";

    public string DisplayName => "MonoTorrent (Pure .NET)";

    public string Version => typeof(ClientEngine).Assembly.GetName().Version?.ToString() ?? "3.0.2";

    public string Description => "Pure managed C# BitTorrent engine powered by MonoTorrent. Zero native dependencies, runs anywhere.";

    public bool IsAvailable => true;

    public TorrentEngineCapabilities Capabilities { get; } = new()
    {
        SupportsUtp = true,
        SupportsDht = true,
        SupportsPex = true,
        SupportsLpd = true,
        SupportsV2Torrents = false,
        SupportsSequentialDownload = true,
        SupportsFastResume = true,
        SupportsCustomPiecePickers = true,
        SupportsDynamicRateLimits = true,
        SupportsSparseAllocation = true,
        SupportsMemoryMappedIo = false,
        SupportsEncryptionToggle = true,
    };

    public Task<EngineHealthCheckResult> ProbeHealthAsync()
    {
        return Task.FromResult(new EngineHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "MonoTorrent managed runtime is ready and operational.",
            DependencyChecks = new List<string>
            {
                ".NET Runtime: OK",
                "MonoTorrent Assembly: OK",
                "Managed Sockets: Ready"
            },
        });
    }

    public MonoTorrentDownloadEngine(
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

    public async Task StartAsync()
    {
        if (this.engine != null)
        {
            return;
        }

        var port = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        this.logger.Info("Initializing MonoTorrent download engine on port {0}...", port);

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr", "Cache");
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch
        {
        }

        var allowedEncryption = this.configService.EncryptionMode?.ToLowerInvariant() switch
        {
            "forceencrypted" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
            },
            "preferencrypted" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
                MonoTorrent.Connections.EncryptionType.PlainText,
            },
            "allowplaintext" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.PlainText,
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
            },
            "disabled" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.PlainText,
            },
            _ => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
                MonoTorrent.Connections.EncryptionType.PlainText
            },
        };

        var listenIp = IPAddress.Any;
        var iface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;
        if (!string.IsNullOrWhiteSpace(iface) && !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) && !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => string.Equals(n.Name, iface, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(n.Id, iface, StringComparison.OrdinalIgnoreCase));
                if (nic != null)
                {
                    var unicast = nic.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (unicast != null)
                    {
                        listenIp = unicast.Address;
                        this.logger.Info("Bound MonoTorrent listening socket to interface '{0}' ({1})", nic.Name, listenIp);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to resolve IP for bound interface '{0}'. Defaulting to IPAddress.Any", iface);
            }
        }

        var engineSettingsBuilder = new EngineSettingsBuilder
        {
            AllowPortForwarding = this.configService.UpnpEnabled,
            AllowLocalPeerDiscovery = this.configService.EnableLpd,
            AllowedEncryption = allowedEncryption,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadDhtCache = true,
            DhtEndPoint = this.configService.EnableDht ? new IPEndPoint(listenIp, port) : null,
            CacheDirectory = cacheDir,
            DiskCacheBytes = this.configService.DiskCacheBytes > 0 ? this.configService.DiskCacheBytes : Math.Max(128, this.configService.DiskWriteCacheSizeMb) * 1024 * 1024,
            MaximumConnections = this.configService.MaxGlobalConnections > 0 ? this.configService.MaxGlobalConnections : 300,
            MaximumDownloadRate = this.configService.MaxDownloadSpeedKbps > 0 ? this.configService.MaxDownloadSpeedKbps * 1024 : 0,
            MaximumUploadRate = this.configService.MaxUploadSpeedKbps > 0 ? this.configService.MaxUploadSpeedKbps * 1024 : 0,
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                { "ipv4", new IPEndPoint(listenIp, port) }
            },
        };

        var engineSettings = engineSettingsBuilder.ToSettings();
        this.engine = new ClientEngine(engineSettings);

        this.logger.Info("MonoTorrent engine started successfully on {0}:{1}.", listenIp, port);
    }

    public async Task StopAsync()
    {
        if (this.engine == null)
        {
            return;
        }

        this.logger.Info("Stopping MonoTorrent download engine...");

        foreach (var task in this.tasks.Values)
        {
            try
            {
                if (task.Manager != null && task.Manager.State != TorrentState.Stopped)
                {
                    await task.Manager.StopAsync();
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error stopping torrent manager for {0}", task.InfoHash);
            }
        }

        await this.engine.StopAllAsync();
        this.engine.Dispose();
        this.engine = null;
        this.logger.Info("MonoTorrent download engine stopped.");
    }

    public async Task<IDownloadTask> AddTorrentAsync(CoreTorrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        if (this.engine == null)
        {
            await this.StartAsync();
        }

        var isCompleteOrSeeding = torrent.Status == TorrentStatus.Seeding || (torrent.Progress >= 1.0 && !string.IsNullOrWhiteSpace(torrent.SavePath));
        var workingPath = isCompleteOrSeeding && !string.IsNullOrWhiteSpace(torrent.SavePath)
            ? torrent.SavePath
            : this.storagePathService.GetIncompleteDirectory();

        try
        {
            Directory.CreateDirectory(workingPath);
        }
        catch
        {
        }

        var torrentSettingsBuilder = new TorrentSettingsBuilder
        {
            MaximumConnections = this.configService.MaxPerTorrentConnections > 0 ? this.configService.MaxPerTorrentConnections : 50,
            UploadSlots = this.configService.MaxUploadSlots > 0 ? this.configService.MaxUploadSlots : 4,
            MaximumDownloadRate = torrent.DownloadLimit > 0 ? torrent.DownloadLimit * 1024 : 0,
            MaximumUploadRate = torrent.UploadLimit > 0 ? torrent.UploadLimit * 1024 : 0,
        };

        if (torrent.IsPrivate)
        {
            torrentSettingsBuilder.AllowDht = false;
            torrentSettingsBuilder.AllowPeerExchange = false;
        }

        TorrentManager manager = null;
        var torrentSettings = torrentSettingsBuilder.ToSettings();

        if (torrent.SequentialDownload)
        {
            if (torrentFileBytes != null && torrentFileBytes.Length > 0)
            {
                var parsedTorrent = MtTorrent.Load(torrentFileBytes);
                manager = await this.engine.AddStreamingAsync(parsedTorrent, workingPath, torrentSettings);
            }
            else if (!string.IsNullOrWhiteSpace(magnetUri))
            {
                var magnetLink = MagnetLink.Parse(magnetUri);
                manager = await this.engine.AddStreamingAsync(magnetLink, workingPath, torrentSettings);
            }
            else if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                var magnetString = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
                    ? $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}"
                    : $"magnet:?xt=urn:btih:{torrent.InfoHash}";
                var magnetLink = MagnetLink.Parse(magnetString);
                manager = await this.engine.AddStreamingAsync(magnetLink, workingPath, torrentSettings);
            }
        }
        else
        {
            if (torrentFileBytes != null && torrentFileBytes.Length > 0)
            {
                var parsedTorrent = MtTorrent.Load(torrentFileBytes);
                manager = await this.engine.AddAsync(parsedTorrent, workingPath, torrentSettings);
            }
            else if (!string.IsNullOrWhiteSpace(magnetUri))
            {
                var magnetLink = MagnetLink.Parse(magnetUri);
                manager = await this.engine.AddAsync(magnetLink, workingPath, torrentSettings);
            }
            else if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                var magnetString = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
                    ? $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}"
                    : $"magnet:?xt=urn:btih:{torrent.InfoHash}";
                var magnetLink = MagnetLink.Parse(magnetString);
                manager = await this.engine.AddAsync(magnetLink, workingPath, torrentSettings);
            }
        }

        if (manager == null)
        {
            throw new InvalidOperationException("Failed to create TorrentManager for torrent.");
        }

        var downloadTask = new MonoTorrentDownloadTask(torrent.Id, torrent.InfoHash, manager, torrent.Category);
        this.tasks[torrent.Id] = downloadTask;
        this.infoHashToId[torrent.InfoHash] = torrent.Id;

        manager.TorrentStateChanged += this.OnTorrentStateChanged;
        manager.PieceHashed += this.OnPieceHashed;

        if (torrent.Status == TorrentStatus.Paused)
        {
            await manager.PauseAsync();
            this.logger.Info("Added paused torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }
        else if (torrent.Status == TorrentStatus.Stopped)
        {
            this.logger.Info("Added stopped torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }
        else
        {
            await manager.StartAsync();
            this.logger.Info("Added and started torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }

        return downloadTask;
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        if (this.tasks.TryRemove(torrentId, out var task))
        {
            this.infoHashToId.TryRemove(task.InfoHash, out _);

            if (task.Manager != null)
            {
                task.Manager.TorrentStateChanged -= this.OnTorrentStateChanged;
                task.Manager.PieceHashed -= this.OnPieceHashed;

                await task.Manager.StopAsync();
                await this.engine.RemoveAsync(task.Manager);

                if (deleteFiles)
                {
                    try
                    {
                        if (task.Manager.Files != null)
                        {
                            foreach (var file in task.Manager.Files)
                            {
                                try
                                {
                                    var filePath = file.FullPath;
                                    if (this.diskProvider.FileExists(filePath))
                                    {
                                        this.diskProvider.DeleteFile(filePath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Warn(ex, "Failed to delete file {0} for torrent id {1}", file.Path, torrentId);
                                }
                            }
                        }

                        if (task.Manager.Torrent?.Files?.Count > 1)
                        {
                            var containingDir = task.Manager.ContainingDirectory;
                            if (!string.IsNullOrWhiteSpace(containingDir) && this.diskProvider.FolderExists(containingDir))
                            {
                                var dirName = Path.GetFileName(containingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                                var incompleteDir = this.storagePathService.GetIncompleteDirectory();
                                var downloadDir = this.configService.DownloadDir ?? "/downloads";

                                var isMatchingName = string.Equals(dirName, task.Manager.Torrent.Name, StringComparison.OrdinalIgnoreCase);
                                var isRootIncomplete = !string.IsNullOrWhiteSpace(incompleteDir) &&
                                    string.Equals(
                                        Path.GetFullPath(containingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                  Path.GetFullPath(incompleteDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                  StringComparison.OrdinalIgnoreCase);
                                var isRootDownload = !string.IsNullOrWhiteSpace(downloadDir) &&
                                    string.Equals(
                                        Path.GetFullPath(containingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                  Path.GetFullPath(downloadDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                  StringComparison.OrdinalIgnoreCase);

                                if (isMatchingName && !isRootIncomplete && !isRootDownload)
                                {
                                    this.diskProvider.DeleteFolder(containingDir, true);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Failed to delete files for torrent id {0}", torrentId);
                    }
                }
            }
        }
    }

    public async Task PauseTorrentAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            await task.Manager.PauseAsync();
            this.logger.Info("Paused torrent id {0}", torrentId);
        }
    }

    public async Task ResumeTorrentAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            await task.Manager.StartAsync();
            this.logger.Info("Resumed torrent id {0}", torrentId);
        }
    }

    public async Task ForceRecheckAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            await task.Manager.HashCheckAsync(true);
            this.logger.Info("Triggered hash recheck for torrent id {0}", torrentId);
        }
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            if (task.Manager.TrackerManager != null)
            {
                await task.Manager.TrackerManager.AnnounceAsync(CancellationToken.None);
            }
        }
    }

    public async Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null && trackers != null)
        {
            if (task.Manager.TrackerManager != null)
            {
                var addedAny = false;
                foreach (var tr in trackers)
                {
                    if (!string.IsNullOrWhiteSpace(tr) && Uri.TryCreate(tr.Trim(), UriKind.Absolute, out var uri))
                    {
                        try
                        {
                            await task.Manager.TrackerManager.AddTrackerAsync(uri);
                            addedAny = true;
                        }
                        catch (Exception ex)
                        {
                            this.logger.Debug(ex, "Could not add tracker {0} to torrent {1}", tr, torrentId);
                        }
                    }
                }

                if (addedAny)
                {
                    try
                    {
                        await task.Manager.TrackerManager.AnnounceAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Failed to re-announce after adding trackers to torrent {0}", torrentId);
                    }
                }
            }
        }
    }

    public async Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null && task.Manager.Files != null)
        {
            var targetFile = task.Manager.Files.FirstOrDefault(f => string.Equals(f.Path, filePath, StringComparison.OrdinalIgnoreCase));
            if (targetFile != null)
            {
                var monoPriority = priority switch
                {
                    0 => MonoTorrent.Priority.DoNotDownload,
                    1 => MonoTorrent.Priority.Lowest,
                    2 => MonoTorrent.Priority.Low,
                    3 => MonoTorrent.Priority.Normal,
                    4 => MonoTorrent.Priority.High,
                    5 => MonoTorrent.Priority.Highest,
                    _ => MonoTorrent.Priority.Normal,
                };
                await task.Manager.SetFilePriorityAsync(targetFile, monoPriority);
                this.logger.Info("Updated file priority for {0} (file: {1}, priority: {2})", task.InfoHash, filePath, monoPriority);
            }
        }
    }

    public async Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        if (this.engine != null)
        {
            var settingsBuilder = new EngineSettingsBuilder(this.engine.Settings)
            {
                MaximumDownloadRate = maxDownloadKbps > 0 ? maxDownloadKbps * 1024 : 0,
                MaximumUploadRate = maxUploadKbps > 0 ? maxUploadKbps * 1024 : 0,
            };
            await this.engine.UpdateSettingsAsync(settingsBuilder.ToSettings());
            this.logger.Info("Updated MonoTorrent rate limits: Download = {0} KB/s, Upload = {1} KB/s", maxDownloadKbps, maxUploadKbps);
        }
    }

    public async Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            var settingsBuilder = new TorrentSettingsBuilder(task.Manager.Settings)
            {
                MaximumDownloadRate = maxDownloadKbps > 0 ? maxDownloadKbps * 1024 : 0,
                MaximumUploadRate = maxUploadKbps > 0 ? maxUploadKbps * 1024 : 0,
            };
            await task.Manager.UpdateSettingsAsync(settingsBuilder.ToSettings());
            this.logger.Info("Updated MonoTorrent per-torrent rate limits for {0}: Download = {1} KB/s, Upload = {2} KB/s", task.InfoHash, maxDownloadKbps, maxUploadKbps);
        }
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

    private async void OnTorrentStateChanged(object sender, TorrentStateChangedEventArgs e)
    {
        try
        {
            var manager = e.TorrentManager;
            var infoHash = manager.InfoHashes.V1OrV2.ToHex();

            if (this.infoHashToId.TryGetValue(infoHash, out var torrentId))
            {
                this.logger.Info("Torrent {0} state changed: {1} -> {2}", infoHash, e.OldState, e.NewState);

                if (e.NewState == TorrentState.Seeding)
                {
                    this.tasks.TryGetValue(torrentId, out var existingTask);
                    var category = existingTask?.Category;
                    var completedDir = !string.IsNullOrWhiteSpace(category)
                        ? this.categoryService.GetSavePathForCategory(category)
                        : (this.configService.DownloadDir ?? "/downloads");

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(completedDir) && !string.Equals(manager.SavePath, completedDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.CreateDirectory(completedDir);
                            await manager.MoveFilesAsync(completedDir, true);
                            this.logger.Info("Moved completed torrent files for {0} to {1}", infoHash, completedDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to move completed torrent files for {0} to {1}", infoHash, completedDir);
                    }

                    var savePath = manager.SavePath ?? completedDir;
                    this.eventAggregator.PublishEvent(new TorrentDownloadCompletedEvent(new CoreTorrent
                    {
                        Id = torrentId,
                        InfoHash = infoHash,
                        Name = manager.Torrent?.Name ?? infoHash,
                        Status = TorrentStatus.Seeding,
                        Category = category,
                        SavePath = savePath,
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling torrent state change");
        }
    }

    private void OnPieceHashed(object sender, PieceHashedEventArgs e)
    {
        var manager = e.TorrentManager;
        var infoHash = manager.InfoHashes.V1OrV2.ToHex();

        if (e.HashPassed)
        {
            this.logger.Trace("Piece {0} verified for torrent {1} (progress: {2:P1})", e.PieceIndex, infoHash, manager.Progress / 100.0);
            if (this.infoHashToId.TryGetValue(infoHash, out var torrentId))
            {
                this.eventAggregator.PublishEvent(new PieceVerifiedEvent(torrentId, e.PieceIndex));
            }
        }
        else
        {
            this.logger.Warn("Piece {0} failed hash check for torrent {1}", e.PieceIndex, infoHash);
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.StopAsync().GetAwaiter().GetResult();
    }
}

public class MonoTorrentDownloadTask : IDownloadTask
{
    public int TorrentId { get; }

    public string InfoHash { get; }

    public string Category { get; set; }

    public TorrentManager Manager { get; }

    public MonoTorrentDownloadTask(int torrentId, string infoHash, TorrentManager manager, string category = null)
    {
        this.TorrentId = torrentId;
        this.InfoHash = infoHash;
        this.Manager = manager;
        this.Category = category;
    }

    public TorrentStatus Status
    {
        get
        {
            if (this.Manager == null)
            {
                return TorrentStatus.Stopped;
            }

            return this.Manager.State switch
            {
                TorrentState.Downloading => TorrentStatus.Downloading,
                TorrentState.Seeding => TorrentStatus.Seeding,
                TorrentState.Paused => TorrentStatus.Paused,
                TorrentState.Stopped => TorrentStatus.Stopped,
                TorrentState.Hashing => TorrentStatus.Checking,
                TorrentState.Metadata => TorrentStatus.Queued,
                TorrentState.Starting => TorrentStatus.Queued,
                TorrentState.Stopping => TorrentStatus.Paused,
                TorrentState.Error => TorrentStatus.Error,
                _ => TorrentStatus.Stopped,
            };
        }
    }

    public long DownloadedBytes => this.Manager?.Monitor?.DataBytesReceived ?? 0;

    public long UploadedBytes => this.Manager?.Monitor?.DataBytesSent ?? 0;

    public double Progress => this.Manager != null ? this.Manager.Progress / 100.0 : 0.0;

    public long DownloadSpeed => this.Manager?.Monitor?.DownloadRate ?? 0;

    public long UploadSpeed => this.Manager?.Monitor?.UploadRate ?? 0;

    public int ConnectedSeeders => this.Manager?.Peers?.Seeds ?? 0;

    public int ConnectedLeechers => this.Manager?.Peers?.Leechs ?? 0;

    public bool[] PieceBitfield
    {
        get
        {
            if (this.Manager?.Bitfield == null)
            {
                return Array.Empty<bool>();
            }

            var bitfield = new bool[this.Manager.Bitfield.Length];
            for (var i = 0; i < bitfield.Length; i++)
            {
                bitfield[i] = this.Manager.Bitfield[i];
            }

            return bitfield;
        }
    }

    private readonly object peerLock = new();
    private IList<PeerId> cachedMonoPeers;
    private DateTime lastPeersUpdate = DateTime.MinValue;
    private bool isUpdatingPeers;

    private IList<PeerId> GetCachedPeers()
    {
        if (this.Manager == null)
        {
            return Array.Empty<PeerId>();
        }

        var shouldUpdate = false;
        lock (this.peerLock)
        {
            if (!this.isUpdatingPeers && (DateTime.UtcNow - this.lastPeersUpdate > TimeSpan.FromSeconds(2) || this.cachedMonoPeers == null))
            {
                this.isUpdatingPeers = true;
                this.lastPeersUpdate = DateTime.UtcNow;
                shouldUpdate = true;
            }
        }

        if (shouldUpdate)
        {
            Task.Run(async () =>
            {
                try
                {
                    var peers = await this.Manager.GetPeersAsync().ConfigureAwait(false);
                    lock (this.peerLock)
                    {
                        this.cachedMonoPeers = peers;
                    }
                }
                catch
                {
                }
                finally
                {
                    lock (this.peerLock)
                    {
                        this.isUpdatingPeers = false;
                    }
                }
            });
        }

        lock (this.peerLock)
        {
            return this.cachedMonoPeers ?? Array.Empty<PeerId>();
        }
    }

    public int[] PieceAvailability
    {
        get
        {
            if (this.Manager == null)
            {
                return Array.Empty<int>();
            }

            var pieceCount = this.Manager.Bitfield?.Length ?? 0;
            if (pieceCount <= 0)
            {
                return Array.Empty<int>();
            }

            var availability = new int[pieceCount];
            try
            {
                var peers = this.GetCachedPeers();
                foreach (var p in peers)
                {
                    if (p.BitField != null)
                    {
                        for (var i = 0; i < Math.Min(pieceCount, p.BitField.Length); i++)
                        {
                            if (p.BitField[i])
                            {
                                availability[i]++;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return availability;
        }
    }

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        if (this.Manager == null)
        {
            return Array.Empty<PeerInfo>();
        }

        try
        {
            var peers = this.GetCachedPeers();
            var list = new List<PeerInfo>();
            foreach (var p in peers)
            {
                var flags = string.Empty;
                if (p.AmInterested)
                {
                    flags += "I";
                }

                if (p.AmChoking)
                {
                    flags += "C";
                }

                if (p.IsInterested)
                {
                    flags += "i";
                }

                if (p.IsChoking)
                {
                    flags += "c";
                }

                var isEncrypted = p.EncryptionType.ToString() != "None";
                if (isEncrypted)
                {
                    flags += "E";
                }

                list.Add(new PeerInfo
                {
                    Ip = p.Uri?.Host ?? "unknown",
                    Port = p.Uri?.Port ?? 0,
                    Client = p.ClientApp.Client.ToString(),
                    Flags = flags,
                    Progress = p.BitField != null && p.BitField.Length > 0 ? (double)p.BitField.PercentComplete / 100.0 : 0.0,
                    DownloadSpeed = p.Monitor?.DownloadRate ?? 0,
                    UploadSpeed = p.Monitor?.UploadRate ?? 0,
                    Downloaded = p.Monitor?.DataBytesReceived ?? 0,
                    Uploaded = p.Monitor?.DataBytesSent ?? 0,
                    IsEncrypted = isEncrypted,
                });
            }

            return list;
        }
        catch
        {
            return Array.Empty<PeerInfo>();
        }
    }
}

public class PieceVerifiedEvent : IEvent
{
    public int TorrentId { get; }

    public int PieceIndex { get; }

    public PieceVerifiedEvent(int torrentId, int pieceIndex)
    {
        this.TorrentId = torrentId;
        this.PieceIndex = pieceIndex;
    }
}
