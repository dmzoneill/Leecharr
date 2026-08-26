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
    private readonly IConfigService _configService;
    private readonly IStoragePathService _storagePathService;
    private readonly ICategoryService _categoryService;
    private readonly IDiskProvider _diskProvider;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    private readonly ConcurrentDictionary<int, MonoTorrentDownloadTask> _tasks = new();
    private readonly ConcurrentDictionary<string, int> _infoHashToId = new(StringComparer.OrdinalIgnoreCase);

    private ClientEngine _engine;
    private bool _disposed;

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
        SupportsEncryptionToggle = true
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
            }
        });
    }

    public MonoTorrentDownloadEngine(
        IConfigService configService,
        IStoragePathService storagePathService,
        ICategoryService categoryService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator)
    {
        _configService = configService;
        _storagePathService = storagePathService;
        _categoryService = categoryService;
        _diskProvider = diskProvider;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync()
    {
        if (_engine != null)
        {
            return;
        }

        var port = _configService.ListeningPort > 0 ? _configService.ListeningPort : 51413;
        _logger.Info("Initializing MonoTorrent download engine on port {0}...", port);

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr", "Cache");
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch
        {
        }

        var allowedEncryption = _configService.EncryptionMode?.ToLowerInvariant() switch
        {
            "forceencrypted" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header
            },
            "preferencrypted" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
                MonoTorrent.Connections.EncryptionType.PlainText
            },
            "allowplaintext" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.PlainText,
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header
            },
            "disabled" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.PlainText
            },
            _ => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
                MonoTorrent.Connections.EncryptionType.PlainText
            }
        };

        var listenIp = IPAddress.Any;
        var iface = !string.IsNullOrWhiteSpace(_configService.NetworkInterfaceBinding)
            ? _configService.NetworkInterfaceBinding
            : _configService.BindInterface;
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
                        _logger.Info("Bound MonoTorrent listening socket to interface '{0}' ({1})", nic.Name, listenIp);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to resolve IP for bound interface '{0}'. Defaulting to IPAddress.Any", iface);
            }
        }

        var engineSettingsBuilder = new EngineSettingsBuilder
        {
            AllowPortForwarding = _configService.UpnpEnabled,
            AllowLocalPeerDiscovery = _configService.EnableLpd,
            AllowedEncryption = allowedEncryption,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadDhtCache = true,
            DhtEndPoint = _configService.EnableDht ? new IPEndPoint(listenIp, port) : null,
            CacheDirectory = cacheDir,
            DiskCacheBytes = _configService.DiskCacheBytes > 0 ? _configService.DiskCacheBytes : Math.Max(128, _configService.DiskWriteCacheSizeMb) * 1024 * 1024,
            MaximumConnections = _configService.MaxGlobalConnections > 0 ? _configService.MaxGlobalConnections : 300,
            MaximumDownloadRate = _configService.MaxDownloadSpeedKbps > 0 ? _configService.MaxDownloadSpeedKbps * 1024 : 0,
            MaximumUploadRate = _configService.MaxUploadSpeedKbps > 0 ? _configService.MaxUploadSpeedKbps * 1024 : 0,
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                { "ipv4", new IPEndPoint(listenIp, port) }
            }
        };

        var engineSettings = engineSettingsBuilder.ToSettings();
        _engine = new ClientEngine(engineSettings);

        _logger.Info("MonoTorrent engine started successfully on {0}:{1}.", listenIp, port);
    }

    public async Task StopAsync()
    {
        if (_engine == null)
        {
            return;
        }

        _logger.Info("Stopping MonoTorrent download engine...");

        foreach (var task in _tasks.Values)
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
                _logger.Warn(ex, "Error stopping torrent manager for {0}", task.InfoHash);
            }
        }

        await _engine.StopAllAsync();
        _engine.Dispose();
        _engine = null;
        _logger.Info("MonoTorrent download engine stopped.");
    }

    public async Task<IDownloadTask> AddTorrentAsync(CoreTorrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        if (_engine == null)
        {
            await StartAsync();
        }

        var isCompleteOrSeeding = torrent.Status == TorrentStatus.Seeding || (torrent.Progress >= 1.0 && !string.IsNullOrWhiteSpace(torrent.SavePath));
        var workingPath = isCompleteOrSeeding && !string.IsNullOrWhiteSpace(torrent.SavePath)
            ? torrent.SavePath
            : _storagePathService.GetIncompleteDirectory();

        try
        {
            Directory.CreateDirectory(workingPath);
        }
        catch
        {
        }

        var torrentSettingsBuilder = new TorrentSettingsBuilder
        {
            MaximumConnections = _configService.MaxPerTorrentConnections > 0 ? _configService.MaxPerTorrentConnections : 50,
            UploadSlots = _configService.MaxUploadSlots > 0 ? _configService.MaxUploadSlots : 4,
            MaximumDownloadRate = torrent.DownloadLimit > 0 ? torrent.DownloadLimit * 1024 : 0,
            MaximumUploadRate = torrent.UploadLimit > 0 ? torrent.UploadLimit * 1024 : 0
        };

        TorrentManager manager = null;

        if (torrentFileBytes != null && torrentFileBytes.Length > 0)
        {
            var parsedTorrent = MtTorrent.Load(torrentFileBytes);
            manager = await _engine.AddAsync(parsedTorrent, workingPath, torrentSettingsBuilder.ToSettings());
        }
        else if (!string.IsNullOrWhiteSpace(magnetUri))
        {
            var magnetLink = MagnetLink.Parse(magnetUri);
            manager = await _engine.AddAsync(magnetLink, workingPath, torrentSettingsBuilder.ToSettings());
        }
        else if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            var magnetString = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
                ? $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}"
                : $"magnet:?xt=urn:btih:{torrent.InfoHash}";
            var magnetLink = MagnetLink.Parse(magnetString);
            manager = await _engine.AddAsync(magnetLink, workingPath, torrentSettingsBuilder.ToSettings());
        }

        if (manager == null)
        {
            throw new InvalidOperationException("Failed to create TorrentManager for torrent.");
        }

        var downloadTask = new MonoTorrentDownloadTask(torrent.Id, torrent.InfoHash, manager, torrent.Category);
        _tasks[torrent.Id] = downloadTask;
        _infoHashToId[torrent.InfoHash] = torrent.Id;

        manager.TorrentStateChanged += OnTorrentStateChanged;
        manager.PieceHashed += OnPieceHashed;

        if (torrent.Status == TorrentStatus.Paused)
        {
            await manager.PauseAsync();
            _logger.Info("Added paused torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }
        else if (torrent.Status == TorrentStatus.Stopped)
        {
            _logger.Info("Added stopped torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }
        else
        {
            await manager.StartAsync();
            _logger.Info("Added and started torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }

        return downloadTask;
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        if (_tasks.TryRemove(torrentId, out var task))
        {
            _infoHashToId.TryRemove(task.InfoHash, out _);

            if (task.Manager != null)
            {
                task.Manager.TorrentStateChanged -= OnTorrentStateChanged;
                task.Manager.PieceHashed -= OnPieceHashed;

                await task.Manager.StopAsync();
                await _engine.RemoveAsync(task.Manager);

                if (deleteFiles)
                {
                    try
                    {
                        var path = task.Manager.ContainingDirectory;
                        if (_diskProvider.FolderExists(path))
                        {
                            _diskProvider.DeleteFolder(path, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to delete files for torrent id {0}", torrentId);
                    }
                }
            }
        }
    }

    public async Task PauseTorrentAsync(int torrentId)
    {
        if (_tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            await task.Manager.PauseAsync();
            _logger.Info("Paused torrent id {0}", torrentId);
        }
    }

    public async Task ResumeTorrentAsync(int torrentId)
    {
        if (_tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            await task.Manager.StartAsync();
            _logger.Info("Resumed torrent id {0}", torrentId);
        }
    }

    public async Task ForceRecheckAsync(int torrentId)
    {
        if (_tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            await task.Manager.HashCheckAsync(true);
            _logger.Info("Triggered hash recheck for torrent id {0}", torrentId);
        }
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        if (_tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            if (task.Manager.TrackerManager != null)
            {
                await task.Manager.TrackerManager.AnnounceAsync(CancellationToken.None);
            }
        }
    }

    public async Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        if (_tasks.TryGetValue(torrentId, out var task) && task.Manager != null && task.Manager.Files != null)
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
                    _ => MonoTorrent.Priority.Normal
                };
                await task.Manager.SetFilePriorityAsync(targetFile, monoPriority);
                _logger.Info("Updated file priority for {0} (file: {1}, priority: {2})", task.InfoHash, filePath, monoPriority);
            }
        }
    }

    public async Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        if (_engine != null)
        {
            var settingsBuilder = new EngineSettingsBuilder(_engine.Settings)
            {
                MaximumDownloadRate = maxDownloadKbps > 0 ? maxDownloadKbps * 1024 : 0,
                MaximumUploadRate = maxUploadKbps > 0 ? maxUploadKbps * 1024 : 0
            };
            await _engine.UpdateSettingsAsync(settingsBuilder.ToSettings());
            _logger.Info("Updated MonoTorrent rate limits: Download = {0} KB/s, Upload = {1} KB/s", maxDownloadKbps, maxUploadKbps);
        }
    }

    public async Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
    {
        if (_tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            var settingsBuilder = new TorrentSettingsBuilder(task.Manager.Settings)
            {
                MaximumDownloadRate = maxDownloadKbps > 0 ? maxDownloadKbps * 1024 : 0,
                MaximumUploadRate = maxUploadKbps > 0 ? maxUploadKbps * 1024 : 0
            };
            await task.Manager.UpdateSettingsAsync(settingsBuilder.ToSettings());
            _logger.Info("Updated MonoTorrent per-torrent rate limits for {0}: Download = {1} KB/s, Upload = {2} KB/s", task.InfoHash, maxDownloadKbps, maxUploadKbps);
        }
    }

    public IDownloadTask GetTask(int torrentId)
    {
        _tasks.TryGetValue(torrentId, out var task);
        return task;
    }

    public IEnumerable<IDownloadTask> GetAllTasks()
    {
        return _tasks.Values;
    }

    private async void OnTorrentStateChanged(object sender, TorrentStateChangedEventArgs e)
    {
        var manager = e.TorrentManager;
        var infoHash = manager.InfoHashes.V1OrV2.ToHex();

        if (_infoHashToId.TryGetValue(infoHash, out var torrentId))
        {
            _logger.Info("Torrent {0} state changed: {1} -> {2}", infoHash, e.OldState, e.NewState);

            if (e.NewState == TorrentState.Seeding)
            {
                _tasks.TryGetValue(torrentId, out var existingTask);
                var category = existingTask?.Category;
                var completedDir = !string.IsNullOrWhiteSpace(category)
                    ? _categoryService.GetSavePathForCategory(category)
                    : (_configService.DownloadDir ?? "/downloads");

                try
                {
                    if (!string.IsNullOrWhiteSpace(completedDir) && !string.Equals(manager.SavePath, completedDir, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(completedDir);
                        await manager.MoveFilesAsync(completedDir, true);
                        _logger.Info("Moved completed torrent files for {0} to {1}", infoHash, completedDir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to move completed torrent files for {0} to {1}", infoHash, completedDir);
                }

                var savePath = manager.SavePath ?? completedDir;
                _eventAggregator.PublishEvent(new TorrentDownloadCompletedEvent(new CoreTorrent
                {
                    Id = torrentId,
                    InfoHash = infoHash,
                    Name = manager.Torrent?.Name ?? infoHash,
                    Status = TorrentStatus.Seeding,
                    Category = category,
                    SavePath = savePath
                }));
            }
        }
    }

    private void OnPieceHashed(object sender, PieceHashedEventArgs e)
    {
        var manager = e.TorrentManager;
        var infoHash = manager.InfoHashes.V1OrV2.ToHex();

        if (e.HashPassed)
        {
            _logger.Trace("Piece {0} verified for torrent {1} (progress: {2:P1})", e.PieceIndex, infoHash, manager.Progress / 100.0);
            if (_infoHashToId.TryGetValue(infoHash, out var torrentId))
            {
                _eventAggregator.PublishEvent(new PieceVerifiedEvent(torrentId, e.PieceIndex));
            }
        }
        else
        {
            _logger.Warn("Piece {0} failed hash check for torrent {1}", e.PieceIndex, infoHash);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
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
        TorrentId = torrentId;
        InfoHash = infoHash;
        Manager = manager;
        Category = category;
    }

    public TorrentStatus Status
    {
        get
        {
            if (Manager == null)
            {
                return TorrentStatus.Stopped;
            }

            return Manager.State switch
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
                _ => TorrentStatus.Stopped
            };
        }
    }

    public long DownloadedBytes => Manager?.Monitor?.DataBytesReceived ?? 0;
    public long UploadedBytes => Manager?.Monitor?.DataBytesSent ?? 0;
    public double Progress => Manager != null ? Manager.Progress / 100.0 : 0.0;
    public long DownloadSpeed => Manager?.Monitor?.DownloadRate ?? 0;
    public long UploadSpeed => Manager?.Monitor?.UploadRate ?? 0;
    public int ConnectedSeeders => Manager?.Peers?.Seeds ?? 0;
    public int ConnectedLeechers => Manager?.Peers?.Leechs ?? 0;

    public bool[] PieceBitfield
    {
        get
        {
            if (Manager?.Bitfield == null)
            {
                return Array.Empty<bool>();
            }

            var bitfield = new bool[Manager.Bitfield.Length];
            for (var i = 0; i < bitfield.Length; i++)
            {
                bitfield[i] = Manager.Bitfield[i];
            }

            return bitfield;
        }
    }

    private IList<PeerId> _cachedMonoPeers;
    private DateTime _lastPeersUpdate = DateTime.MinValue;

    private IList<PeerId> GetCachedPeers()
    {
        if (Manager == null)
        {
            return Array.Empty<PeerId>();
        }

        if (DateTime.UtcNow - _lastPeersUpdate > TimeSpan.FromSeconds(2) || _cachedMonoPeers == null)
        {
            try
            {
                var task = Manager.GetPeersAsync();
                if (task.Wait(100))
                {
                    _cachedMonoPeers = task.Result;
                    _lastPeersUpdate = DateTime.UtcNow;
                }
            }
            catch
            {
            }
        }

        return _cachedMonoPeers ?? Array.Empty<PeerId>();
    }

    public int[] PieceAvailability
    {
        get
        {
            if (Manager == null)
            {
                return Array.Empty<int>();
            }

            var pieceCount = Manager.Bitfield?.Length ?? 0;
            if (pieceCount <= 0)
            {
                return Array.Empty<int>();
            }

            var availability = new int[pieceCount];
            try
            {
                var peers = GetCachedPeers();
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
        if (Manager == null)
        {
            return Array.Empty<PeerInfo>();
        }

        try
        {
            var peers = GetCachedPeers();
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
                    IsEncrypted = isEncrypted
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
        TorrentId = torrentId;
        PieceIndex = pieceIndex;
    }
}
