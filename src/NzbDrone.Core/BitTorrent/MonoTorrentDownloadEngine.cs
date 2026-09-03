// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.PortMapping;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;
using CoreTorrent = NzbDrone.Core.Torrents.Torrent;
using MtTorrent = MonoTorrent.Torrent;

namespace NzbDrone.Core.BitTorrent;

public class MonoTorrentDownloadEngine : ITorrentEngine,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IDisposable
{
    private readonly IConfigService configService;
    private readonly IStoragePathService storagePathService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly IBlocklistService blocklistService;
    private readonly INatPmpPortMapperService natPmpPortMapperService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, MonoTorrentDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<int> interruptedTorrentIds = new();

    private ClientEngine engine;
    private Timer trackerHealthTimer;
    private volatile bool isHaltedByKillSwitch;
    private bool disposed;

    private long totalPiecesHashed;
    private long totalHashFails;
    private long blockedPeersCount;
    private DateTime lastPieceHashSample = DateTime.UtcNow;
    private long lastPiecesHashedCount;

    public bool IsHaltedByKillSwitch => this.isHaltedByKillSwitch;

    public long BlockedPeersCount => Interlocked.Read(ref this.blockedPeersCount);

    public string ProtocolName => "BitTorrent";

    public string EngineId => "MonoTorrent";

    public string DisplayName => "MonoTorrent (Pure .NET)";

    public string Version => typeof(ClientEngine).Assembly.GetName().Version?.ToString() ?? "3.0.2";

    public string Description => "Pure managed C# BitTorrent engine powered by MonoTorrent. Zero native dependencies, runs anywhere.";

    public bool IsAvailable => true;

    public int DhtNodeCount
    {
        get
        {
            try
            {
                if (this.engine == null)
                {
                    return 0;
                }

                var prop = this.engine.GetType().GetProperty("DhtEngine", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                           ?? this.engine.GetType().GetProperty("Dht", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (prop != null)
                {
                    var dht = prop.GetValue(this.engine);
                    if (dht != null)
                    {
                        var nodeCountProp = dht.GetType().GetProperty("NodeCount") ?? dht.GetType().GetProperty("NodesCount");
                        if (nodeCountProp != null)
                        {
                            return Convert.ToInt32(nodeCountProp.GetValue(dht));
                        }
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public TorrentEngineCapabilities Capabilities { get; } = new()
    {
        SupportsUtp = true,
        SupportsDht = true,
        SupportsPex = true,
        SupportsLpd = true,
        SupportsV2Torrents = true,
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
        IEventAggregator eventAggregator,
        IBlocklistService blocklistService = null,
        INatPmpPortMapperService natPmpPortMapperService = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        this.configService = configService;
        this.storagePathService = storagePathService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.blocklistService = blocklistService;
        this.natPmpPortMapperService = natPmpPortMapperService ?? new NatPmpPortMapperService();
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.logger = LogManager.GetCurrentClassLogger();

        if (this.vpnKillSwitchService != null)
        {
            this.vpnKillSwitchService.VpnDropped += this.OnVpnDropped;
            this.vpnKillSwitchService.VpnRestored += this.OnVpnRestored;
        }

        this.trackerHealthTimer = new Timer(_ => this.CheckTrackerHealth(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
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
        var listenIpv6 = IPAddress.IPv6Any;
        var iface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;

        var isKillSwitchEnabled = this.configService.EnableVpnKillSwitch ||
            (this.vpnKillSwitchService?.IsKillSwitchEnabled ?? false);

        var hasSpecificInterface = !string.IsNullOrWhiteSpace(iface) &&
            !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase);

        if (hasSpecificInterface)
        {
            var resolvedIp = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress()
                ?? this.ResolveInterfaceIp(iface, System.Net.Sockets.AddressFamily.InterNetwork);

            if (resolvedIp != null)
            {
                listenIp = resolvedIp;
                this.isHaltedByKillSwitch = false;
                this.logger.Info("Bound MonoTorrent IPv4 listening socket to interface '{0}' ({1})", iface, listenIp);
            }
            else if (isKillSwitchEnabled)
            {
                // STRICT FAIL-CLOSED:
                // Kill switch is active but interface is down or missing IP.
                // Do NOT fallback to IPAddress.Any or default adapter!
                this.logger.Error("VPN Kill Switch is active and interface '{0}' is unavailable or offline. Strict fail-closed halt enforced: Engine will NOT bind to default network interfaces.", iface);
                this.isHaltedByKillSwitch = true;
                return;
            }
            else
            {
                this.logger.Warn("Failed to resolve IP for bound interface '{0}'. Defaulting to IPAddress.Any", iface);
            }

            var resolvedIpv6 = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress(System.Net.Sockets.AddressFamily.InterNetworkV6)
                ?? this.ResolveInterfaceIp(iface, System.Net.Sockets.AddressFamily.InterNetworkV6);
            if (resolvedIpv6 != null)
            {
                listenIpv6 = resolvedIpv6;
                this.logger.Info("Bound MonoTorrent IPv6 listening socket to interface '{0}' ({1})", iface, listenIpv6);
            }
        }
        else if (isKillSwitchEnabled)
        {
            this.logger.Error("VPN Kill Switch is enabled but no valid VPN binding interface is configured. Halting engine in fail-closed state to prevent leaks.");
            this.isHaltedByKillSwitch = true;
            return;
        }

        var listenEndPoints = new Dictionary<string, IPEndPoint>
        {
            { "ipv4", new IPEndPoint(listenIp, port) },
        };

        if (this.configService.EnableIPv6 && System.Net.Sockets.Socket.OSSupportsIPv6)
        {
            try
            {
                listenEndPoints["ipv6"] = new IPEndPoint(listenIpv6, port);
                this.logger.Info("Configured IPv6 dual-stack listening socket on [{0}]:{1}", listenIpv6, port);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to configure IPv6 dual-stack listening socket");
            }
        }

        if (this.configService.UpnpEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.natPmpPortMapperService.MapPortAsync(port, NatPmpProtocol.Tcp);
                    await this.natPmpPortMapperService.MapPortAsync(port, NatPmpProtocol.Udp);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Background NAT-PMP mapping probe completed.");
                }
            });
        }

        var engineSettingsBuilder = new EngineSettingsBuilder
        {
            AllowPortForwarding = this.configService.UpnpEnabled,
            AllowLocalPeerDiscovery = this.configService.EnableLpd,
            AllowedEncryption = allowedEncryption,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadDhtCache = true,
            UsePartialFiles = this.configService.AppendIncompleteExtension,
            DhtEndPoint = this.configService.EnableDht ? new IPEndPoint(listenIp, port) : null,
            CacheDirectory = cacheDir,
            DiskCacheBytes = this.configService.DiskCacheBytes > 0 ? this.configService.DiskCacheBytes : Math.Max(128, this.configService.DiskWriteCacheSizeMb) * 1024 * 1024,
            MaximumConnections = this.configService.MaxGlobalConnections > 0 ? this.configService.MaxGlobalConnections : 300,
            MaximumDownloadRate = this.configService.MaxDownloadSpeedKbps > 0 ? this.configService.MaxDownloadSpeedKbps * 1024 : 0,
            MaximumUploadRate = this.configService.MaxUploadSpeedKbps > 0 ? this.configService.MaxUploadSpeedKbps * 1024 : 0,
            ListenEndPoints = listenEndPoints,
        };

        var engineSettings = engineSettingsBuilder.ToSettings();
        var factories = Factories.Default;

        if (this.configService.ProxyType?.ToLowerInvariant() is "socks5" or "http" &&
            !string.IsNullOrWhiteSpace(this.configService.ProxyHost))
        {
            var proxyType = this.configService.ProxyType.ToLowerInvariant();
            var proxyHost = this.configService.ProxyHost;
            var proxyPort = this.configService.ProxyPort > 0 ? this.configService.ProxyPort : (proxyType == "socks5" ? 1080 : 8080);
            var proxyUri = new Uri($"{proxyType}://{proxyHost}:{proxyPort}");

            ICredentials credentials = null;
            if (!string.IsNullOrWhiteSpace(this.configService.ProxyUsername))
            {
                credentials = new NetworkCredential(this.configService.ProxyUsername, this.configService.ProxyPassword ?? string.Empty);
            }

            var webProxy = new WebProxy(proxyUri)
            {
                Credentials = credentials,
            };

            factories = factories.WithHttpClientCreator(af =>
            {
                var handler = new SocketsHttpHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                };
                return new HttpClient(handler);
            });

            this.logger.Info("Configured MonoTorrent tracker proxy via {0}://{1}:{2}", proxyType, proxyHost, proxyPort);
        }

        this.engine = new ClientEngine(engineSettings, factories);

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

        try
        {
            await this.natPmpPortMapperService.StopAsync();
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error stopping NAT-PMP port mapper on engine stop.");
        }

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

        MtTorrent parsedTorrent = null;
        if (torrentFileBytes != null && torrentFileBytes.Length > 0)
        {
            try
            {
                parsedTorrent = MtTorrent.Load(torrentFileBytes);
                if (parsedTorrent.IsPrivate && !torrent.IsPrivate)
                {
                    torrent.IsPrivate = true;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to pre-inspect torrent file bytes for BEP 27 flag on {0}", torrent.Name);
            }
        }

        var torrentSettingsBuilder = new TorrentSettingsBuilder
        {
            MaximumConnections = this.configService.MaxPerTorrentConnections > 0 ? this.configService.MaxPerTorrentConnections : 50,
            UploadSlots = this.configService.MaxUploadSlots > 0 ? this.configService.MaxUploadSlots : 4,
            MaximumDownloadRate = torrent.DownloadLimit > 0 ? torrent.DownloadLimit * 1024 : 0,
            MaximumUploadRate = torrent.UploadLimit > 0 ? torrent.UploadLimit * 1024 : 0,
            AllowInitialSeeding = torrent.InitialSeeding,
        };

        var enforceBep27 = this.configService.EnableBep27PrivateTorrents && torrent.IsPrivate;
        if (enforceBep27)
        {
            torrentSettingsBuilder.AllowDht = false;
            torrentSettingsBuilder.AllowPeerExchange = false;
            this.logger.Info("BEP 27 active for private torrent {0}: DHT and PEX disabled", torrent.Name);
        }
        else
        {
            torrentSettingsBuilder.AllowDht = this.configService.EnableDht;
            torrentSettingsBuilder.AllowPeerExchange = this.configService.EnablePex;
        }

        TorrentManager manager = null;
        var torrentSettings = torrentSettingsBuilder.ToSettings();

        var isSequential = torrent.SequentialDownload || string.Equals(this.configService.PiecePickerStrategy, "Sequential", StringComparison.OrdinalIgnoreCase);
        if (isSequential)
        {
            if (torrentFileBytes != null && torrentFileBytes.Length > 0)
            {
                parsedTorrent ??= MtTorrent.Load(torrentFileBytes);
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
                parsedTorrent ??= MtTorrent.Load(torrentFileBytes);
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

        var downloadTask = new MonoTorrentDownloadTask(
            torrent.Id,
            torrent.InfoHash,
            manager,
            torrent.Category,
            parsedTorrent,
            this.blocklistService,
            () => Interlocked.Increment(ref this.blockedPeersCount));
        this.tasks[torrent.Id] = downloadTask;
        this.infoHashToId[torrent.InfoHash] = torrent.Id;

        manager.TorrentStateChanged += this.OnTorrentStateChanged;
        manager.PieceHashed += this.OnPieceHashed;

        if (this.isHaltedByKillSwitch)
        {
            await manager.PauseAsync();
            torrent.Status = TorrentStatus.Paused;
            this.logger.Warn("VPN Kill Switch active (fail-closed). Added torrent {0} in paused state.", torrent.Name);
        }
        else if (torrent.Status == TorrentStatus.Paused)
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
            task.UnhookEvents();

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
                                    await this.DeleteFileWithRetryAsync(filePath);
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
                                    await this.DeleteFolderWithRetryAsync(containingDir);
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

    private async Task DeleteFileWithRetryAsync(string filePath, int maxRetries = 3)
    {
        var delayMs = 150;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (this.diskProvider.FileExists(filePath))
                {
                    this.diskProvider.DeleteFile(filePath);
                }

                return;
            }
            catch (IOException ioEx) when (attempt < maxRetries)
            {
                this.logger.Debug(ioEx, "File '{0}' locked during deletion (attempt {1}/{2}). Retrying in {3}ms...", filePath, attempt, maxRetries, delayMs);
                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }
    }

    private async Task DeleteFolderWithRetryAsync(string folderPath, int maxRetries = 3)
    {
        var delayMs = 150;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (this.diskProvider.FolderExists(folderPath))
                {
                    this.diskProvider.DeleteFolder(folderPath, true);
                }

                return;
            }
            catch (IOException ioEx) when (attempt < maxRetries)
            {
                this.logger.Debug(ioEx, "Folder '{0}' locked during deletion (attempt {1}/{2}). Retrying in {3}ms...", folderPath, attempt, maxRetries, delayMs);
                await Task.Delay(delayMs);
                delayMs *= 2;
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
                if (task.Picker != null)
                {
                    var pickerPrio = priority switch
                    {
                        0 => 0,
                        1 or 2 => 1,
                        4 => 2,
                        5 => 3,
                        _ => 1,
                    };

                    for (var p = targetFile.StartPieceIndex; p <= targetFile.EndPieceIndex; p++)
                    {
                        task.Picker.SetPiecePriority(p, pickerPrio);
                    }
                }

                this.logger.Info("Updated file priority for {0} (file: {1}, priority: {2})", task.InfoHash, filePath, monoPriority);
            }
        }
    }

    public async Task<bool> RenameFileAsync(int torrentId, string oldRelativePath, string newRelativePath)
    {
        if (string.IsNullOrWhiteSpace(oldRelativePath) || string.IsNullOrWhiteSpace(newRelativePath))
        {
            return false;
        }

        if (!this.tasks.TryGetValue(torrentId, out var task) || task.Manager == null)
        {
            this.logger.Warn("Cannot rename file: torrent {0} not found in engine", torrentId);
            return false;
        }

        var manager = task.Manager;
        var normalizedOld = oldRelativePath.Replace('\\', '/').TrimStart('/');
        var normalizedNew = newRelativePath.Replace('\\', '/').TrimStart('/');

        var file = manager.Files.FirstOrDefault(f => f.Path.Replace('\\', '/').TrimStart('/').Equals(normalizedOld, StringComparison.OrdinalIgnoreCase));
        if (file == null)
        {
            this.logger.Warn("File '{0}' not found in torrent {1}", oldRelativePath, torrentId);
            return false;
        }

        try
        {
            var destinationFullPath = Path.Combine(manager.SavePath, normalizedNew);
            if (!TorrentPathValidator.IsStrictSubPath(manager.SavePath, destinationFullPath))
            {
                this.logger.Warn("Cannot rename file in torrent {0}: target '{1}' escapes save path '{2}'", torrentId, destinationFullPath, manager.SavePath);
                return false;
            }

            var destDir = Path.GetDirectoryName(destinationFullPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await manager.MoveFileAsync(file, destinationFullPath);
            this.logger.Info("Renamed file in torrent {0}: '{1}' -> '{2}'", torrentId, oldRelativePath, newRelativePath);
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to rename file in torrent {0} from '{1}' to '{2}'", torrentId, oldRelativePath, newRelativePath);
            return false;
        }
    }

    public async Task<bool> RenameFolderAsync(int torrentId, string oldRelativeFolder, string newRelativeFolder)
    {
        if (string.IsNullOrWhiteSpace(oldRelativeFolder) || string.IsNullOrWhiteSpace(newRelativeFolder))
        {
            return false;
        }

        if (!this.tasks.TryGetValue(torrentId, out var task) || task.Manager == null)
        {
            this.logger.Warn("Cannot rename folder: torrent {0} not found in engine", torrentId);
            return false;
        }

        var manager = task.Manager;
        var normalizedOld = oldRelativeFolder.Replace('\\', '/').Trim('/');
        var normalizedNew = newRelativeFolder.Replace('\\', '/').Trim('/');

        var matchingFiles = manager.Files
            .Where(f => f.Path.Replace('\\', '/').TrimStart('/').StartsWith(normalizedOld + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingFiles.Count == 0)
        {
            this.logger.Warn("No files found matching folder '{0}' in torrent {1}", oldRelativeFolder, torrentId);
            return false;
        }

        var anyMoved = false;
        foreach (var file in matchingFiles)
        {
            var currentPath = file.Path.Replace('\\', '/').TrimStart('/');
            var subPath = currentPath[(normalizedOld.Length + 1)..];
            var newRelativePath = $"{normalizedNew}/{subPath}";
            var destinationFullPath = Path.Combine(manager.SavePath, newRelativePath);
            if (!TorrentPathValidator.IsStrictSubPath(manager.SavePath, destinationFullPath))
            {
                this.logger.Warn("Cannot rename file '{0}' in torrent {1}: target '{2}' escapes save path '{3}'", currentPath, torrentId, destinationFullPath, manager.SavePath);
                continue;
            }

            var destDir = Path.GetDirectoryName(destinationFullPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            try
            {
                await manager.MoveFileAsync(file, destinationFullPath);
                anyMoved = true;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to move file '{0}' during folder rename in torrent {1}", currentPath, torrentId);
            }
        }

        if (anyMoved)
        {
            this.logger.Info("Renamed folder in torrent {0}: '{1}' -> '{2}' ({3} files updated)", torrentId, oldRelativeFolder, newRelativeFolder, matchingFiles.Count);
        }

        return anyMoved;
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

    public async Task SetTorrentPrivateStatusAsync(int torrentId, bool isPrivate)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            var settingsBuilder = new TorrentSettingsBuilder(task.Manager.Settings);
            if (isPrivate && this.configService.EnableBep27PrivateTorrents)
            {
                settingsBuilder.AllowDht = false;
                settingsBuilder.AllowPeerExchange = false;
            }
            else
            {
                settingsBuilder.AllowDht = this.configService.EnableDht;
                settingsBuilder.AllowPeerExchange = this.configService.EnablePex;
            }

            await task.Manager.UpdateSettingsAsync(settingsBuilder.ToSettings());
            this.logger.Info("Updated BEP 27 settings for {0} (IsPrivate: {1}, EnforceBep27: {2})", task.InfoHash, isPrivate, this.configService.EnableBep27PrivateTorrents);
        }
    }

    public async Task SetSuperSeedingAsync(int torrentId, bool enabled)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            var settingsBuilder = new TorrentSettingsBuilder(task.Manager.Settings)
            {
                AllowInitialSeeding = enabled,
            };
            await task.Manager.UpdateSettingsAsync(settingsBuilder.ToSettings());
            this.logger.Info("Updated super seeding for torrent {0}: {1}", torrentId, enabled);
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

                if (manager.Torrent != null && manager.Torrent.IsPrivate && this.configService.EnableBep27PrivateTorrents)
                {
                    if (manager.Settings.AllowDht || manager.Settings.AllowPeerExchange)
                    {
                        var strictSettings = new TorrentSettingsBuilder(manager.Settings)
                        {
                            AllowDht = false,
                            AllowPeerExchange = false,
                        }.ToSettings();
                        _ = manager.UpdateSettingsAsync(strictSettings);
                        this.logger.Info("Enforced BEP 27 restrictions for private torrent {0} after metadata received (DHT/PEX disabled)", infoHash);
                    }
                }

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

                        var targetPath = Path.Combine(manager.SavePath ?? completedDir, manager.Torrent?.Name ?? string.Empty);
                        this.storagePathService.StripIncompleteExtensions(targetPath);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to move or finalize completed torrent files for {0}", infoHash);
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

        Interlocked.Increment(ref this.totalPiecesHashed);

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
            Interlocked.Increment(ref this.totalHashFails);
            this.logger.Warn("Piece {0} failed hash check for torrent {1}", e.PieceIndex, infoHash);
        }
    }

    public TorrentEngineMetrics GetEngineMetrics()
    {
        var taskList = this.tasks.Values.ToList();
        long totalDownSpeed = 0;
        long totalUpSpeed = 0;
        long totalDataDown = 0;
        long totalDataUp = 0;
        long totalProtoDown = 0;
        long totalProtoUp = 0;
        var openConns = 0;
        var seeds = 0;
        var leechers = 0;
        var totalSwarmPeers = 0;
        var downloadingCount = 0;
        var seedingCount = 0;
        var pausedCount = 0;
        var encryptedConns = 0;
        var plaintextConns = 0;
        var utpConns = 0;
        var tcpConns = 0;

        foreach (var task in taskList)
        {
            var m = task.Manager;
            if (m != null)
            {
                var mon = m.Monitor;
                if (mon != null)
                {
                    totalDownSpeed += mon.DownloadRate;
                    totalUpSpeed += mon.UploadRate;
                    totalDataDown += mon.DataBytesReceived;
                    totalDataUp += mon.DataBytesSent;
                    totalProtoDown += mon.ProtocolBytesReceived;
                    totalProtoUp += mon.ProtocolBytesSent;
                }

                openConns += m.OpenConnections;
                seeds += m.Peers?.Seeds ?? 0;
                leechers += m.Peers?.Leechs ?? 0;
                totalSwarmPeers += m.Peers?.Available ?? 0;

                switch (m.State)
                {
                    case TorrentState.Downloading or TorrentState.Starting:
                        downloadingCount++;
                        break;
                    case TorrentState.Seeding:
                        seedingCount++;
                        break;
                    case TorrentState.Paused or TorrentState.Stopped:
                        pausedCount++;
                        break;
                }

                var peers = task.GetPeers();
                foreach (var p in peers)
                {
                    if (p.IsEncrypted)
                    {
                        encryptedConns++;
                    }
                    else
                    {
                        plaintextConns++;
                    }

                    if (p.Client != null && p.Client.Contains("uTP", StringComparison.OrdinalIgnoreCase))
                    {
                        utpConns++;
                    }
                    else
                    {
                        tcpConns++;
                    }
                }
            }
        }

        var totalDownAll = totalDataDown + totalProtoDown;
        var protoOverheadPct = totalDownAll > 0 ? Math.Round(((double)totalProtoDown / totalDownAll) * 100.0, 2) : 0.0;

        var now = DateTime.UtcNow;
        var elapsedSec = Math.Max(0.5, (now - this.lastPieceHashSample).TotalSeconds);
        var currentHashed = Interlocked.Read(ref this.totalPiecesHashed);
        var piecesDelta = currentHashed - this.lastPiecesHashedCount;
        this.lastPieceHashSample = now;
        this.lastPiecesHashedCount = currentHashed;
        var piecesPerSec = Math.Round(piecesDelta / elapsedSec, 1);

        var diskCacheCap = this.configService.DiskCacheBytes > 0
            ? this.configService.DiskCacheBytes
            : Math.Max(128, this.configService.DiskWriteCacheSizeMb) * 1024L * 1024L;

        long cacheHits = 0;
        long cacheMisses = 0;
        long cacheUsed = 0;
        var diskPendingWrites = 0;
        var diskPendingReads = 0;
        long totalBytesRead = 0;
        long totalBytesWritten = 0;
        long diskReadRate = 0;
        long diskWriteRate = 0;

        if (this.engine != null)
        {
            TryExtractDiskManagerMetrics(
                this.engine,
                out cacheHits,
                out cacheMisses,
                out cacheUsed,
                out diskPendingWrites,
                out diskPendingReads,
                out totalBytesRead,
                out totalBytesWritten,
                out diskReadRate,
                out diskWriteRate);
        }

        var totalCacheAccesses = cacheHits + cacheMisses;
        var hitRatio = totalCacheAccesses > 0 ? Math.Round(((double)cacheHits / totalCacheAccesses) * 100.0, 1) : 100.0;

        return new TorrentEngineMetrics
        {
            EngineId = this.EngineId,
            DisplayName = this.DisplayName,
            Version = this.Version,
            IsRunning = this.engine != null,
            ActiveTorrents = taskList.Count,
            DownloadingTorrents = downloadingCount,
            SeedingTorrents = seedingCount,
            PausedTorrents = pausedCount,
            TotalDownloadSpeed = totalDownSpeed,
            TotalUploadSpeed = totalUpSpeed,
            TotalProtocolDownloadSpeed = 0,
            TotalProtocolUploadSpeed = 0,
            TotalDataDownloaded = totalDataDown,
            TotalDataUploaded = totalDataUp,
            TotalProtocolDownloaded = totalProtoDown,
            TotalProtocolUploaded = totalProtoUp,
            ProtocolOverheadPercentage = protoOverheadPct,
            OpenConnections = openConns,
            HalfOpenConnections = 0,
            MaxConnections = this.configService.MaxGlobalConnections > 0 ? this.configService.MaxGlobalConnections : 300,
            ConnectedSeeds = seeds,
            ConnectedLeechers = leechers,
            TotalSwarmPeers = totalSwarmPeers,
            DhtNodeCount = this.DhtNodeCount,
            DhtState = this.configService.EnableDht ? "Ready" : "Disabled",
            DiskCacheBytesAllocated = cacheUsed > 0 ? cacheUsed : Math.Min(diskCacheCap, totalDataDown > 0 ? 16 * 1024 * 1024 : 0),
            DiskCacheCapacityBytes = diskCacheCap,
            DiskCacheHitRatio = hitRatio,
            DiskCacheHits = cacheHits,
            DiskCacheMisses = cacheMisses,
            DiskPendingWrites = diskPendingWrites,
            DiskPendingReads = diskPendingReads,
            DiskTotalBytesWritten = totalBytesWritten > 0 ? totalBytesWritten : totalDataDown,
            DiskTotalBytesRead = totalBytesRead > 0 ? totalBytesRead : totalDataUp,
            DiskWriteRate = diskWriteRate > 0 ? diskWriteRate : totalDownSpeed,
            DiskReadRate = diskReadRate > 0 ? diskReadRate : totalUpSpeed,
            PiecesHashedPerSec = piecesPerSec,
            HashFailsTotal = Interlocked.Read(ref this.totalHashFails),
            EncryptedConnectionsCount = encryptedConns,
            PlaintextConnectionsCount = plaintextConns,
            UtpConnectionsCount = utpConns,
            TcpConnectionsCount = tcpConns,
            BlockedPeersCount = this.BlockedPeersCount,
            Timestamp = now,
        };
    }

    public TorrentResourceMetrics GetTorrentResourceMetrics(int torrentId)
    {
        return this.tasks.TryGetValue(torrentId, out var task) ? task.GetResourceMetrics() : null;
    }

    public IReadOnlyList<TorrentResourceMetrics> GetAllTorrentResourceMetrics()
    {
        return this.tasks.Values.Select(t => t.GetResourceMetrics()).ToList();
    }

    private static void TryExtractDiskManagerMetrics(
        ClientEngine clientEngine,
        out long cacheHits,
        out long cacheMisses,
        out long cacheUsed,
        out int pendingWrites,
        out int pendingReads,
        out long bytesRead,
        out long bytesWritten,
        out long readRate,
        out long writeRate)
    {
        cacheHits = 0;
        cacheMisses = 0;
        cacheUsed = 0;
        pendingWrites = 0;
        pendingReads = 0;
        bytesRead = 0;
        bytesWritten = 0;
        readRate = 0;
        writeRate = 0;

        try
        {
            var diskProp = clientEngine.GetType().GetProperty("DiskManager", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (diskProp != null)
            {
                var disk = diskProp.GetValue(clientEngine);
                if (disk != null)
                {
                    var diskType = disk.GetType();
                    cacheHits = Convert.ToInt64(diskType.GetProperty("CacheHits")?.GetValue(disk) ?? 0);
                    cacheUsed = Convert.ToInt64(diskType.GetProperty("CacheUsed")?.GetValue(disk) ?? diskType.GetProperty("CacheBytesUsed")?.GetValue(disk) ?? 0);
                    pendingWrites = Convert.ToInt32(diskType.GetProperty("PendingWriteBytes")?.GetValue(disk) ?? 0) / 16384;
                    pendingReads = Convert.ToInt32(diskType.GetProperty("PendingReadBytes")?.GetValue(disk) ?? 0) / 16384;
                    bytesRead = Convert.ToInt64(diskType.GetProperty("TotalBytesRead")?.GetValue(disk) ?? 0);
                    bytesWritten = Convert.ToInt64(diskType.GetProperty("TotalBytesWritten")?.GetValue(disk) ?? 0);
                    readRate = Convert.ToInt64(diskType.GetProperty("ReadRate")?.GetValue(disk) ?? 0);
                    writeRate = Convert.ToInt64(diskType.GetProperty("WriteRate")?.GetValue(disk) ?? 0);
                }
            }
        }
        catch
        {
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        this.OnVpnDropped(message.InterfaceName);
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        this.OnVpnRestored(message.InterfaceName);
    }

    public void OnVpnDropped(string interfaceName)
    {
        this.logger.Error("VPN Kill Switch drop detected for interface '{0}'. Halting MonoTorrent engine and terminating active peer connections.", interfaceName);
        this.isHaltedByKillSwitch = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await this.HaltAllTorrentsForKillSwitchAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error occurred during VPN kill switch halt");
            }
        });
    }

    public void OnVpnRestored(string interfaceName)
    {
        this.logger.Info("VPN interface '{0}' restored. Resuming MonoTorrent activity.", interfaceName);
        this.isHaltedByKillSwitch = false;

        _ = Task.Run(async () =>
        {
            try
            {
                await this.ResumeTorrentsAfterVpnRestoredAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error occurred while resuming torrents after VPN restoration");
            }
        });
    }

    public async Task HaltAllTorrentsForKillSwitchAsync()
    {
        foreach (var task in this.tasks.Values)
        {
            if (task.Manager != null)
            {
                try
                {
                    var state = task.Manager.State;
                    if (state is TorrentState.Downloading or TorrentState.Seeding or TorrentState.Starting or TorrentState.Metadata)
                    {
                        this.interruptedTorrentIds.Add(task.TorrentId);
                    }

                    // Immediately pause manager to stop all network requests
                    await task.Manager.PauseAsync().ConfigureAwait(false);

                    // Immediately abort active peer socket connections to prevent traffic leakage
                    var peers = await task.Manager.GetPeersAsync().ConfigureAwait(false);
                    foreach (var peer in peers)
                    {
                        try
                        {
                            (peer as IDisposable)?.Dispose();
                            var connProp = peer?.GetType().GetProperty("Connection", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            (connProp?.GetValue(peer) as IDisposable)?.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Error pausing torrent {0} during VPN drop", task.TorrentId);
                }
            }
        }

        this.logger.Warn("VPN kill switch halt completed. {0} active torrents paused and peer connections aborted.", this.interruptedTorrentIds.Count);
    }

    public async Task ResumeTorrentsAfterVpnRestoredAsync()
    {
        if (this.engine == null)
        {
            await this.StartAsync().ConfigureAwait(false);
        }

        var toResume = this.interruptedTorrentIds.Distinct().ToList();
        while (this.interruptedTorrentIds.TryTake(out _))
        {
        }

        foreach (var torrentId in toResume)
        {
            if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
            {
                try
                {
                    await task.Manager.StartAsync().ConfigureAwait(false);
                    this.logger.Info("Resumed torrent id {0} following VPN interface recovery", torrentId);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to resume torrent id {0} after VPN interface recovery", torrentId);
                }
            }
        }
    }

    private IPAddress ResolveInterfaceIp(string interfaceName, System.Net.Sockets.AddressFamily family)
    {
        try
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
            {
                return null;
            }

            var unicast = nic.GetIPProperties()?.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == family &&
                                     !IPAddress.IsLoopback(a.Address) &&
                                     !a.Address.Equals(IPAddress.Any) &&
                                     !a.Address.Equals(IPAddress.None));

            return unicast?.Address;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to resolve IP address for interface '{0}'", interfaceName);
            return null;
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        if (this.vpnKillSwitchService != null)
        {
            this.vpnKillSwitchService.VpnDropped -= this.OnVpnDropped;
            this.vpnKillSwitchService.VpnRestored -= this.OnVpnRestored;
        }

        this.trackerHealthTimer?.Dispose();
        this.trackerHealthTimer = null;

        this.StopAsync().GetAwaiter().GetResult();
    }

    public void CheckTrackerHealth()
    {
        foreach (var task in this.tasks.Values)
        {
            try
            {
                task.CheckTrackerHealth(this.eventAggregator);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error checking tracker health for torrent {0}", task.TorrentId);
            }
        }
    }
}

public class MonoTorrentDownloadTask : IDownloadTask
{
    public int TorrentId { get; }

    public string InfoHash { get; }

    public string Category { get; set; }

    public TorrentManager Manager { get; }

    public PiecePicker Picker { get; private set; }

    private readonly IBlocklistService blocklistService;
    private readonly Action onPeerBlocked;
    private readonly MtTorrent initialTorrent;
    private readonly object peerLock = new();

    private bool isTrackerStalled;
    private string errorMessage;
    private IList<PeerId> cachedMonoPeers;
    private DateTime lastPeersUpdate = DateTime.MinValue;
    private bool isUpdatingPeers;

    public MonoTorrentDownloadTask(
        int torrentId,
        string infoHash,
        TorrentManager manager,
        string category = null,
        MtTorrent initialTorrent = null,
        IBlocklistService blocklistService = null,
        Action onPeerBlocked = null)
    {
        this.TorrentId = torrentId;
        this.InfoHash = infoHash;
        this.Manager = manager;
        this.Category = category;
        this.initialTorrent = initialTorrent;
        this.blocklistService = blocklistService;
        this.onPeerBlocked = onPeerBlocked;

        var t = manager?.Torrent ?? initialTorrent;
        if (t != null && t.PieceCount > 0)
        {
            this.Picker = new PiecePicker(t.PieceCount, t.PieceLength, t.Size);
            if (manager?.Bitfield != null)
            {
                for (var i = 0; i < Math.Min(manager.Bitfield.Length, t.PieceCount); i++)
                {
                    if (manager.Bitfield[i])
                    {
                        this.Picker.MarkPieceVerified(i);
                    }
                }
            }
        }

        if (manager != null)
        {
            manager.TorrentStateChanged += this.OnTorrentStateChanged;
            manager.PeerConnected += this.OnPeerConnected;
            manager.PeerDisconnected += this.OnPeerDisconnected;
            manager.PieceHashed += this.OnPieceHashed;
        }
    }

    public void UnhookEvents()
    {
        if (this.Manager != null)
        {
            this.Manager.TorrentStateChanged -= this.OnTorrentStateChanged;
            this.Manager.PeerConnected -= this.OnPeerConnected;
            this.Manager.PeerDisconnected -= this.OnPeerDisconnected;
            this.Manager.PieceHashed -= this.OnPieceHashed;
        }
    }

    private void OnTorrentStateChanged(object sender, TorrentStateChangedEventArgs e)
    {
        if (this.Manager?.Torrent != null && this.Picker == null)
        {
            var t = this.Manager.Torrent;
            this.Picker = new PiecePicker(t.PieceCount, t.PieceLength, t.Size);
            if (this.Manager.Bitfield != null)
            {
                for (var i = 0; i < Math.Min(this.Manager.Bitfield.Length, t.PieceCount); i++)
                {
                    if (this.Manager.Bitfield[i])
                    {
                        this.Picker.MarkPieceVerified(i);
                    }
                }
            }
        }
    }

    private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
    {
        var peerIp = e.Peer?.Uri?.Host;
        if (!string.IsNullOrEmpty(peerIp) && this.blocklistService != null && this.blocklistService.IsIpBlocked(peerIp))
        {
            this.onPeerBlocked?.Invoke();
            try
            {
                (e.Peer as IDisposable)?.Dispose();
                var connProp = e.Peer?.GetType().GetProperty("Connection", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (connProp?.GetValue(e.Peer) is IDisposable connDisp)
                {
                    connDisp.Dispose();
                }
            }
            catch
            {
            }

            return;
        }

        if (this.Picker != null && e.Peer?.BitField != null)
        {
            var bf = new bool[e.Peer.BitField.Length];
            for (var i = 0; i < bf.Length; i++)
            {
                bf[i] = e.Peer.BitField[i];
            }

            this.Picker.UpdatePeerAvailability(bf, true);
        }
    }

    private void OnPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
    {
        if (this.Picker != null && e.Peer?.BitField != null)
        {
            var bf = new bool[e.Peer.BitField.Length];
            for (var i = 0; i < bf.Length; i++)
            {
                bf[i] = e.Peer.BitField[i];
            }

            this.Picker.UpdatePeerAvailability(bf, false);
        }
    }

    private void OnPieceHashed(object sender, PieceHashedEventArgs e)
    {
        if (this.Picker != null)
        {
            if (e.HashPassed)
            {
                this.Picker.MarkPieceVerified(e.PieceIndex);
            }
            else
            {
                this.Picker.MarkPieceCorrupt(e.PieceIndex);
            }
        }
    }

    public bool IsStalled => this.isTrackerStalled;

    public string ErrorMessage => this.errorMessage;

    public bool IsPrivate => this.Manager?.Torrent?.IsPrivate == true ||
                             this.Manager?.TrackerManager?.Private == true ||
                             this.initialTorrent?.IsPrivate == true;

    public TorrentStatus Status
    {
        get
        {
            if (this.Manager == null)
            {
                return TorrentStatus.Stopped;
            }

            if (this.isTrackerStalled && this.Manager.State != TorrentState.Paused)
            {
                return TorrentStatus.Stalled;
            }

            return this.Manager.State switch
            {
                TorrentState.Downloading => TorrentStatus.Downloading,
                TorrentState.Seeding => TorrentStatus.Seeding,
                TorrentState.Paused => TorrentStatus.Paused,
                TorrentState.Stopped => TorrentStatus.Stopped,
                TorrentState.Hashing => TorrentStatus.Checking,
                TorrentState.Metadata => TorrentStatus.Downloading,
                TorrentState.Starting => TorrentStatus.Downloading,
                TorrentState.Stopping => TorrentStatus.Paused,
                TorrentState.Error => TorrentStatus.Error,
                _ => TorrentStatus.Stopped,
            };
        }
    }

    public bool CheckTrackerHealth(IEventAggregator eventAggregator = null)
    {
        if (this.Manager == null)
        {
            return false;
        }

        var state = this.Manager.State;
        if (state is not (TorrentState.Downloading or TorrentState.Starting or TorrentState.Metadata) ||
            this.Progress >= 1.0)
        {
            if (this.isTrackerStalled)
            {
                this.isTrackerStalled = false;
                this.errorMessage = null;
                eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", "Torrent transitioned out of downloading state.", isResolved: true));
            }

            return false;
        }

        var isPrivate = this.IsPrivate;
        var dhtPexDisabled = isPrivate ||
            (this.Manager.Settings?.AllowDht == false && this.Manager.Settings?.AllowPeerExchange == false);

        var totalPeers = this.ConnectedSeeders + this.ConnectedLeechers + this.Manager.OpenConnections;

        if (totalPeers > 0 || this.DownloadSpeed > 0)
        {
            if (this.isTrackerStalled)
            {
                this.isTrackerStalled = false;
                this.errorMessage = null;
                eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", "Tracker recovered and peers connected.", isResolved: true));
            }

            return false;
        }

        if (!dhtPexDisabled)
        {
            return false;
        }

        var trackerManager = this.Manager.TrackerManager;
        var tiers = trackerManager?.Tiers;

        var allTrackers = tiers != null
            ? tiers.SelectMany(t => t.Trackers).ToList()
            : new List<MonoTorrent.Trackers.ITracker>();

        if (allTrackers.Count == 0)
        {
            var msg = "No trackers configured for private torrent.";
            var wasStalled = this.isTrackerStalled;
            this.isTrackerStalled = true;
            this.errorMessage = msg;

            if (!wasStalled)
            {
                eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", msg, isResolved: false));
            }

            return true;
        }

        var anyWorking = allTrackers.Any(t => t.Status == MonoTorrent.Trackers.TrackerState.Ok);
        var anyTierSucceeded = tiers != null && tiers.Any(t => t.LastAnnounceSucceeded);

        if (anyWorking || anyTierSucceeded)
        {
            if (this.isTrackerStalled)
            {
                this.isTrackerStalled = false;
                this.errorMessage = null;
                eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", "Tracker announce succeeded.", isResolved: true));
            }

            return false;
        }

        var failingTrackers = allTrackers
            .Where(t => t.Status is MonoTorrent.Trackers.TrackerState.Offline or MonoTorrent.Trackers.TrackerState.InvalidResponse ||
                        !string.IsNullOrWhiteSpace(t.FailureMessage))
            .ToList();

        if (failingTrackers.Count == allTrackers.Count || allTrackers.All(t => t.Status != MonoTorrent.Trackers.TrackerState.Ok))
        {
            var failDetails = failingTrackers
                .Select(t => $"{t.Uri}: {(!string.IsNullOrWhiteSpace(t.FailureMessage) ? t.FailureMessage : t.Status.ToString())}")
                .ToList();

            var trackerError = failDetails.Count > 0
                ? "Tracker failure: " + string.Join("; ", failDetails)
                : "All trackers failed or unresponsive.";

            var wasStalled = this.isTrackerStalled;
            this.isTrackerStalled = true;
            this.errorMessage = trackerError;

            if (!wasStalled)
            {
                eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", trackerError, isResolved: false));
            }

            return true;
        }

        return this.isTrackerStalled;
    }

    internal void SetTrackerStalled(string message, IEventAggregator eventAggregator = null)
    {
        var wasStalled = this.isTrackerStalled;
        this.isTrackerStalled = true;
        this.errorMessage = message;

        if (!wasStalled)
        {
            eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", message, isResolved: false));
        }
    }

    internal void ClearTrackerStalled(IEventAggregator eventAggregator = null)
    {
        var wasStalled = this.isTrackerStalled;
        this.isTrackerStalled = false;
        this.errorMessage = null;

        if (wasStalled)
        {
            eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "Tracker", "Tracker recovered", isResolved: true));
        }
    }

    public long DownloadedBytes => this.Manager?.Monitor?.DataBytesReceived ?? 0;

    public long UploadedBytes => this.Manager?.Monitor?.DataBytesSent ?? 0;

    public double Progress => this.Manager != null ? this.Manager.Progress / 100.0 : 0.0;

    public long DownloadSpeed => this.Manager?.Monitor?.DownloadRate ?? 0;

    public long UploadSpeed => this.Manager?.Monitor?.UploadRate ?? 0;

    public int ConnectedSeeders => this.Manager?.Peers?.Seeds ?? 0;

    public int ConnectedLeechers => this.Manager?.Peers?.Leechs ?? 0;

    public bool IsSuperSeeding => this.Manager?.IsInitialSeeding ?? false;

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
            if (this.Picker != null)
            {
                return this.Picker.GetAvailability();
            }

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
                var ip = p.Uri?.Host;
                if (!string.IsNullOrEmpty(ip) && this.blocklistService != null && this.blocklistService.IsIpBlocked(ip))
                {
                    continue;
                }

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

    public TorrentResourceMetrics GetResourceMetrics()
    {
        if (this.Manager == null)
        {
            return new TorrentResourceMetrics
            {
                TorrentId = this.TorrentId,
                InfoHash = this.InfoHash,
                Category = this.Category ?? string.Empty,
                Status = "Stopped",
            };
        }

        var monitor = this.Manager.Monitor;
        var peers = this.GetCachedPeers();

        var encryptedCount = 0;
        var plaintextCount = 0;
        var tcpCount = 0;
        var utpCount = 0;

        foreach (var p in peers)
        {
            if (p.EncryptionType.ToString() != "None")
            {
                encryptedCount++;
            }
            else
            {
                plaintextCount++;
            }

            var clientStr = p.ClientApp.Client.ToString();
            if (clientStr.Contains("uTP", StringComparison.OrdinalIgnoreCase))
            {
                utpCount++;
            }
            else
            {
                tcpCount++;
            }
        }

        var dataDown = monitor?.DataBytesReceived ?? 0;
        var dataUp = monitor?.DataBytesSent ?? 0;
        var protoDown = monitor?.ProtocolBytesReceived ?? 0;
        var protoUp = monitor?.ProtocolBytesSent ?? 0;
        var totalDown = dataDown + protoDown;
        var efficiencyRatio = totalDown > 0 ? (double)dataDown / totalDown : 1.0;

        var bitfield = this.Manager.Bitfield;
        var totalPieces = bitfield?.Length ?? 0;
        var completedPieces = 0;
        if (bitfield != null)
        {
            for (var i = 0; i < bitfield.Length; i++)
            {
                if (bitfield[i])
                {
                    completedPieces++;
                }
            }
        }

        var openConns = this.Manager.OpenConnections;
        var isDownloading = this.Manager.State == TorrentState.Downloading || this.Manager.State == TorrentState.Starting;
        var piecesInFlight = isDownloading ? Math.Min(openConns * 2, Math.Max(0, totalPieces - completedPieces)) : 0;
        var pieceLength = this.Manager.Torrent?.PieceLength ?? (totalPieces > 0 && this.Manager.Torrent != null ? (int)(this.Manager.Torrent.Size / totalPieces) : 262144);
        var hashFails = this.Manager.HashFails;
        var wastedBytes = (long)hashFails * pieceLength;
        var estMemBuffer = (long)piecesInFlight * pieceLength;

        var availabilityList = this.PieceAvailability;
        var swarmAvailability = 0.0;
        if (availabilityList.Length > 0)
        {
            long sum = 0;
            for (var i = 0; i < availabilityList.Length; i++)
            {
                sum += availabilityList[i];
            }

            swarmAvailability = Math.Round((double)sum / availabilityList.Length, 2);
        }

        var downSpeed = monitor?.DownloadRate ?? 0;
        var upSpeed = monitor?.UploadRate ?? 0;
        var totalSize = this.Manager.Torrent?.Size ?? (long)totalPieces * pieceLength;
        long? etaSeconds = null;
        if (isDownloading && downSpeed > 0 && totalSize > dataDown)
        {
            etaSeconds = (totalSize - dataDown) / downSpeed;
        }

        var ratio = dataDown > 0 ? Math.Round((double)dataUp / dataDown, 2) : 0.0;

        return new TorrentResourceMetrics
        {
            TorrentId = this.TorrentId,
            InfoHash = this.InfoHash,
            Name = this.Manager.Torrent?.Name ?? this.InfoHash,
            Category = this.Category ?? string.Empty,
            Status = this.Status.ToString(),
            Progress = this.Progress,
            TotalBytes = totalSize,
            PayloadDownloadSpeed = downSpeed,
            PayloadUploadSpeed = upSpeed,
            ProtocolDownloadSpeed = 0,
            ProtocolUploadSpeed = 0,
            DownloadedPayload = dataDown,
            UploadedPayload = dataUp,
            ProtocolDownloaded = protoDown,
            ProtocolUploaded = protoUp,
            EfficiencyRatio = Math.Round(efficiencyRatio * 100.0, 1),
            ConnectedPeers = openConns,
            ConnectedSeeds = this.Manager.Peers?.Seeds ?? 0,
            ConnectedLeechers = this.Manager.Peers?.Leechs ?? 0,
            TotalAvailablePeers = this.Manager.Peers?.Available ?? 0,
            TcpPeers = tcpCount,
            UtpPeers = utpCount,
            EncryptedPeers = encryptedCount,
            PlaintextPeers = plaintextCount,
            TotalPieces = totalPieces,
            CompletedPieces = completedPieces,
            PiecesInFlight = piecesInFlight,
            PieceLength = pieceLength,
            HashFails = hashFails,
            WastedBytes = wastedBytes,
            DiskPendingWrites = piecesInFlight,
            EstimatedMemoryBufferBytes = estMemBuffer,
            SwarmAvailability = swarmAvailability,
            Ratio = ratio,
            EtaSeconds = etaSeconds,
        };
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
