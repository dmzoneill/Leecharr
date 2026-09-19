// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using MonoTorrent.PieceWriter;
using MonoTorrent.PortForwarding;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.PortMapping;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using CoreTorrent = NzbDrone.Core.Torrents.Torrent;
using MtTorrent = MonoTorrent.Torrent;

namespace NzbDrone.Core.BitTorrent;

public class MonoTorrentDownloadEngine : ITorrentEngine,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IHandle<NetworkBindingProviderSwitchedEvent>,
    IHandle<ConfigSavedEvent>,
    IHandle<ConfigFileSavedEvent>,
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
    private readonly INetworkBindingService networkBindingService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly ITorrentLogService torrentLogService;
    private readonly ITorrentFileRepository torrentFileRepository;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IPeerConnectionHistoryService peerConnectionHistoryService;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, MonoTorrentDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<int> interruptedTorrentIds = new();
    private readonly object pendingTorrentsLock = new();
    private readonly List<(CoreTorrent Torrent, byte[] TorrentFileBytes, string MagnetUri)> pendingTorrents = new();
    private readonly List<FilteringPeerConnectionListener> activePeerListeners = new();
    private readonly SemaphoreSlim engineStateLock = new(1, 1);
    private readonly object vpnTransitionLock = new();
    private Task vpnTransitionQueue = Task.CompletedTask;

    private ClientEngine engine;
    private Timer trackerHealthTimer;
    private Timer diskFlushTimer;
    private Timer fastResumeAutoSaveTimer;
    private volatile bool isHaltedByKillSwitch;
    private volatile bool isEngineStopping;
    private bool disposed;

    private long totalPiecesHashed;
    private long totalHashFails;
    private long blockedPeersCount;
    private DateTime lastPieceHashSample = DateTime.UtcNow;
    private long lastPiecesHashedCount;
    private DateTime lastProtocolSample = DateTime.UtcNow;
    private long lastTotalProtoDown;
    private long lastTotalProtoUp;
    private long lastProtoDownSpeed;
    private long lastProtoUpSpeed;

    private string lastAppliedInterfaceBinding = string.Empty;
    private int lastAppliedListenPort;
    private string lastAppliedProxyType = string.Empty;
    private string lastAppliedProxyHost = string.Empty;
    private int lastAppliedProxyPort;
    private string lastAppliedProxyUsername = string.Empty;
    private string lastAppliedProxyPassword = string.Empty;
    private bool lastAppliedAnonymousMode;

    internal string LastAppliedInterfaceBinding => this.lastAppliedInterfaceBinding;

    internal int LastAppliedListenPort => this.lastAppliedListenPort;

    internal string LastAppliedProxyType => this.lastAppliedProxyType;

    internal string LastAppliedProxyHost => this.lastAppliedProxyHost;

    internal int LastAppliedProxyPort => this.lastAppliedProxyPort;

    internal string LastAppliedProxyUsername => this.lastAppliedProxyUsername;

    internal string LastAppliedProxyPassword => this.lastAppliedProxyPassword;

    internal bool LastAppliedAnonymousMode => this.lastAppliedAnonymousMode;

    public bool IsHaltedByKillSwitch => this.isHaltedByKillSwitch;

    public long BlockedPeersCount => Interlocked.Read(ref this.blockedPeersCount);

    public int ActiveTorrentsCount => this.tasks.Count;

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
        IVpnKillSwitchService vpnKillSwitchService = null,
        INetworkBindingService networkBindingService = null,
        IAppFolderInfo appFolderInfo = null,
        ITorrentLogService torrentLogService = null,
        ITorrentFileRepository torrentFileRepository = null,
        ITrackerEntryRepository trackerEntryRepository = null,
        IPeerConnectionHistoryService peerConnectionHistoryService = null)
    {
        this.configService = configService;
        this.storagePathService = storagePathService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.eventAggregator = eventAggregator;
        this.blocklistService = blocklistService;
        this.natPmpPortMapperService = natPmpPortMapperService ?? new NatPmpPortMapperService(configService: configService);
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.networkBindingService = networkBindingService;
        this.appFolderInfo = appFolderInfo;
        this.torrentLogService = torrentLogService;
        this.torrentFileRepository = torrentFileRepository;
        this.trackerEntryRepository = trackerEntryRepository;
        this.peerConnectionHistoryService = peerConnectionHistoryService;
        this.logger = LogManager.GetCurrentClassLogger();

        this.trackerHealthTimer = new Timer(_ => this.CheckTrackerHealth(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

        var flushIntervalSec = Math.Max(1, this.configService?.DiskFlushIntervalSeconds > 0 ? this.configService.DiskFlushIntervalSeconds : 30);
        this.diskFlushTimer = new Timer(_ => this.PerformPeriodicDiskFlushAndCacheScaling(), null, TimeSpan.FromSeconds(flushIntervalSec), TimeSpan.FromSeconds(flushIntervalSec));

        var fastResumeIntervalSec = Math.Max(5, this.configService?.AutoSaveFastResumeIntervalSeconds > 0 ? this.configService.AutoSaveFastResumeIntervalSeconds : 300);
        this.fastResumeAutoSaveTimer = new Timer(_ => this.PerformPeriodicFastResumeSave(), null, TimeSpan.FromSeconds(fastResumeIntervalSec), TimeSpan.FromSeconds(fastResumeIntervalSec));

        this.lastAppliedInterfaceBinding = !string.IsNullOrWhiteSpace(this.configService?.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService?.BindInterface ?? string.Empty;
        this.lastAppliedListenPort = this.configService?.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        this.lastAppliedProxyType = this.configService?.ProxyType ?? string.Empty;
        this.lastAppliedProxyHost = this.configService?.ProxyHost ?? string.Empty;
        this.lastAppliedProxyPort = this.configService?.ProxyPort ?? 0;
        this.lastAppliedProxyUsername = this.configService?.ProxyUsername ?? string.Empty;
        this.lastAppliedProxyPassword = this.configService?.ProxyPassword ?? string.Empty;
        this.lastAppliedAnonymousMode = this.configService?.AnonymousMode ?? false;
    }

    public async Task StartAsync()
    {
        await this.engineStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.StartEngineAsyncCore().ConfigureAwait(false);
        }
        finally
        {
            this.engineStateLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await this.engineStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.StopEngineAsyncCore().ConfigureAwait(false);
        }
        finally
        {
            this.engineStateLock.Release();
        }
    }

    private async Task StartEngineAsyncCore()
    {
        if (this.engine != null)
        {
            return;
        }

        var port = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        this.logger.Info("Initializing MonoTorrent download engine on port {0}...", port);

        var cacheDir = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
            ? Path.Combine(this.appFolderInfo.AppDataFolder, "Cache")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr", "Cache");
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch
        {
        }

        var allowedEncryption = GetAllowedEncryption(this.configService.EncryptionMode);

        var listenIp = IPAddress.Any;
        IPAddress listenIpv6 = IPAddress.IPv6Any;
        IPAddress resolvedIpv6 = null;
        var iface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;

        var isKillSwitchEnabled = this.configService.EnableVpnKillSwitch ||
            (this.vpnKillSwitchService?.IsKillSwitchEnabled ?? false);

        var hasSpecificInterface = !string.IsNullOrWhiteSpace(iface) &&
            !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase);

        var isInterfaceUp = true;
        if (this.networkBindingService != null && hasSpecificInterface)
        {
            isInterfaceUp = this.networkBindingService.IsInterfaceUp(iface);
        }

        if (hasSpecificInterface)
        {
            if (!isInterfaceUp && isKillSwitchEnabled)
            {
                this.logger.Error("Network binding provider reported interface '{0}' is offline while Kill Switch is active. Halting engine in strict fail-closed state.", iface);
                this.isHaltedByKillSwitch = true;
                return;
            }

            var resolvedIp = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress()
                ?? this.ResolveInterfaceIp(iface, AddressFamily.InterNetwork);

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

            resolvedIpv6 = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress(AddressFamily.InterNetworkV6)
                ?? this.ResolveInterfaceIp(iface, AddressFamily.InterNetworkV6);
            if (resolvedIpv6 != null)
            {
                if (resolvedIpv6.IsIPv6LinkLocal || resolvedIpv6.IsIPv6SiteLocal || resolvedIpv6.IsIPv6Multicast ||
                    IPAddress.IsLoopback(resolvedIpv6) || resolvedIpv6.Equals(IPAddress.IPv6Any) || resolvedIpv6.Equals(IPAddress.IPv6None))
                {
                    resolvedIpv6 = null;
                }
                else
                {
                    listenIpv6 = resolvedIpv6;
                    this.logger.Info("Bound MonoTorrent IPv6 listening socket to interface '{0}' ({1})", iface, listenIpv6);
                }
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

        if (this.configService.EnableIPv6 && Socket.OSSupportsIPv6)
        {
            if (hasSpecificInterface)
            {
                if (resolvedIpv6 != null)
                {
                    try
                    {
                        listenEndPoints["ipv6"] = new IPEndPoint(resolvedIpv6, port);
                        this.logger.Info("Configured IPv6 listening socket on [{0}]:{1}", resolvedIpv6, port);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to configure IPv6 listening socket");
                    }
                }
                else
                {
                    this.logger.Warn("Bound interface '{0}' has no IPv6 address. IPv6 listening disabled to prevent traffic leak outside bound interface.", iface);
                }
            }
            else
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
        }

        if (this.configService.UpnpEnabled && !this.configService.AnonymousMode)
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

        var dynamicCacheBytes = this.CalculateDynamicDiskCacheBytes();
        var cachePolicy = this.GetConfiguredCachePolicy();
        var fastResumeMode = this.GetConfiguredFastResumeMode();

        var isProxyActive = this.IsProxyActive();
        var isAnonymous = this.configService.AnonymousMode;
        var allowLpd = !isAnonymous && !isProxyActive && !hasSpecificInterface && !isKillSwitchEnabled && this.configService.EnableLpd;
        if (this.configService.EnableLpd && !allowLpd && (hasSpecificInterface || isKillSwitchEnabled))
        {
            this.logger.Info("Local Peer Discovery (LPD) disabled to prevent multicast UDP infohash leaks over physical LAN adapters while bound to VPN interface or kill switch.");
        }

        var engineSettingsBuilder = new EngineSettingsBuilder
        {
            AllowPortForwarding = !isAnonymous && this.configService.UpnpEnabled,
            AllowLocalPeerDiscovery = allowLpd,
            AllowHaveSuppression = this.configService.ExtensionLtDontHave,
            AllowedEncryption = allowedEncryption,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadDhtCache = true,
            UsePartialFiles = this.configService.AppendIncompleteExtension,
            DhtEndPoint = (!isAnonymous && !isProxyActive && this.configService.EnableDht) ? new IPEndPoint(listenIp, port) : null,
            CacheDirectory = cacheDir,
            ConnectionTimeout = TimeSpan.FromSeconds(this.configService.TransportConnectionTimeoutSeconds > 0 ? this.configService.TransportConnectionTimeoutSeconds : 30),
            DiskCacheBytes = dynamicCacheBytes,
            DiskCachePolicy = cachePolicy,
            FastResumeMode = fastResumeMode,
            MaximumConnections = this.configService.MaxGlobalConnections > 0 ? this.configService.MaxGlobalConnections : 300,
            MaximumDownloadRate = this.configService.MaxDownloadSpeedKbps > 0
                ? (int)Math.Min((long)this.configService.MaxDownloadSpeedKbps * 1024, int.MaxValue)
                : 0,
            MaximumUploadRate = this.configService.MaxUploadSpeedKbps > 0
                ? (int)Math.Min((long)this.configService.MaxUploadSpeedKbps * 1024, int.MaxValue)
                : 0,
            WebSeedDelay = TimeSpan.FromSeconds(this.configService.WebSeedDelaySeconds > 0 ? this.configService.WebSeedDelaySeconds : 30),
            ListenEndPoints = isAnonymous ? new Dictionary<string, IPEndPoint>() : listenEndPoints,
        };

        if (this.configService.AnonymousMode)
        {
            this.logger.Info("Anonymous mode is enabled. Suppressing listen endpoints, DHT, LPD, port forwarding, and masking client identifiers.");
            engineSettingsBuilder.AllowPortForwarding = false;
            engineSettingsBuilder.AllowLocalPeerDiscovery = false;
            engineSettingsBuilder.DhtEndPoint = null;
            engineSettingsBuilder.ListenEndPoints = new Dictionary<string, IPEndPoint>();
        }

        this.logger.Info(
            "Configured protocol extensions: FastExtension={0}, uTP={1}, TcpFallback={2}, HaveSuppression={3}, Timeout={4}s",
            this.configService.ExtensionFastExtension,
            this.configService.UtpEnabled,
            this.configService.TcpFallback,
            this.configService.ExtensionLtDontHave,
            this.configService.TransportConnectionTimeoutSeconds);

        this.logger.Info(
            "Configured disk write cache: {0} MB (policy={1}, fastResumeMode={2}, flushInterval={3}s, autoSaveFastResumeInterval={4}s)",
            dynamicCacheBytes / (1024 * 1024),
            cachePolicy,
            fastResumeMode,
            this.configService.DiskFlushIntervalSeconds,
            this.configService.AutoSaveFastResumeIntervalSeconds);

        var engineSettings = engineSettingsBuilder.ToSettings();
        var factories = Factories.Default;

        var userAgent = this.configService.AnonymousMode ? string.Empty : this.configService.BitTorrentUserAgent;
        var peerIdPrefix = this.configService.AnonymousMode ? string.Empty : this.configService.PeerIdPrefix;

        ConfigureGlobalMonoTorrentDefaults(peerIdPrefix, userAgent, this.configService.AnonymousMode);

        var webProxy = this.GetConfiguredWebProxy();
        if (webProxy is WebProxy wp)
        {
            this.logger.Info("Configured MonoTorrent tracker proxy via {0}", wp.Address);
        }

        factories = factories.WithHttpClientCreator(af => this.CreateHttpClient(af));

        var baseFactories = factories;
        factories = factories.WithSocketConnectorCreator(() => new BoundSocketConnector(
            () => this.GetBoundLocalIp(AddressFamily.InterNetwork),
            () => this.GetBoundLocalIp(AddressFamily.InterNetworkV6),
            this.networkBindingService,
            () => !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
                ? this.configService.NetworkInterfaceBinding
                : this.configService.BindInterface,
            this.blocklistService,
            () => Interlocked.Increment(ref this.blockedPeersCount),
            this.configService,
            () => this.isHaltedByKillSwitch || (this.vpnKillSwitchService != null && this.vpnKillSwitchService.IsFailClosedActive)));

        lock (this.activePeerListeners)
        {
            this.activePeerListeners.Clear();
        }

        factories = factories.WithPeerConnectionListenerCreator(endPoint =>
        {
            var listener = new FilteringPeerConnectionListener(
                baseFactories.CreatePeerConnectionListener(endPoint),
                this.blocklistService,
                () => Interlocked.Increment(ref this.blockedPeersCount),
                maxHalfOpenConnections: this.configService.MaximumHalfOpenConnections > 0 ? this.configService.MaximumHalfOpenConnections : 50,
                maxConnectionsPerIp: this.configService.MaxConnectionsPerIp > 0 ? this.configService.MaxConnectionsPerIp : 5,
                isHalted: () => this.isHaltedByKillSwitch);

            lock (this.activePeerListeners)
            {
                this.activePeerListeners.Add(listener);
            }

            return listener;
        });

        this.engine = new ClientEngine(engineSettings, factories);
        var overrideField = typeof(DiskManager).GetField("GetHashAsyncOverride", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (overrideField != null)
        {
            Func<ITorrentManagerInfo, int, PieceHash, Task<bool>> hashOverride = (mgr, idx, dest) => this.CalculatePieceHashDirectAsync(mgr, idx, dest);
            overrideField.SetValue(this.engine.DiskManager, hashOverride);
        }

        this.ApplyCustomPeerId(this.engine, peerIdPrefix);

        this.lastAppliedInterfaceBinding = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;
        this.lastAppliedListenPort = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        this.lastAppliedProxyType = this.configService.ProxyType ?? string.Empty;
        this.lastAppliedProxyHost = this.configService.ProxyHost ?? string.Empty;
        this.lastAppliedProxyPort = this.configService.ProxyPort;
        this.lastAppliedProxyUsername = this.configService.ProxyUsername ?? string.Empty;
        this.lastAppliedProxyPassword = this.configService.ProxyPassword ?? string.Empty;
        this.lastAppliedAnonymousMode = this.configService.AnonymousMode;

        if (this.configService.UpnpEnabled && !this.configService.AnonymousMode && this.engine != null)
        {
            try
            {
                var portForwarderProp = typeof(ClientEngine).GetProperty("PortForwarder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (portForwarderProp?.GetValue(this.engine) is IPortForwarder forwarder)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await forwarder.RegisterMappingAsync(new Mapping(Protocol.Tcp, port)).ConfigureAwait(false);
                            await forwarder.RegisterMappingAsync(new Mapping(Protocol.Udp, port)).ConfigureAwait(false);
                            await forwarder.StartAsync(CancellationToken.None).ConfigureAwait(false);
                            this.logger.Info("UPnP/NAT-PMP port forwarder started for listening port {0}", port);
                        }
                        catch (Exception ex)
                        {
                            this.logger.Debug(ex, "Failed to start UPnP port forwarder for port {0}", port);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to initialize UPnP port forwarder");
            }
        }

        this.logger.Info("MonoTorrent engine started successfully on {0}:{1}.", listenIp, port);

        await this.DrainPendingTorrentsAsync().ConfigureAwait(false);
    }

    internal async Task RestartEngineAsyncCore()
    {
        await this.engineStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            this.logger.Info("Restarting MonoTorrent download engine to apply configuration changes...");
            await this.StopEngineAsyncCore().ConfigureAwait(false);
            await this.StartEngineAsyncCore().ConfigureAwait(false);
        }
        finally
        {
            this.engineStateLock.Release();
        }
    }

    internal IWebProxy GetConfiguredWebProxy()
    {
        if (!string.IsNullOrWhiteSpace(this.configService?.ProxyType) &&
            !string.Equals(this.configService.ProxyType, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(this.configService.ProxyHost))
        {
            var proxyType = this.configService.ProxyType.ToLowerInvariant();
            if (proxyType is "socks5" or "http")
            {
                var proxyHost = this.configService.ProxyHost;
                var proxyPort = this.configService.ProxyPort > 0 ? this.configService.ProxyPort : (proxyType == "socks5" ? 1080 : 8080);
                var formattedHost = proxyHost.Contains(':') && !proxyHost.StartsWith("[")
                    ? $"[{proxyHost}]"
                    : proxyHost;
                var proxyUri = new Uri($"{proxyType}://{formattedHost}:{proxyPort}");

                ICredentials credentials = null;
                if (!string.IsNullOrWhiteSpace(this.configService.ProxyUsername))
                {
                    credentials = new NetworkCredential(this.configService.ProxyUsername, this.configService.ProxyPassword ?? string.Empty);
                }

                return new WebProxy(proxyUri)
                {
                    Credentials = credentials,
                };
            }
        }

        return null;
    }

    private async Task StopEngineAsyncCore()
    {
        if (this.engine == null)
        {
            return;
        }

        this.logger.Info("Stopping MonoTorrent download engine...");
        this.isEngineStopping = true;

        try
        {
            foreach (var task in this.tasks.Values)
            {
                try
                {
                    if (task.Manager != null && task.Manager.State != TorrentState.Stopped)
                    {
                        await task.Manager.StopAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Error stopping torrent manager for {0}", task.InfoHash);
                }
            }

            if (this.engine?.DiskManager != null)
            {
                try
                {
                    foreach (var task in this.tasks.Values)
                    {
                        if (task.Manager != null)
                        {
                            await this.engine.DiskManager.FlushAsync(task.Manager).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Error flushing DiskManager write buffers during engine stop");
                }
            }

            try
            {
                await this.SaveAllFastResumeCheckpointsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error saving FastResume checkpoints during engine stop");
            }

            try
            {
                var portForwarderProp = typeof(ClientEngine).GetProperty("PortForwarder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (portForwarderProp?.GetValue(this.engine) is IPortForwarder forwarder)
                {
                    await forwarder.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error stopping port forwarder");
            }

            await this.engine.StopAllAsync();
            this.engine.Dispose();
            this.engine = null;

            lock (this.activePeerListeners)
            {
                this.activePeerListeners.Clear();
            }

            try
            {
                await this.natPmpPortMapperService.StopAsync();
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error stopping NAT-PMP port mapper on engine stop.");
            }
        }
        finally
        {
            this.isEngineStopping = false;
        }

        this.logger.Info("MonoTorrent download engine stopped.");
    }

    private async Task<bool> EnsureEngineReadyAsync()
    {
        if (this.engine != null)
        {
            return true;
        }

        await this.StartAsync();

        if (this.engine != null)
        {
            return true;
        }

        if (this.isHaltedByKillSwitch)
        {
            return false;
        }

        throw new InvalidOperationException("Torrent engine failed to start.");
    }

    private async Task DrainPendingTorrentsAsync()
    {
        List<(CoreTorrent Torrent, byte[] TorrentFileBytes, string MagnetUri)> pending;

        lock (this.pendingTorrentsLock)
        {
            if (this.pendingTorrents.Count == 0)
            {
                return;
            }

            pending = new List<(CoreTorrent Torrent, byte[] TorrentFileBytes, string MagnetUri)>(this.pendingTorrents);
            this.pendingTorrents.Clear();
        }

        foreach (var (torrent, fileBytes, uri) in pending)
        {
            try
            {
                await this.AddTorrentAsync(torrent, fileBytes, uri);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to add queued torrent {0}", torrent.Name);
            }
        }
    }

    public async Task<IDownloadTask> AddTorrentAsync(CoreTorrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        if (torrent == null)
        {
            return null;
        }

        if (this.tasks.TryGetValue(torrent.Id, out var existingTask))
        {
            return existingTask;
        }

        if (!await this.EnsureEngineReadyAsync())
        {
            lock (this.pendingTorrentsLock)
            {
                if (!this.pendingTorrents.Any(p => p.Torrent?.Id == torrent.Id))
                {
                    this.pendingTorrents.Add((torrent, torrentFileBytes, magnetUri));
                }
            }

            this.logger.Info("VPN kill switch active; torrent '{0}' queued until engine is available.", torrent.Name);
            return null;
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

        var useIncompleteDir = this.configService.EnableIncompleteDir;
        var baseSavePath = torrent.SavePath;
        if (!string.IsNullOrWhiteSpace(baseSavePath) && !string.IsNullOrWhiteSpace(torrent.Name))
        {
            var trimmed = baseSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dirOrFileName = Path.GetFileName(trimmed);
            if (Path.HasExtension(trimmed) ||
                string.Equals(dirOrFileName, torrent.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(dirOrFileName), torrent.Name, StringComparison.OrdinalIgnoreCase))
            {
                baseSavePath = Path.GetDirectoryName(trimmed);
            }
        }

        var completedDir = !string.IsNullOrWhiteSpace(baseSavePath)
            ? baseSavePath
            : this.storagePathService.GetCompletedDirectory(torrent.Category);

        if (string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            torrent.SavePath = completedDir;
        }

        var infoHashHex = torrent.InfoHash
            ?? parsedTorrent?.InfoHashes.V1OrV2?.ToHex()
            ?? parsedTorrent?.InfoHashes.V1?.ToHex()
            ?? parsedTorrent?.InfoHashes.V2?.ToHex();

        if (!string.IsNullOrWhiteSpace(infoHashHex) &&
            this.infoHashToId.TryGetValue(infoHashHex, out var existingId) &&
            this.tasks.TryGetValue(existingId, out var existingByHash))
        {
            if (existingId != torrent.Id)
            {
                this.tasks.TryRemove(existingId, out _);
                this.tasks[torrent.Id] = existingByHash;
                this.infoHashToId[infoHashHex] = torrent.Id;
            }

            return existingByHash;
        }

        var cacheDir = this.GetCacheDirectory();
        var savedFastResume = !string.IsNullOrWhiteSpace(infoHashHex)
            ? await this.TryLoadSavedFastResumeAsync(infoHashHex, cacheDir).ConfigureAwait(false)
            : null;

        var incompleteDir = this.storagePathService.GetIncompleteDirectory();
        var hasCompletedFiles = false;
        var hasIncompleteFiles = false;

        if (parsedTorrent?.Files != null && parsedTorrent.Files.Count > 0)
        {
            var completedCount = 0;
            var incompleteCount = 0;
            foreach (var file in parsedTorrent.Files)
            {
                var targetCompletedPath = Path.Combine(completedDir, file.Path);
                var altCompletedPath = Path.Combine(completedDir, Path.GetFileName(file.Path));
                if ((this.diskProvider.FileExists(targetCompletedPath) && this.GetFileSizeSafely(targetCompletedPath) > 0) ||
                    (this.diskProvider.FileExists(altCompletedPath) && this.GetFileSizeSafely(altCompletedPath) > 0))
                {
                    completedCount++;
                }

                var targetIncompletePath = Path.Combine(incompleteDir, file.Path);
                var altIncompletePath = Path.Combine(incompleteDir, Path.GetFileName(file.Path));
                if ((this.diskProvider.FileExists(targetIncompletePath) && this.GetFileSizeSafely(targetIncompletePath) > 0) ||
                    (this.diskProvider.FileExists(altIncompletePath) && this.GetFileSizeSafely(altIncompletePath) > 0) ||
                    this.diskProvider.FileExists(targetIncompletePath + ".!mt") ||
                    this.diskProvider.FileExists(targetIncompletePath + ".incomplete") ||
                    this.diskProvider.FileExists(altIncompletePath + ".!mt") ||
                    this.diskProvider.FileExists(altIncompletePath + ".incomplete"))
                {
                    incompleteCount++;
                }
            }

            if (completedCount == parsedTorrent.Files.Count && completedCount > 0)
            {
                hasCompletedFiles = true;
            }
            else if (incompleteCount > 0)
            {
                hasIncompleteFiles = true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(torrent.Name))
        {
            var completedTarget = Path.Combine(completedDir, torrent.Name);
            if (this.diskProvider.FolderExists(completedTarget) || this.diskProvider.FileExists(completedTarget))
            {
                hasCompletedFiles = true;
            }
        }

        var isDbCompleteOrSeeding = torrent.Status == TorrentStatus.Seeding ||
                                    (torrent.Progress >= 1.0 && !string.IsNullOrWhiteSpace(torrent.SavePath)) ||
                                    torrent.DateCompleted.HasValue;

        var isCompleteOrSeeding = ((torrent.Status == TorrentStatus.Seeding || torrent.Progress >= 1.0) && hasCompletedFiles) ||
                                  (torrent.DateCompleted.HasValue && hasCompletedFiles) ||
                                  (savedFastResume?.Bitfield != null && savedFastResume.Bitfield.AllTrue && hasCompletedFiles);

        var workingPath = (isCompleteOrSeeding || isDbCompleteOrSeeding || !useIncompleteDir || (!hasIncompleteFiles && hasCompletedFiles && torrent.DateCompleted.HasValue))
            ? completedDir
            : incompleteDir;

        try
        {
            Directory.CreateDirectory(workingPath);
        }
        catch
        {
        }

        var isProxyConfigured = this.configService.ProxyType?.ToLowerInvariant() is "socks5" or "http" &&
            !string.IsNullOrWhiteSpace(this.configService.ProxyHost);
        var isProxyActive = isProxyConfigured || this.configService.ForceProxy || (this.networkBindingService?.ActiveProvider is IProxyTunnelBindingProvider);

        var torrentSettingsBuilder = new TorrentSettingsBuilder
        {
            MaximumConnections = this.configService.MaxPerTorrentConnections > 0 ? this.configService.MaxPerTorrentConnections : 50,
            UploadSlots = this.configService.MaxUploadSlots > 0 ? this.configService.MaxUploadSlots : 4,
            MaximumDownloadRate = torrent.DownloadLimit > 0
                ? (int)Math.Min((long)torrent.DownloadLimit * 1024, int.MaxValue)
                : 0,
            MaximumUploadRate = torrent.UploadLimit > 0
                ? (int)Math.Min((long)torrent.UploadLimit * 1024, int.MaxValue)
                : 0,
            AllowInitialSeeding = torrent.InitialSeeding,
        };

        if (torrent.IsPrivate)
        {
            torrentSettingsBuilder.AllowDht = false;
            torrentSettingsBuilder.AllowPeerExchange = false;
            this.logger.Info("BEP 27 strictly enforced for private torrent {0}: DHT and PEX disabled", torrent.Name);
        }
        else if (isProxyActive)
        {
            torrentSettingsBuilder.AllowDht = false;
            torrentSettingsBuilder.AllowPeerExchange = this.configService.EnablePex;
            this.logger.Info("Proxy leak prevention active for torrent {0}: DHT disabled", torrent.Name);
        }
        else
        {
            torrentSettingsBuilder.AllowDht = this.configService.EnableDht;
            torrentSettingsBuilder.AllowPeerExchange = this.configService.EnablePex;
        }

        TorrentManager manager = null;
        var torrentSettings = torrentSettingsBuilder.ToSettings();

        if (this.engine != null && !string.IsNullOrWhiteSpace(infoHashHex))
        {
            manager = this.engine.Torrents.FirstOrDefault(m =>
                m.InfoHashes != null && (
                    string.Equals(m.InfoHashes.V1OrV2?.ToHex(), infoHashHex, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.InfoHashes.V1?.ToHex(), infoHashHex, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.InfoHashes.V2?.ToHex(), infoHashHex, StringComparison.OrdinalIgnoreCase)));
        }

        if (manager == null)
        {
            var isSequential = torrent.SequentialDownload || string.Equals(this.configService.PiecePickerStrategy, "Sequential", StringComparison.OrdinalIgnoreCase);
            try
            {
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
                        try
                        {
                            var parsedMagnet = MagnetLinkParser.Parse(magnetUri);
                            if (parsedMagnet.WebSeeds != null && parsedMagnet.WebSeeds.Count > 0)
                            {
                                foreach (var ws in parsedMagnet.WebSeeds)
                                {
                                    if (!magnetLink.Webseeds.Contains(ws))
                                    {
                                        magnetLink.Webseeds.Add(ws);
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }

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
                        try
                        {
                            var parsedMagnet = MagnetLinkParser.Parse(magnetUri);
                            if (parsedMagnet.WebSeeds != null && parsedMagnet.WebSeeds.Count > 0)
                            {
                                foreach (var ws in parsedMagnet.WebSeeds)
                                {
                                    if (!magnetLink.Webseeds.Contains(ws))
                                    {
                                        magnetLink.Webseeds.Add(ws);
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }

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
            }
            catch (TorrentException ex) when (ex.Message.Contains("already been registered", StringComparison.OrdinalIgnoreCase))
            {
                if (this.engine != null && !string.IsNullOrWhiteSpace(infoHashHex))
                {
                    manager = this.engine.Torrents.FirstOrDefault(m =>
                        m.InfoHashes != null && (
                            string.Equals(m.InfoHashes.V1OrV2?.ToHex(), infoHashHex, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(m.InfoHashes.V1?.ToHex(), infoHashHex, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(m.InfoHashes.V2?.ToHex(), infoHashHex, StringComparison.OrdinalIgnoreCase)));
                }

                if (manager == null)
                {
                    throw;
                }
            }
        }

        if (manager == null)
        {
            throw new InvalidOperationException("Failed to create TorrentManager for torrent.");
        }

        var loadedFastResume = false;
        if (savedFastResume != null && (!manager.HashChecked || manager.Progress < 100.0))
        {
            if (savedFastResume.Bitfield != null && savedFastResume.Bitfield.AllTrue && !hasCompletedFiles)
            {
                this.logger.Warn("Saved FastResume for {0} ({1}) indicates 100% completion but completed files are missing on disk; ignoring invalid FastResume.", torrent.Name, infoHashHex);
            }
            else
            {
                try
                {
                    await manager.LoadFastResumeAsync(savedFastResume).ConfigureAwait(false);
                    loadedFastResume = true;
                    this.logger.Info("Loaded saved FastResume checkpoint for {0} ({1}) - Progress: {2:F1}%, Complete: {3}", torrent.Name, infoHashHex, manager.Progress, manager.Complete);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to load saved FastResume for {0} ({1})", torrent.Name, infoHashHex);
                }
            }
        }

        if (!loadedFastResume && isCompleteOrSeeding && manager.InfoHashes != null)
        {
            var pieceCount = parsedTorrent?.PieceCount ?? manager.Torrent?.PieceCount ?? 0;
            if (pieceCount > 0)
            {
                try
                {
                    var isFullTorrent = manager.Files == null || !manager.Files.Any(f => f.Priority == MonoTorrent.Priority.DoNotDownload);
                    var resumeBitfield = isFullTorrent
                        ? new ReadOnlyBitField(new BitField(pieceCount).SetAll(true))
                        : manager.Bitfield ?? new ReadOnlyBitField(pieceCount);
                    var unhashed = new ReadOnlyBitField(pieceCount);
                    var synthesizedFastResume = new FastResume(manager.InfoHashes, resumeBitfield, unhashed);
                    await manager.LoadFastResumeAsync(synthesizedFastResume).ConfigureAwait(false);
                    await this.SaveFastResumeAtomicAsync(manager, this.GetCacheDirectory()).ConfigureAwait(false);
                    this.logger.Info("Synthesized and loaded 100% FastResume checkpoint for completed torrent {0} ({1})", torrent.Name, infoHashHex);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to load synthesized FastResume for {0} ({1})", torrent.Name, infoHashHex);
                }
            }
        }
        else if (!loadedFastResume && isDbCompleteOrSeeding && !hasCompletedFiles)
        {
            this.logger.Warn("Torrent {0} ({1}) is marked as complete/seeding in database, but completed files are missing on disk. Triggering hash check instead of synthesizing FastResume.", torrent.Name, infoHashHex);
            try
            {
                await manager.HashCheckAsync(autoStart: true).ConfigureAwait(false);
                torrent.Status = TorrentStatus.Checking;
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to trigger data integrity hash check for {0} ({1})", torrent.Name, infoHashHex);
            }
        }

        if (isProxyActive && manager.TrackerManager?.Tiers != null)
        {
            var udpTrackers = manager.TrackerManager.Tiers
                .SelectMany(t => t.Trackers)
                .Where(t => t.Uri != null && string.Equals(t.Uri.Scheme, "udp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var udpTracker in udpTrackers)
            {
                try
                {
                    await manager.TrackerManager.RemoveTrackerAsync(udpTracker).ConfigureAwait(false);
                    this.logger.Info("Removed UDP tracker '{0}' from torrent '{1}' to prevent SOCKS5 proxy IP leak", udpTracker.Uri, torrent.Name);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Could not remove UDP tracker {0}", udpTracker.Uri);
                }
            }
        }

        var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0 ? this.configService.LowDiskSpaceThresholdMb : 500;
        var thresholdBytes = thresholdMb * 1024L * 1024L;
        var availableSpace = this.diskProvider.GetAvailableSpace(workingPath);
        var isLowDiskSpace = availableSpace.HasValue && availableSpace.Value < thresholdBytes;

        var downloadTask = new MonoTorrentDownloadTask(
            torrent.Id,
            torrent.InfoHash,
            manager,
            torrent.Category,
            parsedTorrent,
            this.blocklistService,
            () => Interlocked.Increment(ref this.blockedPeersCount),
            this.configService,
            torrent.IsPrivate,
            workingPath,
            t => _ = this.ApplyStoredFilePrioritiesAsync(t),
            peerConnectionHistoryService: this.peerConnectionHistoryService,
            eventAggregator: this.eventAggregator);
        downloadTask.SavePath = completedDir;
        downloadTask.IsFilesMovedToCompleted = isCompleteOrSeeding;
        this.tasks[torrent.Id] = downloadTask;
        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            this.infoHashToId[torrent.InfoHash] = torrent.Id;
        }

        if (manager.InfoHashes?.V1 != null)
        {
            this.infoHashToId[manager.InfoHashes.V1.ToHex()] = torrent.Id;
        }

        if (manager.InfoHashes?.V2 != null)
        {
            this.infoHashToId[manager.InfoHashes.V2.ToHex()] = torrent.Id;
        }

        manager.TorrentStateChanged += this.OnTorrentStateChanged;
        manager.PieceHashed += this.OnPieceHashed;

        await this.ApplyStoredFilePrioritiesAsync(downloadTask).ConfigureAwait(false);

        if (manager.Complete || (manager.Bitfield != null && manager.Bitfield.Length > 0 && manager.Bitfield.AllTrue) || isCompleteOrSeeding)
        {
            downloadTask.IsFilesMovedToCompleted = true;
            if (torrent.Status == TorrentStatus.Downloading || (torrent.Status == TorrentStatus.Stopped && this.configService?.AutoStart == true))
            {
                torrent.Status = TorrentStatus.Seeding;
                torrent.Progress = 1.0;
            }
            else
            {
                torrent.Progress = 1.0;
            }
        }

        if (!isLowDiskSpace && parsedTorrent != null)
        {
            await this.PreallocateFilesAsync(manager, workingPath, parsedTorrent).ConfigureAwait(false);
        }

        if (this.isHaltedByKillSwitch)
        {
            await manager.PauseAsync();
            torrent.Status = TorrentStatus.Paused;
            this.logger.Warn("VPN Kill Switch active (fail-closed). Added torrent {0} in paused state.", torrent.Name);
        }
        else if (isLowDiskSpace)
        {
            await manager.PauseAsync();
            downloadTask.WasAutoPausedByDiskSpace = true;
            downloadTask.SetStorageFull($"StorageFull: Free disk space dropped below threshold ({availableSpace.Value / (1024 * 1024)} MB available, {thresholdMb} MB required).", this.eventAggregator);
            torrent.Status = TorrentStatus.Paused;
            torrent.ErrorMessage = downloadTask.ErrorMessage;
            this.logger.Warn("Insufficient free disk space on '{0}' for torrent '{1}' ({2} MB available, {3} MB threshold). Added torrent in paused state.", workingPath, torrent.Name, availableSpace.Value / (1024 * 1024), thresholdMb);
        }
        else if (torrent.Status == TorrentStatus.Paused)
        {
            await manager.PauseAsync();
            this.logger.Info("Added paused torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
        }
        else if (torrent.Status == TorrentStatus.Stopped)
        {
            if (this.configService.AutoStart && (isCompleteOrSeeding || manager.Complete || (manager.Bitfield != null && manager.Bitfield.AllTrue)))
            {
                await manager.StartAsync();
                torrent.Status = TorrentStatus.Seeding;
                this.logger.Info("AutoStarted complete/seeding torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
                try
                {
                    if (manager.TrackerManager != null)
                    {
                        _ = this.AnnounceTrackersAsync(manager, torrent.Id, isResumeOrStartup: true);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to announce tracker on startup for torrent {0}", torrent.Id);
                }
            }
            else
            {
                this.logger.Info("Added stopped torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
            }
        }
        else if (torrent.Status is TorrentStatus.Queued or TorrentStatus.Error or TorrentStatus.Stalled)
        {
            await manager.PauseAsync();
            this.logger.Info("Added inactive ({0}) torrent: {1} ({2})", torrent.Status, torrent.Name, torrent.InfoHash);
        }
        else if (torrent.Status == TorrentStatus.Checking || manager.State == TorrentState.Hashing)
        {
            this.logger.Info("Added torrent in checking state: {0} ({1})", torrent.Name, torrent.InfoHash);
        }
        else
        {
            if (this.configService?.AutoStart == false)
            {
                await manager.PauseAsync();
                torrent.Status = TorrentStatus.Paused;
                this.logger.Info("Added torrent in paused state due to AutoStart=false: {0} ({1})", torrent.Name, torrent.InfoHash);
            }
            else
            {
                await manager.StartAsync();
                this.logger.Info("Added and started torrent: {0} ({1})", torrent.Name, torrent.InfoHash);
                try
                {
                    if (manager.TrackerManager != null)
                    {
                        _ = this.AnnounceTrackersAsync(manager, torrent.Id, isResumeOrStartup: true);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to announce tracker on startup for torrent {0}", torrent.Id);
                }
            }
        }

        return downloadTask;
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        lock (this.pendingTorrentsLock)
        {
            this.pendingTorrents.RemoveAll(p => p.Torrent?.Id == torrentId);
        }

        if (this.tasks.TryRemove(torrentId, out var task))
        {
            this.infoHashToId.TryRemove(task.InfoHash, out _);
            if (task.Manager?.InfoHashes?.V1 != null)
            {
                this.infoHashToId.TryRemove(task.Manager.InfoHashes.V1.ToHex(), out _);
            }

            if (task.Manager?.InfoHashes?.V2 != null)
            {
                this.infoHashToId.TryRemove(task.Manager.InfoHashes.V2.ToHex(), out _);
            }

            task.UnhookEvents();

            if (task.Manager != null)
            {
                task.Manager.TorrentStateChanged -= this.OnTorrentStateChanged;
                task.Manager.PieceHashed -= this.OnPieceHashed;

                await task.Manager.StopAsync();
                if (this.engine != null)
                {
                    await this.engine.RemoveAsync(task.Manager);
                }

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

                                    if (!string.IsNullOrWhiteSpace(task.Manager.SavePath) && !string.IsNullOrWhiteSpace(file.Path))
                                    {
                                        var altPath = Path.Combine(task.Manager.SavePath, file.Path);
                                        if (!string.Equals(altPath, filePath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            await this.DeleteFileWithRetryAsync(altPath);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Warn(ex, "Failed to delete file {0} for torrent id {1}", file.Path, torrentId);
                                }
                            }
                        }

                        var containingDir = task.Manager.ContainingDirectory;
                        if (!string.IsNullOrWhiteSpace(containingDir) && this.diskProvider.FolderExists(containingDir))
                        {
                            var dirName = Path.GetFileName(containingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            var incompleteDir = this.storagePathService.GetIncompleteDirectory();
                            var downloadDir = this.storagePathService.GetCompletedDirectory(null);
                            var categoryDownloadDir = !string.IsNullOrWhiteSpace(task.Category) ? this.storagePathService.GetCompletedDirectory(task.Category) : null;

                            var torrentName = task.Manager.Torrent?.Name ?? string.Empty;
                            var sanitizedTorrentName = TorrentPathValidator.SanitizeRelativePath(torrentName);

                            var isMatchingName = string.Equals(dirName, torrentName, StringComparison.OrdinalIgnoreCase) ||
                                                 (!string.IsNullOrWhiteSpace(sanitizedTorrentName) && string.Equals(dirName, sanitizedTorrentName, StringComparison.OrdinalIgnoreCase));
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
                            var isRootCategory = !string.IsNullOrWhiteSpace(categoryDownloadDir) &&
                                string.Equals(
                                    Path.GetFullPath(containingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                    Path.GetFullPath(categoryDownloadDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                    StringComparison.OrdinalIgnoreCase);

                            if (isMatchingName && !isRootIncomplete && !isRootDownload && !isRootCategory)
                            {
                                await this.DeleteFolderWithRetryAsync(containingDir);
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
        lock (this.pendingTorrentsLock)
        {
            var pendingIndex = this.pendingTorrents.FindIndex(p => p.Torrent?.Id == torrentId);
            if (pendingIndex >= 0)
            {
                this.pendingTorrents[pendingIndex].Torrent.Status = TorrentStatus.Paused;
            }
        }

        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            task.WasAutoPausedByDiskSpace = false;
            await task.Manager.PauseAsync();
            this.logger.Info("Paused torrent id {0}", torrentId);
        }
    }

    public async Task ResumeTorrentAsync(int torrentId)
    {
        if (this.isHaltedByKillSwitch)
        {
            this.logger.Warn("Cannot resume torrent id {0}: VPN Kill Switch is active (fail-closed).", torrentId);
            return;
        }

        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0 ? this.configService.LowDiskSpaceThresholdMb : 500;
            var thresholdBytes = thresholdMb * 1024L * 1024L;
            var targetPath = task.WorkingPath ?? task.SavePath ?? task.Manager?.SavePath;
            if (!string.IsNullOrWhiteSpace(targetPath) && task.Progress < 1.0)
            {
                var availableSpace = this.diskProvider.GetAvailableSpace(targetPath);
                if (availableSpace.HasValue && availableSpace.Value < thresholdBytes)
                {
                    task.SetStorageFull($"StorageFull: Free disk space dropped below threshold ({availableSpace.Value / (1024 * 1024)} MB available, {thresholdMb} MB required).", this.eventAggregator);
                    this.logger.Warn("Cannot resume torrent id {0}: Insufficient free disk space ({1} MB available, {2} MB threshold).", torrentId, availableSpace.Value / (1024 * 1024), thresholdMb);
                    return;
                }
            }
        }

        lock (this.pendingTorrentsLock)
        {
            var pendingIndex = this.pendingTorrents.FindIndex(p => p.Torrent?.Id == torrentId);
            if (pendingIndex >= 0)
            {
                this.pendingTorrents[pendingIndex].Torrent.Status = TorrentStatus.Downloading;
            }
        }

        if (this.tasks.TryGetValue(torrentId, out var activeTask) && activeTask.Manager != null)
        {
            activeTask.ClearStorageFull(this.eventAggregator);
            activeTask.ClearTrackerStalled(this.eventAggregator);
            await activeTask.Manager.StartAsync();
            try
            {
                if (activeTask.Manager.TrackerManager != null)
                {
                    _ = this.AnnounceTrackersAsync(activeTask.Manager, torrentId, isResumeOrStartup: true);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to announce tracker on resume for torrent {0}", torrentId);
            }

            this.logger.Info("Resumed torrent id {0}", torrentId);
        }
    }

    public async Task PauseAllTorrentsAsync()
    {
        foreach (var task in this.tasks.Values)
        {
            task.WasAutoPausedByDiskSpace = false;
            if (task.Manager != null && task.Manager.State is not (TorrentState.Stopped or TorrentState.Paused))
            {
                try
                {
                    await task.Manager.PauseAsync();
                    this.logger.Info("Paused torrent id {0}", task.TorrentId);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to pause torrent id {0}", task.TorrentId);
                }
            }
        }
    }

    public Task PauseAllAsync() => this.PauseAllTorrentsAsync();

    public Task ResumeAllAsync() => this.ResumeAllTorrentsAsync();

    public async Task ResumeAllTorrentsAsync()
    {
        if (this.isHaltedByKillSwitch)
        {
            this.logger.Warn("Cannot resume all torrents: VPN Kill Switch is active (fail-closed).");
            return;
        }

        var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0 ? this.configService.LowDiskSpaceThresholdMb : 500;
        var thresholdBytes = thresholdMb * 1024L * 1024L;

        foreach (var task in this.tasks.Values)
        {
            if (task.Manager != null)
            {
                var targetPath = task.WorkingPath ?? task.SavePath ?? task.Manager.SavePath;
                if (!string.IsNullOrWhiteSpace(targetPath) && task.Progress < 1.0)
                {
                    var availableSpace = this.diskProvider.GetAvailableSpace(targetPath);
                    if (availableSpace.HasValue && availableSpace.Value < thresholdBytes)
                    {
                        task.SetStorageFull($"StorageFull: Free disk space dropped below threshold ({availableSpace.Value / (1024 * 1024)} MB available, {thresholdMb} MB required).", this.eventAggregator);
                        this.logger.Warn("Skipping resume for torrent id {0}: Insufficient free disk space.", task.TorrentId);
                        continue;
                    }
                }

                try
                {
                    task.ClearStorageFull(this.eventAggregator);
                    task.ClearTrackerStalled(this.eventAggregator);
                    await task.Manager.StartAsync();
                    try
                    {
                        if (task.Manager.TrackerManager != null)
                        {
                            _ = this.AnnounceTrackersAsync(task.Manager, task.TorrentId, isResumeOrStartup: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Failed to announce tracker on resume for torrent {0}", task.TorrentId);
                    }

                    this.logger.Info("Resumed torrent id {0}", task.TorrentId);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to resume torrent id {0}", task.TorrentId);
                }
            }
        }
    }

    public async Task ForceRecheckAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            var manager = task.Manager;

            var isAnotherHashing = this.tasks.Values.Any(t => t.TorrentId != torrentId && (t.Manager?.State == TorrentState.Hashing || t.Status == TorrentStatus.Checking));
            if (isAnotherHashing)
            {
                task.IsQueuedForRecheck = true;
                task.IsExplicitRecheck = true;
                this.eventAggregator?.PublishEvent(new TorrentStatusChangedEvent
                {
                    Torrent = new CoreTorrent
                    {
                        Id = torrentId,
                        InfoHash = task.InfoHash,
                        Name = task.Manager.Torrent?.Name ?? task.InfoHash,
                        Status = TorrentStatus.QueuedForChecking,
                        Category = task.Category,
                        SavePath = task.SavePath,
                        Progress = task.Progress,
                    },
                    OldStatus = task.Status,
                    NewStatus = TorrentStatus.QueuedForChecking,
                });
                this.logger.Info("Queued force recheck for torrent id {0} (another hash check is currently active)", torrentId);
                return;
            }

            task.IsQueuedForRecheck = false;
            task.IsExplicitRecheck = true;

            if (manager.State is not (TorrentState.Stopped or TorrentState.Paused))
            {
                await manager.StopAsync().ConfigureAwait(false);
            }

            // Determine if the files reside in completed destination or custom working path
            var completedDir = this.storagePathService.GetCompletedDirectory(task.Category);
            var candidatePaths = new List<string>();

            var incompleteDir = this.storagePathService?.GetIncompleteDirectory();
            if (!string.IsNullOrWhiteSpace(incompleteDir))
            {
                candidatePaths.Add(incompleteDir);
            }

            if (!string.IsNullOrWhiteSpace(completedDir))
            {
                candidatePaths.Add(completedDir);
            }

            var downloadDir = this.configService?.DownloadDir;
            if (!string.IsNullOrWhiteSpace(downloadDir))
            {
                candidatePaths.Add(downloadDir);
            }

            if (!string.IsNullOrWhiteSpace(task.WorkingPath))
            {
                candidatePaths.Add(task.WorkingPath);
                var parentDir = Path.GetDirectoryName(task.WorkingPath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    candidatePaths.Add(parentDir);
                }
            }

            if (!string.IsNullOrWhiteSpace(task.SavePath))
            {
                candidatePaths.Add(task.SavePath);
                var parentDir = Path.GetDirectoryName(task.SavePath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    candidatePaths.Add(parentDir);
                }
            }

            if (!string.IsNullOrWhiteSpace(manager.SavePath))
            {
                candidatePaths.Add(manager.SavePath);
                var parentDir = Path.GetDirectoryName(manager.SavePath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    candidatePaths.Add(parentDir);
                }
            }

            string matchedSavePath = null;
            long maxExistingBytes = 0;
            var filePaths = new List<string>();
            if (manager.Torrent?.Files != null)
            {
                foreach (var f in manager.Torrent.Files)
                {
                    filePaths.Add(f.Path);
                }
            }
            else if (manager.Files != null)
            {
                foreach (var f in manager.Files)
                {
                    filePaths.Add(f.Path);
                }
            }

            var isMultiFile = (manager.Torrent != null && manager.Torrent.Files.Count > 1) ||
                              (manager.Files != null && manager.Files.Count > 1);

            foreach (var p in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(p))
                {
                    continue;
                }

                if (filePaths.Count > 0)
                {
                    long totalBytes = 0;
                    foreach (var path in filePaths)
                    {
                        var full = isMultiFile && !string.IsNullOrWhiteSpace(manager.Torrent?.Name)
                            ? Path.Combine(p, manager.Torrent.Name, path)
                            : Path.Combine(p, path);

                        if (File.Exists(full))
                        {
                            totalBytes += new FileInfo(full).Length;
                        }
                        else if (File.Exists(full + ".!mt"))
                        {
                            totalBytes += new FileInfo(full + ".!mt").Length;
                        }
                        else if (File.Exists(full + ".incomplete"))
                        {
                            totalBytes += new FileInfo(full + ".incomplete").Length;
                        }
                    }

                    if (totalBytes > maxExistingBytes)
                    {
                        maxExistingBytes = totalBytes;
                        matchedSavePath = p;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(matchedSavePath) &&
                !string.Equals(manager.SavePath, matchedSavePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await manager.MoveFilesAsync(matchedSavePath, false).ConfigureAwait(false);
                    this.logger.Info("Aligned MonoTorrent save path to '{0}' prior to forced hash recheck for torrent id {1}", matchedSavePath, torrentId);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to align MonoTorrent save path to '{0}' prior to forced hash recheck for torrent id {1}", matchedSavePath, torrentId);
                }
            }

            if (this.engine?.DiskManager != null && manager != null)
            {
                try
                {
                    await this.engine.DiskManager.FlushAsync(manager).ConfigureAwait(false);
                    await this.CloseDiskManagerFilesAsync(manager).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to flush and close cached file handles for torrent {0} before hash check", torrentId);
                }
            }

            await manager.HashCheckAsync(autoStart: true).ConfigureAwait(false);
            this.logger.Info("Triggered hash recheck for torrent id {0}", torrentId);
        }
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        if (this.isHaltedByKillSwitch)
        {
            this.logger.Warn("Cannot force announce torrent id {0}: VPN Kill Switch is active (fail-closed).", torrentId);
            this.torrentLogService?.Log(torrentId, "Warn", "Tracker", "Cannot announce to tracker: VPN Kill Switch is active (fail-closed)");
            return;
        }

        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null)
        {
            if (task.Manager.State is TorrentState.Stopped or TorrentState.Stopping or TorrentState.Paused)
            {
                this.logger.Warn("Cannot force announce torrent id {0}: torrent is in {1} state.", torrentId, task.Manager.State);
                this.torrentLogService?.Log(torrentId, "Warn", "Tracker", $"Cannot announce to tracker: torrent is in {task.Manager.State} state");
                return;
            }

            if (task.Manager.TrackerManager != null)
            {
                var (dispatchedCount, unit) = await this.AnnounceTrackersAsync(task.Manager, torrentId, isResumeOrStartup: false).ConfigureAwait(false);
                try
                {
                    await task.Manager.TrackerManager.ScrapeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Tracker scrape on force announce failed for torrent id {0}", torrentId);
                }

                this.logger.Info("Dispatched announce and scrape request for torrent id {0} to {1} {2}", torrentId, dispatchedCount, unit);
                this.torrentLogService?.Log(torrentId, "Info", "Tracker", $"Dispatched announce & scrape request to {dispatchedCount} {unit} to discover peers");
            }
        }
    }

    private async Task<(int DispatchedCount, string Unit)> AnnounceTrackersAsync(TorrentManager manager, int torrentId, bool isResumeOrStartup = false)
    {
        if (manager == null || manager.TrackerManager == null)
        {
            return (0, "tracker(s)");
        }

        if (manager.State is TorrentState.Stopped or TorrentState.Stopping or TorrentState.Paused)
        {
            this.logger.Debug("Skipping tracker announce for torrent id {0}: torrent is in {1} state.", torrentId, manager.State);
            return (0, "tracker(s)");
        }

        var announceToAllInTier = this.configService?.AnnounceToAllInTier == true;
        var announceToAllTiers = this.configService?.AnnounceToAllTiers == true;

        var dispatchedCount = 0;
        var unit = "tracker(s)";

        try
        {
            if (announceToAllInTier)
            {
                unit = "tracker(s)";
                var tiers = manager.TrackerManager.Tiers?.ToList();
                if (tiers != null)
                {
                    foreach (var tier in tiers)
                    {
                        var trackers = tier?.Trackers?.ToList();
                        if (trackers != null)
                        {
                            foreach (var tracker in trackers)
                            {
                                if (tracker != null)
                                {
                                    try
                                    {
                                        await manager.TrackerManager.AnnounceAsync(tracker, CancellationToken.None).ConfigureAwait(false);
                                        dispatchedCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        this.logger.Debug(ex, "Failed to announce to tracker {0} for torrent {1}", tracker.Uri, torrentId);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (announceToAllTiers)
            {
                unit = "tier(s)";
                var tiers = manager.TrackerManager.Tiers?.ToList();
                if (tiers != null)
                {
                    foreach (var tier in tiers)
                    {
                        var tracker = tier?.ActiveTracker ?? tier?.Trackers?.FirstOrDefault();
                        if (tracker != null)
                        {
                            try
                            {
                                await manager.TrackerManager.AnnounceAsync(tracker, CancellationToken.None).ConfigureAwait(false);
                                dispatchedCount++;
                            }
                            catch (Exception ex)
                            {
                                this.logger.Debug(ex, "Failed to announce to tracker tier for torrent {0}", torrentId);
                            }
                        }
                    }
                }
            }
            else
            {
                unit = "tracker(s)";
                var hasAnyTrackers = manager.TrackerManager.Tiers?.Any(t => t.Trackers.Count > 0) == true;
                if (hasAnyTrackers)
                {
                    try
                    {
                        await manager.TrackerManager.AnnounceAsync(CancellationToken.None).ConfigureAwait(false);
                        dispatchedCount = 1;
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Failed to announce to default tracker for torrent {0}", torrentId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error announcing trackers for torrent {0}", torrentId);
        }

        if (isResumeOrStartup)
        {
            this.logger.Info("Dispatched startup/resume announce request for torrent id {0} to {1} {2}", torrentId, dispatchedCount, unit);
        }

        return (dispatchedCount, unit);
    }

    public async Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null && trackers != null)
        {
            if (task.Manager.TrackerManager != null)
            {
                var isProxyConfigured = this.configService.ProxyType?.ToLowerInvariant() is "socks5" or "http" &&
                    !string.IsNullOrWhiteSpace(this.configService.ProxyHost);
                var isProxyActive = isProxyConfigured || this.configService.ForceProxy || (this.networkBindingService?.ActiveProvider is IProxyTunnelBindingProvider);

                var trackerList = trackers.Where(tr => !string.IsNullOrWhiteSpace(tr)).Select(tr => tr.Trim()).ToList();
                if (trackerList.Count == 0)
                {
                    return;
                }

                if (this.trackerEntryRepository != null)
                {
                    var dbTiers = this.trackerEntryRepository.GetByTorrentId(torrentId)
                        .Where(e => !string.IsNullOrWhiteSpace(e.Url))
                        .ToDictionary(e => e.Url.Trim(), e => e.Tier, StringComparer.OrdinalIgnoreCase);

                    trackerList = trackerList
                        .OrderBy(t => dbTiers.TryGetValue(t, out var tier) ? tier : 1)
                        .ToList();
                }

                var existingUris = task.Manager.TrackerManager.Tiers?
                    .SelectMany(t => t.Trackers)
                    .Where(t => t?.Uri != null)
                    .Select(t => t.Uri)
                    .ToHashSet() ?? new HashSet<Uri>();

                var newlyAddedTrackers = new List<MonoTorrent.Trackers.ITracker>();

                foreach (var tr in trackerList)
                {
                    if (Uri.TryCreate(tr, UriKind.Absolute, out var uri))
                    {
                        if (existingUris.Contains(uri))
                        {
                            continue;
                        }

                        if (isProxyActive && string.Equals(uri.Scheme, "udp", StringComparison.OrdinalIgnoreCase))
                        {
                            this.logger.Warn("Skipping UDP tracker '{0}' on torrent {1}: proxy is active and unproxied UDP announces are blocked to prevent IP leaks", tr, torrentId);
                            continue;
                        }

                        try
                        {
                            await task.Manager.TrackerManager.AddTrackerAsync(uri);
                            existingUris.Add(uri);

                            var addedTracker = task.Manager.TrackerManager.Tiers?
                                .SelectMany(t => t.Trackers)
                                .FirstOrDefault(t => t?.Uri == uri);

                            if (addedTracker != null)
                            {
                                newlyAddedTrackers.Add(addedTracker);
                            }
                        }
                        catch (Exception ex)
                        {
                            this.logger.Debug(ex, "Could not add tracker {0} to torrent {1}", tr, torrentId);
                        }
                    }
                }

                if (newlyAddedTrackers.Count > 0)
                {
                    // Rate-limit batch announces to prevent unthrottled announce storm across all trackers
                    var toAnnounce = newlyAddedTrackers.Where(t => t.Status != MonoTorrent.Trackers.TrackerState.Connecting).Take(10).ToList();
                    foreach (var tracker in toAnnounce)
                    {
                        try
                        {
                            await task.Manager.TrackerManager.AnnounceAsync(tracker, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            this.logger.Debug(ex, "Failed to announce newly added tracker {0} for torrent {1}", tracker.Uri, torrentId);
                        }
                    }
                }
            }
        }
    }

    public async Task RemoveTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null && trackers != null)
        {
            if (task.Manager.TrackerManager != null)
            {
                var targetUrls = trackers
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (targetUrls.Count == 0)
                {
                    return;
                }

                var trackersToRemove = new List<MonoTorrent.Trackers.ITracker>();
                var tiers = task.Manager.TrackerManager.Tiers;
                if (tiers != null)
                {
                    foreach (var tier in tiers)
                    {
                        if (tier?.Trackers != null)
                        {
                            foreach (var tracker in tier.Trackers)
                            {
                                if (tracker?.Uri != null)
                                {
                                    var uriString = tracker.Uri.OriginalString;
                                    var absString = tracker.Uri.ToString();
                                    if (targetUrls.Contains(uriString) || targetUrls.Contains(absString))
                                    {
                                        trackersToRemove.Add(tracker);
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var tracker in trackersToRemove)
                {
                    try
                    {
                        await task.Manager.TrackerManager.RemoveTrackerAsync(tracker);
                        this.logger.Debug("Removed tracker {0} from torrent {1}", tracker.Uri, torrentId);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Could not remove tracker {0} from torrent {1}", tracker.Uri, torrentId);
                    }
                }
            }
        }
    }

    public async Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        if (this.tasks.TryGetValue(torrentId, out var task) && task.Manager != null && task.Manager.Files != null)
        {
            var normalizedPath = filePath?.Replace('\\', '/').TrimStart('/');
            var targetFile = task.Manager.Files.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Path) &&
                f.Path.Replace('\\', '/').TrimStart('/').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
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
                    var files = task.Manager.Files;
                    for (var p = targetFile.StartPieceIndex; p <= targetFile.EndPieceIndex; p++)
                    {
                        var effectivePickerPrio = 0;
                        foreach (var file in files)
                        {
                            if (p >= file.StartPieceIndex && p <= file.EndPieceIndex)
                            {
                                var filePrio = file.Priority switch
                                {
                                    MonoTorrent.Priority.DoNotDownload => 0,
                                    MonoTorrent.Priority.Lowest or MonoTorrent.Priority.Low => 1,
                                    MonoTorrent.Priority.Normal => 1,
                                    MonoTorrent.Priority.High => 2,
                                    MonoTorrent.Priority.Highest => 3,
                                    _ => 1,
                                };
                                if (filePrio > effectivePickerPrio)
                                {
                                    effectivePickerPrio = filePrio;
                                }
                            }
                        }

                        task.Picker.SetPiecePriority(p, effectivePickerPrio);
                    }
                }

                this.logger.Info("Updated file priority for {0} (file: {1}, priority: {2})", task.InfoHash, filePath, monoPriority);
            }
        }
    }

    private async Task ApplyStoredFilePrioritiesAsync(MonoTorrentDownloadTask task)
    {
        if (task?.Manager == null || task.Manager.Files == null || this.torrentFileRepository == null)
        {
            return;
        }

        try
        {
            var storedFiles = this.torrentFileRepository.GetByTorrentId(task.TorrentId)?.ToList();
            if (storedFiles == null || storedFiles.Count == 0)
            {
                return;
            }

            var managerFiles = task.Manager.Files;
            foreach (var sf in storedFiles)
            {
                var normalizedPath = sf.Path?.Replace('\\', '/').TrimStart('/');
                var targetFile = managerFiles.FirstOrDefault(f =>
                    !string.IsNullOrEmpty(f.Path) &&
                    f.Path.Replace('\\', '/').TrimStart('/').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

                if (targetFile != null)
                {
                    var monoPriority = sf.Priority switch
                    {
                        0 => MonoTorrent.Priority.DoNotDownload,
                        1 => MonoTorrent.Priority.Lowest,
                        2 => MonoTorrent.Priority.Low,
                        3 => MonoTorrent.Priority.Normal,
                        4 => MonoTorrent.Priority.High,
                        5 => MonoTorrent.Priority.Highest,
                        _ => MonoTorrent.Priority.Normal,
                    };

                    await task.Manager.SetFilePriorityAsync(targetFile, monoPriority).ConfigureAwait(false);
                }
            }

            if (task.Picker != null)
            {
                for (var p = 0; p < task.Picker.PieceCount; p++)
                {
                    var effectivePickerPrio = 0;
                    var hasOverlappingFile = false;

                    foreach (var file in managerFiles)
                    {
                        if (p >= file.StartPieceIndex && p <= file.EndPieceIndex)
                        {
                            hasOverlappingFile = true;
                            var filePrio = file.Priority switch
                            {
                                MonoTorrent.Priority.DoNotDownload => 0,
                                MonoTorrent.Priority.Lowest or MonoTorrent.Priority.Low => 1,
                                MonoTorrent.Priority.Normal => 1,
                                MonoTorrent.Priority.High => 2,
                                MonoTorrent.Priority.Highest => 3,
                                _ => 1,
                            };
                            if (filePrio > effectivePickerPrio)
                            {
                                effectivePickerPrio = filePrio;
                            }
                        }
                    }

                    if (hasOverlappingFile)
                    {
                        task.Picker.SetPiecePriority(p, effectivePickerPrio);
                    }
                }
            }

            this.logger.Info("Applied stored file priorities for torrent {0} ({1})", task.TorrentId, task.InfoHash);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to apply stored file priorities for torrent {0}", task.TorrentId);
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

    public async Task MoveTorrentFilesAsync(int torrentId, string newSavePath, bool moveFiles = true)
    {
        if (string.IsNullOrWhiteSpace(newSavePath))
        {
            throw new ArgumentException("New save path must not be empty.", nameof(newSavePath));
        }

        if (!this.tasks.TryGetValue(torrentId, out var task) || task.Manager == null)
        {
            this.logger.Warn("Cannot move torrent files: torrent id {0} not found in active engine tasks", torrentId);
            return;
        }

        var oldSavePath = task.Manager.SavePath;
        var existingPriorities = task.Manager.Files?.Select(f => (File: f, f.Priority)).ToList();

        try
        {
            Directory.CreateDirectory(newSavePath);
            await task.Manager.MoveFilesAsync(newSavePath, moveFiles).ConfigureAwait(false);

            task.WorkingPath = newSavePath;
            task.SavePath = newSavePath;

            if (task.IsStorageFull)
            {
                var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0 ? this.configService.LowDiskSpaceThresholdMb : 500;
                var thresholdBytes = thresholdMb * 1024L * 1024L;
                var availableSpace = this.diskProvider.GetAvailableSpace(newSavePath);
                if (!availableSpace.HasValue || availableSpace.Value >= thresholdBytes)
                {
                    task.ClearStorageFull(this.eventAggregator);
                }
            }

            if (existingPriorities != null)
            {
                foreach (var (file, priority) in existingPriorities)
                {
                    if (file.Priority != priority)
                    {
                        await task.Manager.SetFilePriorityAsync(file, priority).ConfigureAwait(false);
                    }
                }
            }

            this.logger.Info("Successfully moved files for torrent {0} to '{1}' (moveFiles={2})", torrentId, newSavePath, moveFiles);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to move files for torrent {0} to '{1}'", torrentId, newSavePath);
            if (moveFiles && !string.IsNullOrWhiteSpace(oldSavePath) && !string.Equals(oldSavePath, newSavePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    this.logger.Info("Attempting to rollback moved files for torrent {0} back to '{1}'", torrentId, oldSavePath);
                    await task.Manager.MoveFilesAsync(oldSavePath, true).ConfigureAwait(false);
                    task.WorkingPath = oldSavePath;
                    task.SavePath = oldSavePath;

                    if (existingPriorities != null)
                    {
                        foreach (var (file, priority) in existingPriorities)
                        {
                            if (file.Priority != priority)
                            {
                                await task.Manager.SetFilePriorityAsync(file, priority).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (Exception rollbackEx)
                {
                    this.logger.Error(rollbackEx, "Failed to rollback files for torrent {0} to '{1}'", torrentId, oldSavePath);
                }
            }

            throw;
        }
    }

    public async Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        if (this.engine != null)
        {
            var settingsBuilder = new EngineSettingsBuilder(this.engine.Settings)
            {
                MaximumDownloadRate = maxDownloadKbps > 0
                    ? (int)Math.Min((long)maxDownloadKbps * 1024, int.MaxValue)
                    : 0,
                MaximumUploadRate = maxUploadKbps > 0
                    ? (int)Math.Min((long)maxUploadKbps * 1024, int.MaxValue)
                    : 0,
                DiskCacheBytes = this.CalculateDynamicDiskCacheBytes(this.engine.TotalDownloadRate),
                DiskCachePolicy = this.GetConfiguredCachePolicy(),
                FastResumeMode = this.GetConfiguredFastResumeMode(),
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
                MaximumDownloadRate = maxDownloadKbps > 0
                    ? (int)Math.Min((long)maxDownloadKbps * 1024, int.MaxValue)
                    : 0,
                MaximumUploadRate = maxUploadKbps > 0
                    ? (int)Math.Min((long)maxUploadKbps * 1024, int.MaxValue)
                    : 0,
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
            if (isPrivate)
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
            this.logger.Info("Updated BEP 27 settings for {0} (IsPrivate: {1})", task.InfoHash, isPrivate);
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

    public Task SetSequentialDownloadAsync(int torrentId, bool enabled)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.SequentialDownload = enabled;
            this.logger.Info("Updated sequential download for torrent {0}: {1}", torrentId, enabled);
        }

        return Task.CompletedTask;
    }

    public Task SetFirstLastPiecePriorityAsync(int torrentId, bool enabled)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.FirstLastPiecePriority = enabled;
            if (task.Picker != null)
            {
                var pieceCount = task.Picker.PieceCount;
                if (pieceCount > 0)
                {
                    var headCount = Math.Max(1, Math.Min(4, pieceCount / 10));
                    var tailCount = Math.Max(1, Math.Min(2, pieceCount / 20));
                    var priority = enabled ? 3 : 1;

                    for (var i = 0; i < headCount && i < pieceCount; i++)
                    {
                        task.Picker.SetPiecePriority(i, priority);
                    }

                    for (var i = Math.Max(0, pieceCount - tailCount); i < pieceCount; i++)
                    {
                        task.Picker.SetPiecePriority(i, priority);
                    }
                }
            }

            this.logger.Info("Updated first/last piece priority for torrent {0}: {1}", torrentId, enabled);
        }

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

    private void OnTorrentStateChanged(object sender, TorrentStateChangedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.HandleTorrentStateChangedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error handling torrent state change");
            }
        });
    }

    public static TorrentStatus MapTorrentStateToStatus(TorrentState state)
    {
        return state switch
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

    public async Task HandleTorrentStateChangedAsync(TorrentStateChangedEventArgs e)
    {
        if (this.isEngineStopping)
        {
            return;
        }

        try
        {
            var manager = e.TorrentManager;
            var infoHash = manager.InfoHashes.V1OrV2.ToHex();

            if (this.infoHashToId.TryGetValue(infoHash, out var torrentId))
            {
                var torrentName = manager.Torrent?.Name ?? infoHash;
                var oldStatus = MapTorrentStateToStatus(e.OldState);
                var newStatus = MapTorrentStateToStatus(e.NewState);
                this.tasks.TryGetValue(torrentId, out var currentTask);
                if (newStatus == TorrentStatus.Downloading &&
                    (manager.Complete || (manager.Bitfield != null && manager.Bitfield.Length > 0 && manager.Bitfield.AllTrue) || currentTask?.IsFilesMovedToCompleted == true))
                {
                    newStatus = TorrentStatus.Seeding;
                }

                this.logger.Info(
                    "[State Machine] Torrent #{0} ('{1}', hash: {2}) state changed: {3} -> {4} (status: {5} -> {6}) | Progress: {7:F2}% | Seeds: {8}, Leechs: {9}",
                    torrentId,
                    torrentName,
                    infoHash,
                    e.OldState,
                    e.NewState,
                    oldStatus,
                    newStatus,
                    manager.Progress,
                    manager.Peers?.Seeds ?? 0,
                    manager.Peers?.Leechs ?? 0);

                if (oldStatus != newStatus)
                {
                    var coreTorrent = new CoreTorrent
                    {
                        Id = torrentId,
                        InfoHash = infoHash,
                        Name = manager.Torrent?.Name ?? infoHash,
                        Status = newStatus,
                        Category = currentTask?.Category,
                        SavePath = currentTask?.SavePath ?? this.storagePathService?.GetCompletedDirectory(currentTask?.Category) ?? this.configService?.DownloadDir ?? "/downloads",
                        Progress = manager.Progress / 100.0,
                    };

                    this.eventAggregator?.PublishEvent(new TorrentStatusChangedEvent
                    {
                        Torrent = coreTorrent,
                        OldStatus = oldStatus,
                        NewStatus = newStatus,
                    });
                }

                if (manager.Torrent != null && manager.Torrent.IsPrivate)
                {
                    if (manager.Settings.AllowDht || manager.Settings.AllowPeerExchange)
                    {
                        var strictSettings = new TorrentSettingsBuilder(manager.Settings)
                        {
                            AllowDht = false,
                            AllowPeerExchange = false,
                        }.ToSettings();
                        await manager.UpdateSettingsAsync(strictSettings).ConfigureAwait(false);
                        this.logger.Info("[State Machine] Enforced BEP 27 restrictions for private torrent #{0} ('{1}') (DHT/PEX disabled)", torrentId, torrentName);
                    }
                }

                if (manager.Torrent != null && (e.OldState == TorrentState.Metadata || e.NewState == TorrentState.Downloading || e.NewState == TorrentState.Starting))
                {
                    var taskCompleted = this.tasks.TryGetValue(torrentId, out var activeT) && activeT.IsFilesMovedToCompleted;
                    var isComplete = taskCompleted ||
                                     manager.Complete ||
                                     (manager.Bitfield != null && manager.Bitfield.Length > 0 && manager.Bitfield.AllTrue) ||
                                     manager.Progress >= 99.99 ||
                                     e.NewState == TorrentState.Seeding;

                    if (!isComplete)
                    {
                        await this.PreallocateFilesAsync(manager, manager.SavePath, manager.Torrent).ConfigureAwait(false);
                    }
                }

                if (e.NewState == TorrentState.Hashing)
                {
                    if (this.tasks.TryGetValue(torrentId, out var activeTask))
                    {
                        activeTask.IsQueuedForRecheck = false;
                    }

                    this.logger.Info("[State Machine] Torrent #{0} ('{1}') started data integrity hash check on disk.", torrentId, torrentName);
                    this.torrentLogService?.Log(torrentId, "Info", "Storage", "Data integrity hash check in progress...");
                }
                else if (e.OldState == TorrentState.Hashing)
                {
                    if (this.tasks.TryGetValue(torrentId, out var activeTask))
                    {
                        var wasExplicitRecheck = activeTask.IsExplicitRecheck;
                        activeTask.IsExplicitRecheck = false;
                        activeTask.IsQueuedForRecheck = false;
                        var allVerified = manager.Complete || (manager.Bitfield != null && manager.Bitfield.Length > 0 && manager.Bitfield.AllTrue);
                        if (!allVerified && activeTask.IsFilesMovedToCompleted && wasExplicitRecheck)
                        {
                            activeTask.IsFilesMovedToCompleted = false;
                            this.logger.Warn("[State Machine] Torrent #{0} ('{1}') failed hash check after completion ({2:F1}% verified). Resetting completed latch and resuming download to fetch missing pieces.", torrentId, torrentName, manager.Progress);
                            this.torrentLogService?.Log(torrentId, "Warn", "Storage", $"Hash check detected missing pieces ({manager.Progress:F1}% verified). Resuming download to repair.");
                        }
                    }

                    this.logger.Info("[State Machine] Torrent #{0} ('{1}') finished data integrity hash check ({2:F1}% verified). Next state: {3}", torrentId, torrentName, manager.Progress, e.NewState);
                    this.torrentLogService?.Log(torrentId, "Info", "Storage", $"Data integrity check finished ({manager.Progress:F1}% verified). Next state: {e.NewState}");
                    GC.Collect(2, GCCollectionMode.Forced, false);

                    var nextQueued = this.tasks.Values.FirstOrDefault(t => t.IsQueuedForRecheck && t.Manager != null);
                    if (nextQueued != null)
                    {
                        this.logger.Info("[State Machine] Triggering next queued hash check for torrent id {0} ('{1}')", nextQueued.TorrentId, nextQueued.Manager?.Torrent?.Name ?? nextQueued.InfoHash);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await this.ForceRecheckAsync(nextQueued.TorrentId).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Failed to start next queued hash check for torrent id {0}", nextQueued.TorrentId);
                            }
                        });
                    }
                }

                if (e.NewState == TorrentState.Error)
                {
                    this.logger.Error("[State Machine] Torrent #{0} ('{1}') entered Error state: Reason={2}, Exception={3}", torrentId, torrentName, manager.Error.Reason, manager.Error.Exception?.Message);
                    this.torrentLogService?.Log(torrentId, "Error", "Engine", $"Torrent error: {manager.Error.Reason} ({manager.Error.Exception?.Message})");
                }

                if (e.NewState == TorrentState.Seeding)
                {
                    this.logger.Info("[State Machine] Torrent #{0} ('{1}') entered Seeding state (Progress: {2:F2}%). Triggering completion processing.", torrentId, torrentName, manager.Progress);
                    if (this.tasks.TryGetValue(torrentId, out var activeTask) && !activeTask.IsFilesMovedToCompleted)
                    {
                        await this.OnTorrentCompletedAsync(torrentId, infoHash, manager).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling torrent state change for {0}", e?.TorrentManager?.InfoHashes?.V1OrV2?.ToHex());
        }
    }

    public void OnTorrentCompleted(int torrentId, string infoHash, TorrentManager manager)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.OnTorrentCompletedAsync(torrentId, infoHash, manager).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error handling torrent completion for {0}", infoHash);
            }
        });
    }

    public async Task OnTorrentCompletedAsync(int torrentId, string infoHash, TorrentManager manager)
    {
        this.tasks.TryGetValue(torrentId, out var existingTask);
        var category = existingTask?.Category;
        var torrentName = manager?.Torrent?.Name ?? infoHash;

        if (manager == null)
        {
            return;
        }

        if (existingTask != null)
        {
            if (existingTask.IsFilesMovedToCompleted)
            {
                this.logger.Debug("Torrent {0} ({1}) files already moved to completed directory; skipping duplicate completion move.", torrentId, torrentName);
                return;
            }

            existingTask.IsFilesMovedToCompleted = true;
        }

        // Flush dirty write cache blocks to disk before moving files
        if (this.engine?.DiskManager != null)
        {
            try
            {
                await this.engine.DiskManager.FlushAsync(manager).ConfigureAwait(false);
                this.logger.Debug("Flushed dirty write cache blocks for completed torrent {0}", infoHash);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to flush dirty write cache blocks for torrent {0} before move", infoHash);
            }
        }

        var customSavePath = existingTask?.SavePath;
        var targetCompletedDir = this.storagePathService.GetCompletedDirectory(category);
        var downloadDir = this.configService?.DownloadDir ?? "/downloads";
        var seedingSavePath = !string.IsNullOrWhiteSpace(customSavePath)
            ? customSavePath
            : (!string.IsNullOrWhiteSpace(targetCompletedDir) ? targetCompletedDir : downloadDir);
        string finalDestination = null;

        var sourcePath = manager.SavePath ?? this.storagePathService.GetIncompleteDirectory();
        if (manager.Files != null && manager.Files.Count > 0)
        {
            var isMulti = manager.Files.Count > 1 || (manager.Torrent != null && manager.Torrent.Files.Count > 1);
            if (isMulti)
            {
                var folderName = manager.Torrent?.Name ?? torrentName;
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    sourcePath = Path.Combine(sourcePath, folderName);
                }
            }
            else
            {
                var firstFullPath = manager.Files[0].FullPath;
                if (!string.IsNullOrWhiteSpace(firstFullPath))
                {
                    sourcePath = firstFullPath;
                }
            }
        }

        var isCustomSavePath = !string.IsNullOrWhiteSpace(customSavePath) &&
            (!string.IsNullOrWhiteSpace(targetCompletedDir)
                ? !string.Equals(customSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), targetCompletedDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                : true) &&
            (!string.IsNullOrWhiteSpace(downloadDir)
                ? !string.Equals(customSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), downloadDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                : true);

        if (!isCustomSavePath)
        {
            try
            {
                // First notify StoragePathService for tracking and category hooks
                this.storagePathService.MoveToCompleted(sourcePath, category, torrentName, out finalDestination);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "StoragePathService.MoveToCompleted notification completed with exception for {0}", infoHash);
            }
        }

        // Relocate files in MonoTorrent if moving from incomplete to completed directory
        var currentManagerPath = manager.SavePath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetSeedingPath = seedingSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(seedingSavePath) &&
            !string.Equals(currentManagerPath, targetSeedingPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(seedingSavePath);
                await manager.MoveFilesAsync(seedingSavePath, true).ConfigureAwait(false);
                this.logger.Info("MonoTorrent moved completed files to '{0}' for torrent {1}", seedingSavePath, infoHash);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "MonoTorrent MoveFilesAsync to '{0}' for {1} failed; updating SavePath", seedingSavePath, infoHash);
                try
                {
                    await manager.MoveFilesAsync(seedingSavePath, false).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        finalDestination ??= seedingSavePath;
        this.storagePathService.StripIncompleteExtensions(seedingSavePath);
        this.storagePathService.EnsureAccessiblePermissions(seedingSavePath);

        if (existingTask != null)
        {
            existingTask.WorkingPath = seedingSavePath;
            existingTask.SavePath = seedingSavePath;
        }

        try
        {
            await this.SaveFastResumeAtomicAsync(manager, this.GetCacheDirectory()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to save FastResume checkpoint on completion for {0}", infoHash);
        }

        this.logger.Info("[State Machine] Torrent #{0} ('{1}') download completed (100% verified). Final destination: '{2}'. Publishing completion event.", torrentId, torrentName, finalDestination);
        this.eventAggregator.PublishEvent(new TorrentDownloadCompletedEvent(new CoreTorrent
        {
            Id = torrentId,
            InfoHash = infoHash,
            Name = torrentName,
            Status = TorrentStatus.Seeding,
            Category = category,
            SavePath = seedingSavePath,
            Progress = 1.0,
            DateCompleted = DateTime.UtcNow,
        }));
    }

    private async Task CloseDiskManagerFilesAsync(TorrentManager manager)
    {
        if (manager == null || this.engine?.DiskManager == null)
        {
            return;
        }

        try
        {
            var closeMethod = typeof(DiskManager).GetMethod("CloseFilesAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (closeMethod != null)
            {
                var task = closeMethod.Invoke(this.engine.DiskManager, new object[] { manager }) as Task;
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Non-critical error closing DiskManager files via reflection");
        }
    }

    private static readonly PropertyInfo MassiveBuffersProp = typeof(ByteBufferPool).GetProperty("MassiveBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly PropertyInfo SpinLockedValueProp = MassiveBuffersProp?.PropertyType.GetProperty("Value", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

    public static void TrimMonoTorrentMassiveBuffers()
    {
        try
        {
            if (MassiveBuffersProp != null && SpinLockedValueProp != null)
            {
                var spinLocked = MassiveBuffersProp.GetValue(MemoryPool.Default);
                if (spinLocked != null)
                {
                    if (SpinLockedValueProp.GetValue(spinLocked) is System.Collections.ICollection queue && queue.Count > 0)
                    {
                        var clearMethod = queue.GetType().GetMethod("Clear");
                        clearMethod?.Invoke(queue, null);
                    }
                }
            }
        }
        catch
        {
            // Ignore reflection errors on buffer trimming
        }
    }

    internal async Task<bool> CalculatePieceHashDirectAsync(ITorrentManagerInfo manager, int pieceIndex, PieceHash dest)
    {
        if (manager.TorrentInfo == null)
        {
            return false;
        }

        var pieceLength = (long)manager.TorrentInfo.PieceLength;
        var totalSize = manager.TorrentInfo.Size;
        var pieceStart = (long)pieceIndex * pieceLength;
        var pieceEnd = Math.Min(pieceStart + pieceLength, totalSize);
        var pieceBytesToRead = pieceEnd - pieceStart;

        if (pieceBytesToRead <= 0)
        {
            return false;
        }

        using var sha1 = !dest.V1Hash.IsEmpty ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
        using var sha256 = !dest.V2Hash.IsEmpty ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var currentOffset = pieceStart;
            var remaining = pieceBytesToRead;

            for (var i = 0; i < manager.Files.Count && remaining > 0; i++)
            {
                var file = manager.Files[i];
                var fileStart = file.OffsetInTorrent;
                var fileEnd = fileStart + file.Length;

                if (currentOffset >= fileStart && currentOffset < fileEnd)
                {
                    var fileOffset = currentOffset - fileStart;
                    var bytesInThisFile = Math.Min(remaining, fileEnd - currentOffset);

                    var candidatePaths = new List<string>();
                    if (!string.IsNullOrWhiteSpace(file.FullPath))
                    {
                        candidatePaths.Add(file.FullPath);
                        candidatePaths.Add(file.FullPath + ".!mt");
                        candidatePaths.Add(file.FullPath + ".!leech");
                        candidatePaths.Add(file.FullPath + ".incomplete");
                        candidatePaths.Add(file.FullPath + ".part");
                    }

                    if (!string.IsNullOrWhiteSpace(file.DownloadCompleteFullPath))
                    {
                        candidatePaths.Add(file.DownloadCompleteFullPath);
                        candidatePaths.Add(file.DownloadCompleteFullPath + ".!mt");
                        candidatePaths.Add(file.DownloadCompleteFullPath + ".!leech");
                    }

                    if (!string.IsNullOrWhiteSpace(file.DownloadIncompleteFullPath))
                    {
                        candidatePaths.Add(file.DownloadIncompleteFullPath);
                        candidatePaths.Add(file.DownloadIncompleteFullPath + ".!mt");
                        candidatePaths.Add(file.DownloadIncompleteFullPath + ".!leech");
                    }

                    var incompleteDir = this.storagePathService?.GetIncompleteDirectory();
                    var completedDir = this.storagePathService?.GetCompletedDirectory(null);
                    var torrentName = manager.TorrentInfo?.Name ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(file.Path))
                    {
                        var fileName = Path.GetFileName(file.Path);
                        if (!string.IsNullOrWhiteSpace(completedDir))
                        {
                            candidatePaths.Add(Path.Combine(completedDir, file.Path));
                            candidatePaths.Add(Path.Combine(completedDir, file.Path + ".!mt"));
                            candidatePaths.Add(Path.Combine(completedDir, torrentName, file.Path));
                            candidatePaths.Add(Path.Combine(completedDir, torrentName, file.Path + ".!mt"));
                            if (!string.IsNullOrWhiteSpace(fileName))
                            {
                                candidatePaths.Add(Path.Combine(completedDir, fileName));
                                candidatePaths.Add(Path.Combine(completedDir, fileName + ".!mt"));
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(incompleteDir))
                        {
                            candidatePaths.Add(Path.Combine(incompleteDir, file.Path));
                            candidatePaths.Add(Path.Combine(incompleteDir, file.Path + ".!mt"));
                            candidatePaths.Add(Path.Combine(incompleteDir, torrentName, file.Path));
                            candidatePaths.Add(Path.Combine(incompleteDir, torrentName, file.Path + ".!mt"));
                            if (!string.IsNullOrWhiteSpace(fileName))
                            {
                                candidatePaths.Add(Path.Combine(incompleteDir, fileName));
                                candidatePaths.Add(Path.Combine(incompleteDir, fileName + ".!mt"));
                            }
                        }
                    }

                    string filePath = null;
                    var validFiles = candidatePaths
                        .Where(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(c => new FileInfo(c))
                        .OrderByDescending(fi => fi.Length >= fileOffset + bytesInThisFile ? 2 : (fi.Length > 0 ? 1 : 0))
                        .ThenByDescending(fi => fi.Length)
                        .ToList();

                    if (validFiles.Count > 0)
                    {
                        filePath = validFiles[0].FullName;
                    }

                    if (File.Exists(filePath))
                    {
                        using var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.None);
                        var fileReadOffset = fileOffset;
                        var fileRemaining = bytesInThisFile;

                        while (fileRemaining > 0)
                        {
                            var toRead = (int)Math.Min(fileRemaining, buffer.Length);
                            var read = RandomAccess.Read(handle, buffer.AsSpan(0, toRead), fileReadOffset);
                            if (read <= 0)
                            {
                                Array.Clear(buffer, 0, toRead);
                                read = toRead;
                            }

                            sha1?.AppendData(buffer, 0, read);
                            sha256?.AppendData(buffer, 0, read);

                            fileReadOffset += read;
                            fileRemaining -= read;
                            currentOffset += read;
                            remaining -= read;
                        }
                    }
                    else
                    {
                        Array.Clear(buffer, 0, buffer.Length);
                        var fileRemaining = bytesInThisFile;
                        while (fileRemaining > 0)
                        {
                            var toRead = (int)Math.Min(fileRemaining, buffer.Length);
                            sha1?.AppendData(buffer, 0, toRead);
                            sha256?.AppendData(buffer, 0, toRead);
                            fileRemaining -= toRead;
                            currentOffset += toRead;
                            remaining -= toRead;
                        }
                    }
                }
            }

            if (sha1 != null && !dest.V1Hash.IsEmpty)
            {
                sha1.GetHashAndReset(dest.V1Hash.Span);
            }

            if (sha256 != null && !dest.V2Hash.IsEmpty)
            {
                sha256.GetHashAndReset(dest.V2Hash.Span);
            }

            return await Task.FromResult(true).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void OnPieceHashed(object sender, PieceHashedEventArgs e)
    {
        try
        {
            var manager = e.TorrentManager;
            var infoHash = manager.InfoHashes.V1OrV2.ToHex();

            var currentHashed = Interlocked.Increment(ref this.totalPiecesHashed);

            // Reclaim MonoTorrent MassiveBuffers queue and force LOH compaction every 4 pieces during hashing
            if (currentHashed % 4 == 0 || e.PieceIndex % 4 == 0)
            {
                TrimMonoTorrentMassiveBuffers();
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }

            if (this.infoHashToId.TryGetValue(infoHash, out var torrentId))
            {
                if (this.tasks.TryGetValue(torrentId, out var task))
                {
                    var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0 ? this.configService.LowDiskSpaceThresholdMb : 500;
                    var thresholdBytes = thresholdMb * 1024L * 1024L;
                    task.CheckDiskSpace(this.diskProvider, thresholdBytes, this.eventAggregator);
                }

                if (e.HashPassed)
                {
                    this.logger.Trace("Piece {0} verified for torrent {1} (progress: {2:P1})", e.PieceIndex, infoHash, manager.Progress / 100.0);
                    this.eventAggregator.PublishEvent(new PieceVerifiedEvent(torrentId, e.PieceIndex));
                }
                else
                {
                    Interlocked.Increment(ref this.totalHashFails);
                    this.logger.Debug("Piece {0} failed hash check for torrent {1}", e.PieceIndex, infoHash);
                }
            }
            else if (!e.HashPassed)
            {
                Interlocked.Increment(ref this.totalHashFails);
                this.logger.Debug("Piece {0} failed hash check for torrent {1}", e.PieceIndex, infoHash);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error handling piece hashed event");
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
                var isInactive = task.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued;
                if (mon != null && !isInactive)
                {
                    totalDownSpeed += mon.DownloadRate;
                    totalUpSpeed += mon.UploadRate;
                }

                if (mon != null)
                {
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

        var totalBytesAll = totalDataDown + totalDataUp + totalProtoDown + totalProtoUp;
        var totalProtoAll = totalProtoDown + totalProtoUp;
        var protoOverheadPct = totalBytesAll > 0 ? Math.Round(((double)totalProtoAll / totalBytesAll) * 100.0, 2) : 0.0;

        var now = DateTime.UtcNow;
        var protoElapsedSec = Math.Max(0.001, (now - this.lastProtocolSample).TotalSeconds);
        var protoDownSpeed = this.lastProtoDownSpeed;
        var protoUpSpeed = this.lastProtoUpSpeed;

        if (protoElapsedSec >= 0.5)
        {
            var deltaProtoDown = totalProtoDown - this.lastTotalProtoDown;
            var deltaProtoUp = totalProtoUp - this.lastTotalProtoUp;

            protoDownSpeed = deltaProtoDown >= 0 ? (long)Math.Max(0, Math.Round(deltaProtoDown / protoElapsedSec)) : 0;
            protoUpSpeed = deltaProtoUp >= 0 ? (long)Math.Max(0, Math.Round(deltaProtoUp / protoElapsedSec)) : 0;

            this.lastTotalProtoDown = totalProtoDown;
            this.lastTotalProtoUp = totalProtoUp;
            this.lastProtocolSample = now;
            this.lastProtoDownSpeed = protoDownSpeed;
            this.lastProtoUpSpeed = protoUpSpeed;
        }

        var elapsedSec = Math.Max(0.5, (now - this.lastPieceHashSample).TotalSeconds);
        var currentHashed = Interlocked.Read(ref this.totalPiecesHashed);
        var piecesDelta = currentHashed - this.lastPiecesHashedCount;
        this.lastPieceHashSample = now;
        this.lastPiecesHashedCount = currentHashed;
        var piecesPerSec = Math.Round(piecesDelta / elapsedSec, 1);

        var diskCacheCap = this.engine != null
            ? (long)this.engine.Settings.DiskCacheBytes
            : (long)this.CalculateDynamicDiskCacheBytes();

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

        int halfOpenConns;
        lock (this.activePeerListeners)
        {
            halfOpenConns = this.activePeerListeners.Sum(l => l.HalfOpenConnections);
        }

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
            TotalProtocolDownloadSpeed = protoDownSpeed,
            TotalProtocolUploadSpeed = protoUpSpeed,
            TotalDataDownloaded = totalDataDown,
            TotalDataUploaded = totalDataUp,
            TotalProtocolDownloaded = totalProtoDown,
            TotalProtocolUploaded = totalProtoUp,
            ProtocolOverheadPercentage = protoOverheadPct,
            OpenConnections = openConns,
            HalfOpenConnections = halfOpenConns,
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
        object clientEngine,
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
                    cacheMisses = Convert.ToInt64(diskType.GetProperty("CacheMisses")?.GetValue(disk) ?? diskType.GetProperty("CacheMiss")?.GetValue(disk) ?? 0);
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

    public void Handle(NetworkBindingProviderSwitchedEvent message)
    {
        this.logger.Info("Network binding provider switched ({0} -> {1}). Cycling active peer sockets to enforce new interface binding.", message.PreviousProvider, message.NewProvider);
        _ = Task.Run(async () =>
        {
            try
            {
                await this.ResetPeerSocketsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error occurred while resetting peer sockets after network binding provider switch");
            }
        });
    }

    public async Task ResetPeerSocketsAsync()
    {
        foreach (var task in this.tasks.Values)
        {
            if (task.Manager != null)
            {
                try
                {
                    var peers = await task.Manager.GetPeersAsync().ConfigureAwait(false);
                    foreach (var peer in peers)
                    {
                        try
                        {
                            (peer as IDisposable)?.Dispose();
                            var connProp = peer?.GetType().GetProperty("Connection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            (connProp?.GetValue(peer) as IDisposable)?.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Error cycling peer connections for {0}", task.InfoHash);
                }
            }
        }
    }

    public void OnVpnDropped(string interfaceName)
    {
        this.logger.Error("VPN Kill Switch drop detected for interface '{0}'. Halting MonoTorrent engine and terminating active peer connections.", interfaceName);
        this.isHaltedByKillSwitch = true;
        this.natPmpPortMapperService?.Suspend();

        lock (this.vpnTransitionLock)
        {
            this.vpnTransitionQueue = this.vpnTransitionQueue.ContinueWith(
                async _ =>
                {
                    try
                    {
                        await this.HaltAllTorrentsForKillSwitchAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error occurred during VPN kill switch halt");
                    }
                },
                TaskScheduler.Default).Unwrap();
        }
    }

    public void OnVpnRestored(string interfaceName)
    {
        this.logger.Info("VPN interface '{0}' restored. Resuming MonoTorrent activity.", interfaceName);
        this.natPmpPortMapperService?.Resume();

        lock (this.vpnTransitionLock)
        {
            this.vpnTransitionQueue = this.vpnTransitionQueue.ContinueWith(
                async _ =>
                {
                    try
                    {
                        await this.ResumeTorrentsAfterVpnRestoredAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Error occurred while resuming torrents after VPN restoration");
                    }
                },
                TaskScheduler.Default).Unwrap();
        }
    }

    public async Task HaltAllTorrentsForKillSwitchAsync()
    {
        await this.engineStateLock.WaitAsync().ConfigureAwait(false);
        try
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
                                var connProp = peer?.GetType().GetProperty("Connection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                (connProp?.GetValue(peer) as IDisposable)?.Dispose();
                            }
                            catch
                            {
                            }
                        }

                        if (this.engine?.DiskManager != null)
                        {
                            try
                            {
                                await this.engine.DiskManager.FlushAsync(task.Manager).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Warn(ex, "Error flushing dirty write cache for torrent {0} during VPN kill switch halt", task.TorrentId);
                            }
                        }

                        try
                        {
                            await this.SaveFastResumeAtomicAsync(task.Manager, this.GetCacheDirectory()).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            this.logger.Debug(ex, "Error saving FastResume checkpoint for torrent {0} during VPN kill switch halt", task.TorrentId);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Error pausing torrent {0} during VPN drop", task.TorrentId);
                    }
                }
            }

            if (this.engine != null)
            {
                try
                {
                    var settingsBuilder = new EngineSettingsBuilder(this.engine.Settings)
                    {
                        DhtEndPoint = null,
                        AllowLocalPeerDiscovery = false,
                    };
                    await this.engine.UpdateSettingsAsync(settingsBuilder.ToSettings()).ConfigureAwait(false);
                    this.logger.Info("Disabled DHT endpoint and LPD following VPN kill switch halt");
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Error disabling DHT endpoint during VPN kill switch halt");
                }
            }

            this.logger.Warn("VPN kill switch halt completed. {0} active torrents paused, dirty write cache flushed, and peer connections aborted.", this.interruptedTorrentIds.Count);
        }
        finally
        {
            this.engineStateLock.Release();
        }
    }

    public async Task ResumeTorrentsAfterVpnRestoredAsync()
    {
        var isKillSwitchEnabled = this.configService.EnableVpnKillSwitch ||
                                  (this.vpnKillSwitchService?.IsKillSwitchEnabled ?? false);

        if (this.vpnKillSwitchService != null && isKillSwitchEnabled)
        {
            var vpnIp = this.vpnKillSwitchService.GetVpnInterfaceIpAddress(System.Net.Sockets.AddressFamily.InterNetwork) ??
                         this.vpnKillSwitchService.GetVpnInterfaceIpAddress(System.Net.Sockets.AddressFamily.InterNetworkV6);
            if (vpnIp == null)
            {
                this.isHaltedByKillSwitch = true;
                this.logger.Warn("Cannot resume torrents after VPN restoration: VPN Kill Switch is active (fail-closed).");
                return;
            }
        }

        await this.engineStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.engine == null)
            {
                await this.StartEngineAsyncCore().ConfigureAwait(false);
                if (this.isHaltedByKillSwitch || this.engine == null)
                {
                    this.logger.Warn("Aborting torrent resumption after VPN restoration: engine failed to start or is halted by kill switch.");
                    return;
                }
            }
            else
            {
                await this.UpdateEngineListenEndpointsAsync().ConfigureAwait(false);
                if (this.isHaltedByKillSwitch)
                {
                    this.logger.Warn("Aborting torrent resumption after VPN restoration: engine is halted by kill switch.");
                    return;
                }

                await this.DrainPendingTorrentsAsync().ConfigureAwait(false);
            }

            this.isHaltedByKillSwitch = false;

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
        finally
        {
            this.engineStateLock.Release();
        }
    }

    internal async Task UpdateEngineListenEndpointsAsync()
    {
        if (this.engine == null)
        {
            return;
        }

        var port = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        var iface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;

        var hasSpecificInterface = !string.IsNullOrWhiteSpace(iface) &&
            !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase);

        var isKillSwitchEnabled = this.IsVpnKillSwitchActive();
        var listenIp = IPAddress.Any;
        IPAddress resolvedIpv6 = null;

        if (hasSpecificInterface)
        {
            var resolvedIp = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress()
                ?? this.ResolveInterfaceIp(iface, AddressFamily.InterNetwork);

            if (resolvedIp != null)
            {
                listenIp = resolvedIp;
                this.isHaltedByKillSwitch = false;
                this.logger.Info("Re-resolved and updated MonoTorrent IPv4 listening socket to interface '{0}' ({1})", iface, listenIp);
            }
            else if (isKillSwitchEnabled)
            {
                this.isHaltedByKillSwitch = true;
                this.logger.Error("VPN Kill Switch is active and interface '{0}' has no resolved IPv4 address. Strict fail-closed halt enforced: Engine will NOT bind to default network interfaces.", iface);
                return;
            }
            else
            {
                this.logger.Warn("Failed to resolve IP for bound interface '{0}'. Defaulting to IPAddress.Any", iface);
            }

            resolvedIpv6 = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress(AddressFamily.InterNetworkV6)
                ?? this.ResolveInterfaceIp(iface, AddressFamily.InterNetworkV6);
            if (resolvedIpv6 != null && (resolvedIpv6.IsIPv6LinkLocal || resolvedIpv6.IsIPv6SiteLocal || resolvedIpv6.IsIPv6Multicast ||
                IPAddress.IsLoopback(resolvedIpv6) || resolvedIpv6.Equals(IPAddress.IPv6Any) || resolvedIpv6.Equals(IPAddress.IPv6None)))
            {
                resolvedIpv6 = null;
            }
        }

        var listenEndPoints = new Dictionary<string, IPEndPoint>
        {
            { "ipv4", new IPEndPoint(listenIp, port) },
        };

        if (this.configService.EnableIPv6 && Socket.OSSupportsIPv6)
        {
            if (hasSpecificInterface && resolvedIpv6 != null)
            {
                listenEndPoints["ipv6"] = new IPEndPoint(resolvedIpv6, port);
            }
            else if (!hasSpecificInterface)
            {
                listenEndPoints["ipv6"] = new IPEndPoint(IPAddress.IPv6Any, port);
            }
        }

        var isProxyActive = this.IsProxyActive();
        var allowLpd = !isProxyActive && !hasSpecificInterface && !isKillSwitchEnabled && this.configService.EnableLpd;

        if (this.configService.EnableLpd && !allowLpd && (hasSpecificInterface || isKillSwitchEnabled))
        {
            this.logger.Info("Local Peer Discovery (LPD) disabled to prevent multicast UDP infohash leaks over physical LAN adapters while bound to VPN interface or kill switch.");
        }

        if (this.configService.AnonymousMode)
        {
            listenEndPoints = new Dictionary<string, IPEndPoint>();
        }

        var newSettings = this.BuildEngineSettings(listenEndPoints);
        await this.engine.UpdateSettingsAsync(newSettings).ConfigureAwait(false);
        this.logger.Info("Updated MonoTorrent engine listening endpoints to {0}:{1}", listenIp, port);
    }

    private bool IsSpecificInterfaceBound()
    {
        var iface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;

        return !string.IsNullOrWhiteSpace(iface) &&
            !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsVpnKillSwitchActive()
    {
        return this.configService.EnableVpnKillSwitch ||
            (this.vpnKillSwitchService?.IsKillSwitchEnabled ?? false);
    }

    private bool IsProxyActive()
    {
        var isProxyConfigured = this.configService.ProxyType?.ToLowerInvariant() is "socks5" or "http" &&
            !string.IsNullOrWhiteSpace(this.configService.ProxyHost);
        return isProxyConfigured || this.configService.ForceProxy || (this.networkBindingService?.ActiveProvider is IProxyTunnelBindingProvider);
    }

    private bool IsLocalPeerDiscoveryAllowed()
    {
        return !this.configService.AnonymousMode && !this.IsProxyActive() && !this.IsSpecificInterfaceBound() && !this.IsVpnKillSwitchActive() && this.configService.EnableLpd;
    }

    internal HttpClient CreateHttpClient(AddressFamily addressFamily = AddressFamily.InterNetwork)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };
        var proxy = this.GetConfiguredWebProxy();
        if (proxy != null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        else
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                if (this.isHaltedByKillSwitch || (this.vpnKillSwitchService != null && this.vpnKillSwitchService.IsFailClosedActive))
                {
                    throw new SocketException((int)SocketError.NetworkUnreachable);
                }

                var connector = new BoundSocketConnector(
                    () => this.GetBoundLocalIp(AddressFamily.InterNetwork),
                    () => this.GetBoundLocalIp(AddressFamily.InterNetworkV6),
                    this.networkBindingService,
                    () => !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
                        ? this.configService.NetworkInterfaceBinding
                        : this.configService.BindInterface,
                    this.blocklistService,
                    () => Interlocked.Increment(ref this.blockedPeersCount),
                    this.configService,
                    () => this.isHaltedByKillSwitch || (this.vpnKillSwitchService != null && this.vpnKillSwitchService.IsFailClosedActive));

                var host = context.DnsEndPoint.Host;
                var hostStr = host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
                var uri = new Uri($"http://{hostStr}:{context.DnsEndPoint.Port}");
                var socket = await connector.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            };
        }

        var client = new HttpClient(handler);
        var currentUserAgent = this.configService.AnonymousMode ? string.Empty : this.configService.BitTorrentUserAgent;
        if (string.IsNullOrWhiteSpace(currentUserAgent) && !this.configService.AnonymousMode)
        {
            currentUserAgent = ClientEmulationPresets.DefaultUserAgent;
        }

        client.DefaultRequestHeaders.Remove("User-Agent");
        if (!string.IsNullOrEmpty(currentUserAgent))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", currentUserAgent);
        }

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");

        return client;
    }

    private IPAddress GetBoundLocalIp(AddressFamily family)
    {
        var iface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;

        var hasSpecificInterface = !string.IsNullOrWhiteSpace(iface) &&
            !string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase);

        if (!hasSpecificInterface)
        {
            return family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
        }

        var resolved = this.vpnKillSwitchService?.GetVpnInterfaceIpAddress(family)
            ?? this.ResolveInterfaceIp(iface, family);

        if (resolved != null && family == AddressFamily.InterNetworkV6)
        {
            if (resolved.IsIPv6LinkLocal || resolved.IsIPv6SiteLocal || resolved.IsIPv6Multicast ||
                IPAddress.IsLoopback(resolved) || resolved.Equals(IPAddress.IPv6Any) || resolved.Equals(IPAddress.IPv6None))
            {
                return null;
            }
        }

        return resolved;
    }

    private IPAddress ResolveInterfaceIp(string interfaceName, AddressFamily family)
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

            var props = nic.GetIPProperties();
            if (props == null || props.UnicastAddresses == null)
            {
                return null;
            }

            var ip = ManagedSocketBindingProvider.SelectIpAddress(props.UnicastAddresses.Select(u => u.Address), props, family);
            if (ip != null && family == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast ||
                    IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
                {
                    return null;
                }
            }

            return ip;
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

        this.trackerHealthTimer?.Dispose();
        this.trackerHealthTimer = null;

        this.diskFlushTimer?.Dispose();
        this.diskFlushTimer = null;

        this.fastResumeAutoSaveTimer?.Dispose();
        this.fastResumeAutoSaveTimer = null;

        lock (this.pendingTorrentsLock)
        {
            this.pendingTorrents.Clear();
        }

        try
        {
            this.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error during StopAsync in Dispose");
        }

        this.engineStateLock.Dispose();
    }

    public void CheckDiskSpaceHealth()
    {
        var thresholdMb = this.configService?.LowDiskSpaceThresholdMb > 0 ? this.configService.LowDiskSpaceThresholdMb : 500;
        var thresholdBytes = thresholdMb * 1024L * 1024L;

        foreach (var task in this.tasks.Values)
        {
            try
            {
                task.CheckDiskSpace(this.diskProvider, thresholdBytes, this.eventAggregator);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error checking disk space health for torrent {0}", task.TorrentId);
            }
        }
    }

    public void CheckTrackerHealth()
    {
        this.CheckDiskSpaceHealth();

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

    internal EngineSettings BuildEngineSettings(Dictionary<string, IPEndPoint> listenEndPoints = null)
    {
        var builder = this.engine != null
            ? new EngineSettingsBuilder(this.engine.Settings)
            : new EngineSettingsBuilder();

        this.PopulateDynamicEngineSettings(builder, listenEndPoints);
        return builder.ToSettings();
    }

    private void PopulateDynamicEngineSettings(EngineSettingsBuilder builder, Dictionary<string, IPEndPoint> listenEndPoints = null)
    {
        var isAnonymous = this.configService.AnonymousMode;
        var isProxyActive = this.IsProxyActive();

        var port = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        var listenIp = IPAddress.Any;
        if (listenEndPoints != null && listenEndPoints.TryGetValue("ipv4", out var customIpv4Ep))
        {
            listenIp = customIpv4Ep.Address;
            port = customIpv4Ep.Port;
        }
        else if (builder.ListenEndPoints != null && builder.ListenEndPoints.TryGetValue("ipv4", out var existingIpv4Ep))
        {
            listenIp = existingIpv4Ep.Address;
            port = existingIpv4Ep.Port;
        }
        else if (builder.DhtEndPoint != null)
        {
            listenIp = builder.DhtEndPoint.Address;
            port = builder.DhtEndPoint.Port;
        }

        if (string.IsNullOrWhiteSpace(builder.CacheDirectory))
        {
            builder.CacheDirectory = this.GetCacheDirectory();
            builder.AutoSaveLoadFastResume = true;
            builder.AutoSaveLoadDhtCache = true;
        }

        if (isAnonymous)
        {
            builder.ListenEndPoints = new Dictionary<string, IPEndPoint>();
        }
        else if (listenEndPoints != null)
        {
            builder.ListenEndPoints = listenEndPoints;
        }

        builder.AllowPortForwarding = !isAnonymous && this.configService.UpnpEnabled;
        builder.AllowLocalPeerDiscovery = this.IsLocalPeerDiscoveryAllowed();
        builder.AllowHaveSuppression = this.configService.ExtensionLtDontHave;
        builder.AllowedEncryption = GetAllowedEncryption(this.configService.EncryptionMode);
        builder.DhtEndPoint = (!isAnonymous && !isProxyActive && this.configService.EnableDht) ? new IPEndPoint(listenIp, port) : null;
        builder.ConnectionTimeout = TimeSpan.FromSeconds(this.configService.TransportConnectionTimeoutSeconds > 0 ? this.configService.TransportConnectionTimeoutSeconds : 30);
        builder.DiskCacheBytes = this.CalculateDynamicDiskCacheBytes(this.engine?.TotalDownloadRate ?? 0);
        builder.DiskCachePolicy = this.GetConfiguredCachePolicy();
        builder.FastResumeMode = this.GetConfiguredFastResumeMode();
        builder.MaximumConnections = this.configService.MaxGlobalConnections > 0 ? this.configService.MaxGlobalConnections : 300;
        builder.MaximumDownloadRate = this.configService.MaxDownloadSpeedKbps > 0
            ? (int)Math.Min((long)this.configService.MaxDownloadSpeedKbps * 1024, int.MaxValue)
            : 0;
        builder.MaximumUploadRate = this.configService.MaxUploadSpeedKbps > 0
            ? (int)Math.Min((long)this.configService.MaxUploadSpeedKbps * 1024, int.MaxValue)
            : 0;
        builder.WebSeedDelay = TimeSpan.FromSeconds(this.configService.WebSeedDelaySeconds > 0 ? this.configService.WebSeedDelaySeconds : 30);
        builder.UsePartialFiles = this.configService.AppendIncompleteExtension;
    }

    internal async Task ApplyConfigChangesAsync()
    {
        if (this.engine == null)
        {
            return;
        }

        try
        {
            var updatedSettings = this.BuildEngineSettings();
            await this.engine.UpdateSettingsAsync(updatedSettings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to update engine settings on config change");
        }

        var currentIface = !string.IsNullOrWhiteSpace(this.configService.NetworkInterfaceBinding)
            ? this.configService.NetworkInterfaceBinding
            : this.configService.BindInterface;
        var currentPort = this.configService.ListeningPort > 0 ? this.configService.ListeningPort : 51413;

        var currentProxyType = this.configService.ProxyType ?? string.Empty;
        var currentProxyHost = this.configService.ProxyHost ?? string.Empty;
        var currentProxyPort = this.configService.ProxyPort;
        var currentProxyUsername = this.configService.ProxyUsername ?? string.Empty;
        var currentProxyPassword = this.configService.ProxyPassword ?? string.Empty;

        var currentAnonymousMode = this.configService.AnonymousMode;
        var anonymousModeChanged = this.lastAppliedAnonymousMode != currentAnonymousMode;

        var interfaceOrPortChanged = !string.Equals(this.lastAppliedInterfaceBinding, currentIface, StringComparison.OrdinalIgnoreCase) ||
                                     this.lastAppliedListenPort != currentPort;

        var proxyChanged = !string.Equals(this.lastAppliedProxyType, currentProxyType, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(this.lastAppliedProxyHost, currentProxyHost, StringComparison.OrdinalIgnoreCase) ||
                           this.lastAppliedProxyPort != currentProxyPort ||
                           !string.Equals(this.lastAppliedProxyUsername, currentProxyUsername, StringComparison.Ordinal) ||
                           !string.Equals(this.lastAppliedProxyPassword, currentProxyPassword, StringComparison.Ordinal);

        if (interfaceOrPortChanged || proxyChanged || anonymousModeChanged)
        {
            this.logger.Info("Network interface, listen port, proxy, or anonymous mode configuration changed. Updating listen endpoints and cycling peer sockets.");
            await this.UpdateEngineListenEndpointsAsync().ConfigureAwait(false);
            await this.ResetPeerSocketsAsync().ConfigureAwait(false);

            this.lastAppliedInterfaceBinding = currentIface;
            this.lastAppliedListenPort = currentPort;
            this.lastAppliedProxyType = currentProxyType;
            this.lastAppliedProxyHost = currentProxyHost;
            this.lastAppliedProxyPort = currentProxyPort;
            this.lastAppliedProxyUsername = currentProxyUsername;
            this.lastAppliedProxyPassword = currentProxyPassword;
            this.lastAppliedAnonymousMode = currentAnonymousMode;
        }
    }

    public void Handle(ConfigSavedEvent message)
    {
        if (this.engine != null)
        {
            this.ApplyCustomPeerId(this.engine, this.configService.PeerIdPrefix);

            _ = Task.Run(async () =>
            {
                try
                {
                    await this.ApplyConfigChangesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to apply config changes on config save");
                }
            });
        }
    }

    public void Handle(ConfigFileSavedEvent message)
    {
        if (this.engine != null)
        {
            this.ApplyCustomPeerId(this.engine, this.configService.PeerIdPrefix);

            _ = Task.Run(async () =>
            {
                try
                {
                    await this.ApplyConfigChangesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to apply config changes on config file save");
                }
            });
        }
    }

    internal static void ConfigureGlobalMonoTorrentDefaults(string prefix, string userAgent = null, bool anonymousMode = false)
    {
        if (anonymousMode)
        {
            prefix = string.Empty;
            userAgent = string.Empty;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = ClientEmulationPresets.DefaultPeerIdPrefix;
            }

            if (prefix.Contains("MO3002", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("-MO", StringComparison.OrdinalIgnoreCase))
            {
                prefix = ClientEmulationPresets.DefaultPeerIdPrefix;
            }

            if (string.IsNullOrWhiteSpace(userAgent) ||
                userAgent.Contains("MO3002", StringComparison.OrdinalIgnoreCase) ||
                userAgent.StartsWith("MonoTorrent", StringComparison.OrdinalIgnoreCase))
            {
                userAgent = ClientEmulationPresets.GetUserAgentForPrefix(prefix);
            }
        }

        try
        {
            var cleanVersion = anonymousMode ? string.Empty : ClientEmulationPresets.CleanClientVersion(prefix);
            var cleanIdentifier = anonymousMode ? string.Empty : ClientEmulationPresets.CleanClientIdentifier(prefix);

            var knownMonoTorrentAssemblyNames = new[]
            {
                "MonoTorrent",
                "MonoTorrent.Client",
                "MonoTorrent.Dht",
                "MonoTorrent.Factories",
                "MonoTorrent.Trackers",
                "MonoTorrent.PiecePicking",
                "MonoTorrent.Messages"
            };

            foreach (var asmName in knownMonoTorrentAssemblyNames)
            {
                try
                {
                    Assembly.Load(new AssemblyName(asmName));
                }
                catch
                {
                }
            }

            var monoTorrentAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("MonoTorrent", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var asm in monoTorrentAssemblies)
            {
                try
                {
                    var gitInfoType = asm.GetType("MonoTorrent.GitInfoHelper");
                    if (gitInfoType != null)
                    {
                        gitInfoType.GetProperty("ClientVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?.SetValue(null, userAgent);
                        gitInfoType.GetProperty("DhtClientVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?.SetValue(null, cleanVersion);
                        var clientIdentifierProp = gitInfoType.GetProperty("ClientIdentifier", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (clientIdentifierProp?.CanWrite == true)
                        {
                            clientIdentifierProp.SetValue(null, cleanIdentifier);
                        }

                        gitInfoType.GetField("<ClientVersion>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?.SetValue(null, userAgent);
                        gitInfoType.GetField("<DhtClientVersion>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?.SetValue(null, cleanVersion);
                        var clientIdentifierField = gitInfoType.GetField("<ClientIdentifier>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        SetStaticField(clientIdentifierField, cleanIdentifier);
                    }
                }
                catch
                {
                }

                try
                {
                    var dhtMsgType = asm.GetType("MonoTorrent.Dht.Messages.DhtMessage");
                    if (dhtMsgType != null)
                    {
                        var dhtVersionField = dhtMsgType.GetField("DhtVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?? dhtMsgType.GetField("<DhtVersion>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        dhtVersionField?.SetValue(null, new BEncodedString(cleanVersion));
                    }
                }
                catch
                {
                }

                try
                {
                    var extHandshakeType = asm.GetType("MonoTorrent.Messages.Peer.Libtorrent.ExtendedHandshakeMessage");
                    if (extHandshakeType != null)
                    {
                        var extVerProp = extHandshakeType.GetProperty("Version", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (extVerProp?.CanWrite == true)
                        {
                            extVerProp.SetValue(null, userAgent);
                        }

                        var extVerField = extHandshakeType.GetField("version", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?? extHandshakeType.GetField("<Version>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        SetStaticField(extVerField, userAgent);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
            // Best-effort configuration of MonoTorrent static version info
        }
    }

    private static void SetStaticField(FieldInfo field, object value)
    {
        if (field == null)
        {
            return;
        }

        try
        {
            if (field.IsInitOnly)
            {
                var dm = new DynamicMethod("SetStaticField_" + Guid.NewGuid().ToString("N"), null, new[] { field.FieldType }, field.DeclaringType?.Module ?? typeof(MonoTorrentDownloadEngine).Module, true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, field);
                il.Emit(OpCodes.Ret);
                dm.Invoke(null, new[] { value });
            }
            else
            {
                field.SetValue(null, value);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private static BEncodedString GeneratePeerId(string prefix, bool anonymousMode = false)
    {
        if (anonymousMode)
        {
            prefix = string.Empty;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = ClientEmulationPresets.DefaultPeerIdPrefix;
            }

            if (prefix.Contains("MO3002", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("-MO", StringComparison.OrdinalIgnoreCase))
            {
                prefix = ClientEmulationPresets.DefaultPeerIdPrefix;
            }
        }

        var lengthRemaining = 20 - prefix.Length;
        if (lengthRemaining <= 0)
        {
            return new BEncodedString(prefix.Substring(0, 20));
        }

        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var randomChars = new char[lengthRemaining];
        var randomBytes = new byte[lengthRemaining];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        for (var i = 0; i < lengthRemaining; i++)
        {
            randomChars[i] = chars[randomBytes[i] % chars.Length];
        }

        return new BEncodedString(prefix + new string(randomChars));
    }

    private void ApplyCustomPeerId(ClientEngine clientEngine, string prefix)
    {
        var isAnonymous = this.configService.AnonymousMode;
        if (isAnonymous)
        {
            prefix = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = this.configService.PeerIdPrefix;
        }

        var userAgent = isAnonymous ? string.Empty : this.configService.BitTorrentUserAgent;
        ConfigureGlobalMonoTorrentDefaults(prefix, userAgent, isAnonymous);

        if (clientEngine == null)
        {
            return;
        }

        try
        {
            var customPeerId = GeneratePeerId(prefix, isAnonymous);

            var peerIdField = typeof(ClientEngine).GetField("<PeerId>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (peerIdField != null)
            {
                peerIdField.SetValue(clientEngine, customPeerId);
            }

            var connManagerProp = typeof(ClientEngine).GetProperty("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var connManagerField = typeof(ClientEngine).GetField("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(ClientEngine).GetField("<ConnectionManager>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var connMgr = connManagerProp?.GetValue(clientEngine) ?? connManagerField?.GetValue(clientEngine);
            if (connMgr != null)
            {
                var localPeerIdField = connMgr.GetType().GetField("<LocalPeerId>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? connMgr.GetType().GetField("LocalPeerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                localPeerIdField?.SetValue(connMgr, customPeerId);
            }

            this.logger.Info("Configured MonoTorrent download client Peer ID: '{0}' (User-Agent: '{1}')", customPeerId.Text, userAgent);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to apply custom Peer ID prefix '{0}' to MonoTorrent engine.", prefix);
        }
    }

    [DllImport("libc", EntryPoint = "posix_fallocate", SetLastError = true)]
    private static extern int PosixFallocate(int fd, long offset, long len);

    internal async Task PreallocateFilesAsync(TorrentManager manager, string workingPath, MtTorrent parsedTorrent = null)
    {
        var mode = this.configService.PreallocationMode?.Trim();
        if (string.IsNullOrEmpty(mode))
        {
            mode = "Sparse";
        }

        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isFull = string.Equals(mode, "Full", StringComparison.OrdinalIgnoreCase);
        var isSparse = string.Equals(mode, "Sparse", StringComparison.OrdinalIgnoreCase);

        if (manager != null && (manager.Complete ||
                                manager.HashChecked ||
                                (manager.Bitfield != null && manager.Bitfield.Length > 0 && (manager.Bitfield.AllTrue || manager.Bitfield.TrueCount > 0)) ||
                                manager.Progress > 0 ||
                                (manager.InfoHashes?.V1OrV2 != null &&
                                 this.infoHashToId.TryGetValue(manager.InfoHashes.V1OrV2.ToHex(), out var tId) &&
                                 this.tasks.TryGetValue(tId, out var tTask) &&
                                 tTask.IsFilesMovedToCompleted)))
        {
            return;
        }

        try
        {
            var usePartialFiles = this.configService?.AppendIncompleteExtension == true || this.engine?.Settings?.UsePartialFiles == true;
            const string partialExtension = ".!mt";

            var filesToPreallocate = new List<(string FullPath, long Length)>();
            if (manager?.Files != null && manager.Files.Count > 0)
            {
                var isMultiFile = manager.Files.Count > 1;
                var torrentName = manager.Torrent?.Name;
                foreach (var file in manager.Files)
                {
                    if (file.Priority == MonoTorrent.Priority.DoNotDownload)
                    {
                        continue;
                    }

                    var cleanPath = !string.IsNullOrWhiteSpace(file.FullPath)
                        ? file.FullPath
                        : Path.Combine(workingPath, file.Path);

                    if (cleanPath.EndsWith(partialExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        cleanPath = cleanPath[..^partialExtension.Length];
                    }

                    if (File.Exists(cleanPath) && new FileInfo(cleanPath).Length >= file.Length)
                    {
                        // Clean completed file already exists on disk, do not preallocate partial file
                        continue;
                    }

                    var fullPath = cleanPath;
                    if (usePartialFiles && !fullPath.EndsWith(partialExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        fullPath += partialExtension;
                    }

                    filesToPreallocate.Add((fullPath, file.Length));
                }
            }
            else if (parsedTorrent?.Files != null && parsedTorrent.Files.Count > 0)
            {
                foreach (var file in parsedTorrent.Files)
                {
                    var cleanPath = Path.Combine(workingPath, file.Path);

                    if (File.Exists(cleanPath) && new FileInfo(cleanPath).Length >= file.Length)
                    {
                        // Clean completed file already exists on disk, do not preallocate partial file
                        continue;
                    }

                    var fullPath = cleanPath;
                    if (usePartialFiles && !fullPath.EndsWith(partialExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        fullPath += partialExtension;
                    }

                    filesToPreallocate.Add((fullPath, file.Length));
                }
            }

            if (filesToPreallocate.Count == 0)
            {
                return;
            }

            var hasExistingFiles = false;
            foreach (var (fullPath, _) in filesToPreallocate)
            {
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > 0)
                    {
                        hasExistingFiles = true;
                        break;
                    }
                }
            }

            var allAllocated = true;
            var newlyCreatedFiles = new List<string>();

            foreach (var (fullPath, expectedLength) in filesToPreallocate)
            {
                var fileExistedBefore = File.Exists(fullPath);
                var allocated = await this.PreallocateSingleFileAsync(fullPath, expectedLength, isFull).ConfigureAwait(false);
                if (!allocated)
                {
                    allAllocated = false;
                    this.logger.Error("Preallocation failed for file '{0}' (expected length {1} bytes) in torrent {2}", fullPath, expectedLength, manager?.InfoHashes?.V1OrV2?.ToHex() ?? workingPath);
                    if (!fileExistedBefore && File.Exists(fullPath))
                    {
                        newlyCreatedFiles.Add(fullPath);
                    }

                    break;
                }
                else if (!fileExistedBefore)
                {
                    newlyCreatedFiles.Add(fullPath);
                }
            }

            if (!allAllocated)
            {
                this.logger.Warn("Aborting synthetic FastResume initialization for torrent {0} due to preallocation failure; standard hash check will be enforced.", manager?.InfoHashes?.V1OrV2?.ToHex() ?? workingPath);
                foreach (var file in newlyCreatedFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            this.logger.Debug("Cleaned up incomplete preallocated file '{0}'", file);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Failed to clean up incomplete file '{0}' after failed preallocation", file);
                    }
                }

                return;
            }

            if (!hasExistingFiles && manager != null && !manager.HashChecked && manager.InfoHashes != null && (manager.Bitfield == null || manager.Bitfield.TrueCount == 0))
            {
                var pieceCount = parsedTorrent?.PieceCount ?? manager.Torrent?.PieceCount ?? 0;
                if (pieceCount > 0)
                {
                    var emptyBitfield = new ReadOnlyBitField(pieceCount);
                    var unhashed = new ReadOnlyBitField(pieceCount);
                    var fastResume = new FastResume(manager.InfoHashes, emptyBitfield, unhashed);
                    await manager.LoadFastResumeAsync(fastResume).ConfigureAwait(false);
                    this.logger.Debug("Initialized empty FastResume for newly created preallocated torrent {0} to skip initial hash check", manager.InfoHashes.V1OrV2?.ToHex());
                }
            }
            else if (hasExistingFiles)
            {
                this.logger.Info("Existing files detected on disk for torrent {0}; executing integrity hash check.", manager?.InfoHashes?.V1OrV2?.ToHex() ?? workingPath);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to preallocate files with mode '{0}' in '{1}'", mode, workingPath);
        }
    }

    private async Task<bool> PreallocateSingleFileAsync(string fullPath, long expectedLength, bool isFull)
    {
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length >= expectedLength)
                {
                    return true;
                }
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous,
            };

            await using var fs = new FileStream(fullPath, options);
            if (fs.Length >= expectedLength)
            {
                return true;
            }

            if (isFull)
            {
                var allocated = false;
                if (OsInfo.IsLinux)
                {
                    try
                    {
                        var fd = fs.SafeFileHandle.DangerousGetHandle().ToInt32();
                        var ret = PosixFallocate(fd, 0, expectedLength);
                        if (ret == 0)
                        {
                            allocated = true;
                        }
                        else
                        {
                            this.logger.Debug("posix_fallocate returned error {0} for '{1}', falling back to background allocation", ret, fullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "posix_fallocate failed for '{0}', falling back to background allocation", fullPath);
                    }
                }

                if (!allocated)
                {
                    await Task.Run(async () =>
                    {
                        fs.Seek(0, SeekOrigin.End);
                        const int bufferSize = 1024 * 1024; // 1 MB buffer
                        var buffer = new byte[bufferSize];
                        long bytesRemaining = expectedLength - fs.Length;
                        while (bytesRemaining > 0)
                        {
                            int toWrite = (int)Math.Min(bytesRemaining, bufferSize);
                            await fs.WriteAsync(buffer.AsMemory(0, toWrite)).ConfigureAwait(false);
                            bytesRemaining -= toWrite;
                        }

                        await fs.FlushAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }

                return true;
            }
            else
            {
                try
                {
                    fs.SetLength(expectedLength);
                    return true;
                }
                catch (Exception ex) when (ex is NotSupportedException or IOException or PlatformNotSupportedException)
                {
                    this.logger.Warn(ex, "Sparse preallocation unsupported or failed for '{0}' (expected {1} bytes). Falling back to on-demand streaming allocation.", fullPath, expectedLength);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to preallocate file '{0}' (size {1} bytes)", fullPath, expectedLength);
            return false;
        }
    }

    public long GetEffectiveMemoryBytes(string cgroupV2Path = "/sys/fs/cgroup/memory.max", string cgroupV1Path = "/sys/fs/cgroup/memory/memory.limit_in_bytes")
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                if (File.Exists(cgroupV2Path))
                {
                    var text = File.ReadAllText(cgroupV2Path).Trim();
                    if (!string.Equals(text, "max", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(text, out var cgroupV2Limit) &&
                        cgroupV2Limit > 0 &&
                        cgroupV2Limit < 0x7FFFFFFFFFFFF000L)
                    {
                        return cgroupV2Limit;
                    }
                }

                if (File.Exists(cgroupV1Path))
                {
                    var text = File.ReadAllText(cgroupV1Path).Trim();
                    if (long.TryParse(text, out var cgroupV1Limit) &&
                        cgroupV1Limit > 0 &&
                        cgroupV1Limit < 0x7FFFFFFFFFFFF000L)
                    {
                        return cgroupV1Limit;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to read cgroup memory limit");
            }
        }

        long totalSystemRam = 0;
        try
        {
            totalSystemRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch
        {
        }

        if (totalSystemRam <= 0)
        {
            totalSystemRam = 8L * 1024L * 1024L * 1024L;
        }

        return totalSystemRam;
    }

    public int CalculateDynamicDiskCacheBytes(long currentDownloadThroughputBytesPerSec = 0, long? effectiveMemoryOverride = null)
    {
        var configuredMb = this.configService?.DiskWriteCacheSizeMb ?? 128;
        var configuredBytes = this.configService?.DiskCacheBytes ?? (128 * 1024 * 1024);

        var effectiveMemory = effectiveMemoryOverride ?? this.GetEffectiveMemoryBytes();

        long baseRamCache;
        if (effectiveMemory >= 32L * 1024L * 1024L * 1024L)
        {
            baseRamCache = 512L * 1024L * 1024L;
        }
        else if (effectiveMemory >= 16L * 1024L * 1024L * 1024L)
        {
            baseRamCache = 256L * 1024L * 1024L;
        }
        else if (effectiveMemory >= 8L * 1024L * 1024L * 1024L)
        {
            baseRamCache = 192L * 1024L * 1024L;
        }
        else
        {
            baseRamCache = 128L * 1024L * 1024L;
        }

        var throughputBufferBytes = currentDownloadThroughputBytesPerSec > 0
            ? currentDownloadThroughputBytesPerSec * 4
            : 0;

        var dynamicCache = baseRamCache + throughputBufferBytes;

        if (configuredMb > 0 && configuredMb < 128)
        {
            dynamicCache = ((long)configuredMb * 1024L * 1024L) + throughputBufferBytes;
        }
        else if (configuredBytes > 0 && configuredBytes < 128 * 1024 * 1024)
        {
            dynamicCache = (long)configuredBytes + throughputBufferBytes;
        }
        else if (configuredMb > 128)
        {
            dynamicCache = Math.Max(dynamicCache, (long)configuredMb * 1024L * 1024L);
        }
        else if (configuredBytes > 128 * 1024 * 1024)
        {
            dynamicCache = Math.Max(dynamicCache, (long)configuredBytes);
        }

        var maxAllowedCache = Math.Max(32L * 1024L * 1024L, (long)(effectiveMemory * 0.25));
        var minAllowedCache = Math.Min(32L * 1024L * 1024L, (long)configuredBytes);
        var upperLimit = Math.Min(1024L * 1024L * 1024L, maxAllowedCache);
        if (minAllowedCache > upperLimit)
        {
            minAllowedCache = upperLimit;
        }

        var clampedBytes = (int)Math.Clamp(dynamicCache, minAllowedCache, upperLimit);
        return clampedBytes;
    }

    private CachePolicy GetConfiguredCachePolicy()
    {
        var policy = this.configService?.DiskCachePolicy;
        if (string.Equals(policy, "ReadsAndWrites", StringComparison.OrdinalIgnoreCase))
        {
            return CachePolicy.ReadsAndWrites;
        }

        return CachePolicy.WritesOnly;
    }

    private FastResumeMode GetConfiguredFastResumeMode()
    {
        var mode = this.configService?.FastResumeMode;
        if (string.Equals(mode, "Accurate", StringComparison.OrdinalIgnoreCase))
        {
            return FastResumeMode.Accurate;
        }

        return FastResumeMode.BestEffort;
    }

    private string GetCacheDirectory()
    {
        return this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
            ? Path.Combine(this.appFolderInfo.AppDataFolder, "Cache")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr", "Cache");
    }

    private void PerformPeriodicDiskFlushAndCacheScaling()
    {
        if (this.engine == null || this.disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (this.engine?.DiskManager != null)
                {
                    foreach (var task in this.tasks.Values)
                    {
                        if (task.Manager != null && task.Manager.State != TorrentState.Stopped)
                        {
                            try
                            {
                                await this.engine.DiskManager.FlushAsync(task.Manager).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Debug(ex, "Error flushing dirty cache for torrent {0}", task.InfoHash);
                            }
                        }
                    }
                }

                var currentDownloadRate = this.engine != null ? this.engine.TotalDownloadRate : 0;
                var targetCacheBytes = this.CalculateDynamicDiskCacheBytes(currentDownloadRate);
                if (this.engine != null && Math.Abs(this.engine.Settings.DiskCacheBytes - targetCacheBytes) >= 32 * 1024 * 1024)
                {
                    var updatedSettings = new EngineSettingsBuilder(this.engine.Settings)
                    {
                        DiskCacheBytes = targetCacheBytes,
                        DiskCachePolicy = this.GetConfiguredCachePolicy(),
                        FastResumeMode = this.GetConfiguredFastResumeMode(),
                    }.ToSettings();
                    await this.engine.UpdateSettingsAsync(updatedSettings).ConfigureAwait(false);
                    this.logger.Debug("Scaled dynamic disk write cache to {0} MB (throughput: {1:F1} MB/s)", targetCacheBytes / (1024 * 1024), currentDownloadRate / (1024.0 * 1024.0));
                }

                TrimMonoTorrentMassiveBuffers();
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error during periodic disk flush and cache scaling");
            }
        });
    }

    private void PerformPeriodicFastResumeSave()
    {
        if (this.engine == null || this.disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await this.SaveAllFastResumeCheckpointsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error during periodic FastResume checkpoint autosave");
            }
        });
    }

    public async Task SaveAllFastResumeCheckpointsAsync()
    {
        var cacheDir = this.GetCacheDirectory();
        foreach (var task in this.tasks.Values)
        {
            if (task.Manager != null)
            {
                await this.SaveFastResumeAtomicAsync(task.Manager, cacheDir).ConfigureAwait(false);
            }
        }
    }

    public async Task SaveFastResumeAtomicAsync(TorrentManager manager, string cacheDirectory)
    {
        if (manager == null || manager.State is TorrentState.Hashing or TorrentState.Metadata or TorrentState.Error || !manager.HashChecked)
        {
            return;
        }

        try
        {
            var fastResume = await manager.SaveFastResumeAsync().ConfigureAwait(false);
            if (fastResume != null)
            {
                var fastResumeDir = Path.Combine(cacheDirectory, "FastResume");
                Directory.CreateDirectory(fastResumeDir);

                var infoHashHex = manager.InfoHashes?.V1OrV2?.ToHex()
                    ?? manager.InfoHashes?.V1?.ToHex()
                    ?? manager.InfoHashes?.V2?.ToHex();

                if (string.IsNullOrWhiteSpace(infoHashHex))
                {
                    return;
                }

                var targetFile = Path.Combine(fastResumeDir, $"{infoHashHex}.fastresume");
                var tempFile = Path.Combine(fastResumeDir, $"{infoHashHex}.fastresume.tmp");

                var encodedBytes = fastResume.Encode();
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    await fs.WriteAsync(encodedBytes, 0, encodedBytes.Length).ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                    fs.Flush(flushToDisk: true);
                }

                File.Move(tempFile, targetFile, overwrite: true);
                this.logger.Debug("Atomically saved FastResume checkpoint for {0}", infoHashHex);
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to atomically save FastResume checkpoint for {0}", manager?.InfoHashes?.V1OrV2?.ToHex());
        }
    }

    public async Task<FastResume> TryLoadSavedFastResumeAsync(string infoHashHex, string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(infoHashHex) || string.IsNullOrWhiteSpace(cacheDirectory))
        {
            return null;
        }

        var candidatePaths = new[]
        {
            Path.Combine(cacheDirectory, "FastResume", $"{infoHashHex}.fastresume"),
            Path.Combine(cacheDirectory, "fastresume", $"{infoHashHex}.fresume"),
            Path.Combine(cacheDirectory, "FastResume", $"{infoHashHex.ToUpperInvariant()}.fastresume"),
            Path.Combine(cacheDirectory, "fastresume", $"{infoHashHex.ToUpperInvariant()}.fresume"),
            Path.Combine(cacheDirectory, "FastResume", $"{infoHashHex.ToLowerInvariant()}.fastresume"),
            Path.Combine(cacheDirectory, "fastresume", $"{infoHashHex.ToLowerInvariant()}.fresume"),
        };

        foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var dict = BEncodedValue.Decode<BEncodedDictionary>(bytes);
                        if (dict != null)
                        {
                            try
                            {
                                var ctor = typeof(FastResume).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(BEncodedDictionary) }, null);
                                if (ctor != null)
                                {
                                    var resume = (FastResume)ctor.Invoke(new object[] { dict });
                                    this.logger.Debug("Found and decoded FastResume checkpoint from '{0}' for {1}", path, infoHashHex);
                                    return resume;
                                }
                            }
                            catch (Exception ex)
                            {
                                this.logger.Debug(ex, "Failed to decode FastResume dictionary using constructor at '{0}', attempting manual bitfield reconstruction", path);
                            }

                            if (FastResume.TryLoad(new MemoryStream(bytes), out var loadedResume))
                            {
                                this.logger.Debug("Found and decoded FastResume checkpoint from '{0}' for {1} via TryLoad", path, infoHashHex);
                                return loadedResume;
                            }

                            if (dict.TryGetValue(new BEncodedString("bitfield"), out var bfVal) && bfVal is BEncodedString bfStr &&
                                dict.TryGetValue(new BEncodedString("bitfield_length"), out var bflVal) && bflVal is BEncodedNumber bflNum)
                            {
                                var bitfieldLength = (int)bflNum.Number;
                                var bitfieldBytes = bfStr.Span.ToArray();
                                var bitfieldMutable = new BitField(bitfieldLength);
                                for (var i = 0; i < bitfieldLength; i++)
                                {
                                    var byteIndex = i / 8;
                                    if (byteIndex < bitfieldBytes.Length)
                                    {
                                        var mask = (byte)(128 >> (i % 8));
                                        if ((bitfieldBytes[byteIndex] & mask) != 0)
                                        {
                                            bitfieldMutable.Set(i, true);
                                        }
                                    }
                                }

                                var bitfield = new ReadOnlyBitField(bitfieldMutable);

                                ReadOnlyBitField unhashed = null;
                                if (dict.TryGetValue(new BEncodedString("unhashed_pieces"), out var unhashedVal) && unhashedVal is BEncodedString unhashedStr)
                                {
                                    var unhashedBytes = unhashedStr.Span.ToArray();
                                    var unhashedMutable = new BitField(bitfieldLength);
                                    for (var i = 0; i < bitfieldLength; i++)
                                    {
                                        var byteIndex = i / 8;
                                        if (byteIndex < unhashedBytes.Length)
                                        {
                                            var mask = (byte)(128 >> (i % 8));
                                            if ((unhashedBytes[byteIndex] & mask) != 0)
                                            {
                                                unhashedMutable.Set(i, true);
                                            }
                                        }
                                    }

                                    unhashed = new ReadOnlyBitField(unhashedMutable);
                                }
                                else
                                {
                                    unhashed = new ReadOnlyBitField(bitfieldLength);
                                }

                                InfoHashes infoHashes = null;
                                if (dict.TryGetValue(new BEncodedString("infohash"), out var ihVal) && ihVal is BEncodedString ihStr)
                                {
                                    var hashBytes = ihStr.Span.ToArray();
                                    if (hashBytes.Length == 20)
                                    {
                                        infoHashes = InfoHashes.FromV1(InfoHash.FromMemory(hashBytes));
                                    }
                                    else if (hashBytes.Length == 32)
                                    {
                                        infoHashes = InfoHashes.FromV2(InfoHash.FromMemory(hashBytes));
                                    }
                                }

                                infoHashes ??= InfoHashes.FromV1(InfoHash.FromHex(infoHashHex));

                                var resume = new FastResume(infoHashes, bitfield, unhashed);
                                this.logger.Debug("Found and decoded FastResume checkpoint from '{0}' for {1} (manual fallback)", path, infoHashHex);
                                return resume;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to decode FastResume file at '{0}'", path);
                }
            }
        }

        return null;
    }

    private long GetFileSizeSafely(string path)
    {
        try
        {
            return this.diskProvider.GetFileSize(path);
        }
        catch
        {
            return 0;
        }
    }

    internal static List<MonoTorrent.Connections.EncryptionType> GetAllowedEncryption(string encryptionMode)
    {
        return encryptionMode?.Trim().ToLowerInvariant() switch
        {
            "forceencrypted" or "forced" or "requireencrypted" or "forcedencryption" or "required" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
            },
            "preferencrypted" or "enabled" or "preferred" => new List<MonoTorrent.Connections.EncryptionType>
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
            "disabled" or "plaintext" or "none" => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.PlainText,
            },
            _ => new List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
                MonoTorrent.Connections.EncryptionType.PlainText,
            },
        };
    }
}

public class MonoTorrentDownloadTask : IDownloadTask
{
    public int TorrentId { get; }

    public string InfoHash { get; }

    public string Category { get; set; }

    public TorrentManager Manager { get; }

    public PiecePicker Picker { get; private set; }

    public IEnumerable<int> PartialPieces => this.Picker?.PartialPieces;

    public bool SequentialDownload { get; set; }

    public bool FirstLastPiecePriority { get; set; }

    private class PeerActivityState
    {
        public long LastReceivedBytes { get; set; }

        public DateTime LastActivityUtc { get; set; }
    }

    private readonly IBlocklistService blocklistService;
    private readonly Action onPeerBlocked;
    private readonly MtTorrent initialTorrent;
    private readonly bool initialIsPrivate;
    private readonly IConfigService configService;
    private readonly Action<MonoTorrentDownloadTask> onPickerCreated;
    private readonly IPeerConnectionHistoryService peerConnectionHistoryService;
    private readonly IEventAggregator eventAggregator;
    private readonly object peerLock = new();
    private readonly Dictionary<string, PeerActivityState> peerActivity = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    private bool isTrackerStalled;
    private bool isStorageFull;
    private bool wasAutoPausedByDiskSpace;
    private string errorMessage;
    private IList<PeerId> cachedMonoPeers;
    private DateTime lastPeersUpdate = DateTime.MinValue;
    private bool isUpdatingPeers;
    private DateTime lastTaskProtocolSample = DateTime.UtcNow;
    private long lastTaskProtoDown;
    private long lastTaskProtoUp;
    private long lastTaskProtoDownSpeed;
    private long lastTaskProtoUpSpeed;
    private int hashedPiecesCount;

    public string WorkingPath { get; set; }

    public string SavePath { get; set; }

    public bool IsFilesMovedToCompleted { get; set; }

    public IReadOnlyList<string> WebSeeds
    {
        get
        {
            var seeds = this.Manager?.Torrent?.HttpSeeds ?? this.initialTorrent?.HttpSeeds;
            if (seeds == null || seeds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var seed in seeds)
            {
                if (seed != null)
                {
                    list.Add(seed.ToString());
                }
            }

            return list;
        }
    }

    public MonoTorrentDownloadTask(
        int torrentId,
        string infoHash,
        TorrentManager manager,
        string category = null,
        MtTorrent initialTorrent = null,
        IBlocklistService blocklistService = null,
        Action onPeerBlocked = null,
        IConfigService configService = null,
        bool isPrivate = false,
        string workingPath = null,
        Action<MonoTorrentDownloadTask> onPickerCreated = null,
        PiecePicker picker = null,
        IPeerConnectionHistoryService peerConnectionHistoryService = null,
        IEventAggregator eventAggregator = null)
    {
        this.TorrentId = torrentId;
        this.InfoHash = infoHash;
        this.Manager = manager;
        this.Category = category;
        this.initialTorrent = initialTorrent;
        this.initialIsPrivate = isPrivate;
        this.blocklistService = blocklistService;
        this.onPeerBlocked = onPeerBlocked;
        this.configService = configService;
        this.WorkingPath = workingPath;
        this.onPickerCreated = onPickerCreated;
        this.Picker = picker;
        this.peerConnectionHistoryService = peerConnectionHistoryService;
        this.eventAggregator = eventAggregator;

        if (manager != null)
        {
            if (manager.Torrent != null && this.Picker == null)
            {
                var t = manager.Torrent;
                this.Picker = new PiecePicker(t.PieceCount, t.PieceLength, t.Size, configService: configService);
                if (manager.Bitfield != null)
                {
                    for (var i = 0; i < Math.Min(manager.Bitfield.Length, t.PieceCount); i++)
                    {
                        if (manager.Bitfield[i])
                        {
                            this.Picker.MarkPieceVerified(i);
                        }
                    }
                }

                this.onPickerCreated?.Invoke(this);
            }

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
        try
        {
            if (e.NewState == TorrentState.Hashing)
            {
                Interlocked.Exchange(ref this.hashedPiecesCount, 0);
            }

            if (this.Manager?.Torrent != null && this.Picker == null)
            {
                var t = this.Manager.Torrent;
                this.Picker = new PiecePicker(t.PieceCount, t.PieceLength, t.Size, configService: this.configService);
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

                this.onPickerCreated?.Invoke(this);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error in task OnTorrentStateChanged for torrent {0}", this.TorrentId);
        }
    }

    private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
    {
        try
        {
            var peerIp = e.Peer?.Uri?.Host;
            var peerPort = e.Peer?.Uri?.Port ?? 0;
            string clientStr = null;
            try
            {
                clientStr = e.Peer?.ClientApp.Client.ToString();
            }
            catch
            {
            }

            clientStr ??= string.Empty;
            var isEncrypted = e.Peer != null && e.Peer.EncryptionType != MonoTorrent.Connections.EncryptionType.PlainText;
            var torrentName = this.Manager?.Torrent?.Name ?? this.initialTorrent?.Name ?? string.Empty;

            if (!string.IsNullOrEmpty(peerIp) && this.blocklistService != null && this.blocklistService.IsIpBlocked(peerIp))
            {
                this.onPeerBlocked?.Invoke();

                var blockedEvent = new PeerConnectionEvent
                {
                    InfoHash = this.InfoHash,
                    TorrentName = torrentName,
                    RemoteIp = peerIp,
                    RemotePort = peerPort,
                    PeerId = clientStr,
                    IsEncrypted = isEncrypted,
                    EventType = "Blocked",
                    Timestamp = DateTime.UtcNow,
                };

                if (this.peerConnectionHistoryService != null)
                {
                    this.peerConnectionHistoryService.RecordEvent(blockedEvent);
                }
                else
                {
                    this.eventAggregator?.PublishEvent(blockedEvent);
                }

                try
                {
                    (e.Peer as IDisposable)?.Dispose();
                }
                catch
                {
                }

                return;
            }

            var connectedEvent = new PeerConnectionEvent
            {
                InfoHash = this.InfoHash,
                TorrentName = torrentName,
                RemoteIp = peerIp ?? string.Empty,
                RemotePort = peerPort,
                PeerId = clientStr,
                IsEncrypted = isEncrypted,
                EventType = "Connected",
                Timestamp = DateTime.UtcNow,
            };

            if (this.peerConnectionHistoryService != null)
            {
                this.peerConnectionHistoryService.RecordEvent(connectedEvent);
            }
            else
            {
                this.eventAggregator?.PublishEvent(connectedEvent);
            }

            this.SynchronizePieceAvailability();
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error in task OnPeerConnected for torrent {0}", this.TorrentId);
        }
    }

    private void OnPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
    {
        try
        {
            var peerIp = e.Peer?.Uri?.Host;
            var peerPort = e.Peer?.Uri?.Port ?? 0;
            string clientStr = null;
            try
            {
                clientStr = e.Peer?.ClientApp.Client.ToString();
            }
            catch
            {
            }

            clientStr ??= string.Empty;
            var isEncrypted = e.Peer != null && e.Peer.EncryptionType != MonoTorrent.Connections.EncryptionType.PlainText;
            var torrentName = this.Manager?.Torrent?.Name ?? this.initialTorrent?.Name ?? string.Empty;

            var disconnectedEvent = new PeerConnectionEvent
            {
                InfoHash = this.InfoHash,
                TorrentName = torrentName,
                RemoteIp = peerIp ?? string.Empty,
                RemotePort = peerPort,
                PeerId = clientStr,
                IsEncrypted = isEncrypted,
                EventType = "Disconnected",
                Timestamp = DateTime.UtcNow,
            };

            if (this.peerConnectionHistoryService != null)
            {
                this.peerConnectionHistoryService.RecordEvent(disconnectedEvent);
            }
            else
            {
                this.eventAggregator?.PublishEvent(disconnectedEvent);
            }

            this.SynchronizePieceAvailability();
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error in task OnPeerDisconnected for torrent {0}", this.TorrentId);
        }
    }

    private void OnPieceHashed(object sender, PieceHashedEventArgs e)
    {
        try
        {
            Interlocked.Increment(ref this.hashedPiecesCount);

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
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error in task OnPieceHashed for torrent {0}", this.TorrentId);
        }
    }

    private volatile bool isQueuedForRecheck;

    public bool IsQueuedForRecheck
    {
        get => this.isQueuedForRecheck;
        set => this.isQueuedForRecheck = value;
    }

    private volatile bool isExplicitRecheck;

    public bool IsExplicitRecheck
    {
        get => this.isExplicitRecheck;
        set => this.isExplicitRecheck = value;
    }

    public bool IsStalled => this.isTrackerStalled;

    public bool IsStorageFull => this.isStorageFull;

    public bool IsOutOfDiskSpace => this.isStorageFull;

    public bool WasAutoPausedByDiskSpace
    {
        get => this.wasAutoPausedByDiskSpace;
        internal set => this.wasAutoPausedByDiskSpace = value;
    }

    public string ErrorMessage => this.errorMessage;

    public bool IsPrivate => this.Manager?.Torrent?.IsPrivate == true ||
                             this.Manager?.TrackerManager?.Private == true ||
                             this.initialTorrent?.IsPrivate == true ||
                             this.initialIsPrivate;

    public static TorrentStatus MapTorrentStateToStatus(TorrentState state)
    {
        return state switch
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

    public TorrentStatus Status
    {
        get
        {
            if (this.Manager == null)
            {
                return TorrentStatus.Stopped;
            }

            if (this.isQueuedForRecheck)
            {
                return TorrentStatus.QueuedForChecking;
            }

            if (this.isStorageFull)
            {
                return TorrentStatus.Paused;
            }

            if (this.isTrackerStalled && this.Manager.State != TorrentState.Paused)
            {
                return TorrentStatus.Stalled;
            }

            var st = MapTorrentStateToStatus(this.Manager.State);
            if (st == TorrentStatus.Downloading && (this.Progress >= 1.0 || this.Manager.Complete || (this.Manager.Bitfield != null && this.Manager.Bitfield.AllTrue)))
            {
                return TorrentStatus.Seeding;
            }

            return st;
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

        var hasPendingTrackers = allTrackers.Any(t =>
            t.Status is MonoTorrent.Trackers.TrackerState.Connecting
            or MonoTorrent.Trackers.TrackerState.Unknown);

        if (hasPendingTrackers)
        {
            if (this.isTrackerStalled)
            {
                this.isTrackerStalled = false;
                this.errorMessage = null;
            }

            return false;
        }

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

        // Determine failing trackers and set stall if appropriate
        var failingTrackers = allTrackers
            .Where(t => t.Status is MonoTorrent.Trackers.TrackerState.Offline or MonoTorrent.Trackers.TrackerState.InvalidResponse ||
                        !string.IsNullOrWhiteSpace(t.FailureMessage))
            .ToList();

        if (!hasPendingTrackers &&
            (failingTrackers.Count == allTrackers.Count ||
             allTrackers.All(t => t.Status != MonoTorrent.Trackers.TrackerState.Ok)))
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

        return false;
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

    internal void SetStorageFull(string message, IEventAggregator eventAggregator = null)
    {
        var wasFull = this.isStorageFull;
        this.isStorageFull = true;
        this.errorMessage = !string.IsNullOrWhiteSpace(message) ? message : "StorageFull: Free disk space dropped below low-disk threshold (500 MB).";

        if (!wasFull)
        {
            eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "DiskSpace", this.errorMessage, isResolved: false));
        }
    }

    internal void ClearStorageFull(IEventAggregator eventAggregator = null)
    {
        var wasFull = this.isStorageFull;
        this.isStorageFull = false;
        if (this.errorMessage != null && this.errorMessage.StartsWith("StorageFull", StringComparison.OrdinalIgnoreCase))
        {
            this.errorMessage = null;
        }

        if (this.wasAutoPausedByDiskSpace && this.Manager != null)
        {
            this.wasAutoPausedByDiskSpace = false;
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.Manager.StartAsync().ConfigureAwait(false);
                    this.logger.Info("Automatically resumed torrent {0} after disk space restored", this.TorrentId);
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to resume torrent {0} after disk space restored", this.TorrentId);
                }
            });
        }

        if (wasFull)
        {
            eventAggregator?.PublishEvent(new HealthIssueEvent(this.TorrentId, "DiskSpace", "Disk space restored.", isResolved: true));
        }
    }

    public bool CheckDiskSpace(IDiskProvider diskProvider, long thresholdBytes, IEventAggregator eventAggregator = null)
    {
        if (this.Manager == null || diskProvider == null)
        {
            return false;
        }

        var path = this.WorkingPath ?? this.SavePath ?? this.Manager.SavePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var freeSpace = diskProvider.GetAvailableSpace(path);
        if (!freeSpace.HasValue)
        {
            return false;
        }

        if (freeSpace.Value < thresholdBytes)
        {
            var isDownloading = this.Manager.State is TorrentState.Downloading or TorrentState.Starting or TorrentState.Metadata or TorrentState.Hashing or TorrentState.Error;
            if (isDownloading && this.Progress < 1.0)
            {
                try
                {
                    if (this.Manager.State != TorrentState.Paused && this.Manager.State != TorrentState.Stopping && this.Manager.State != TorrentState.Stopped)
                    {
                        this.wasAutoPausedByDiskSpace = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await this.Manager.PauseAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Debug(ex, "Failed to pause manager during low-disk condition for torrent {0}", this.TorrentId);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to pause manager during low-disk condition for torrent {0}", this.TorrentId);
                }

                this.SetStorageFull($"StorageFull: Free disk space dropped below threshold ({freeSpace.Value / (1024 * 1024)} MB available, {thresholdBytes / (1024 * 1024)} MB required).", eventAggregator);
                return true;
            }
        }
        else if (this.isStorageFull || this.wasAutoPausedByDiskSpace)
        {
            this.ClearStorageFull(eventAggregator);
        }

        return false;
    }

    public long TotalBytes => this.Manager?.Torrent?.Size ?? this.initialTorrent?.Size ?? 0;

    public long TotalSize => this.TotalBytes;

    public long DownloadedBytes => this.Manager?.Monitor?.DataBytesReceived ?? 0;

    public long UploadedBytes => this.Manager?.Monitor?.DataBytesSent ?? 0;

    public double Progress
    {
        get
        {
            if (this.Manager == null)
            {
                return 0.0;
            }

            if (this.Manager.State == TorrentState.Hashing)
            {
                var total = this.Manager.Torrent?.PieceCount ?? this.initialTorrent?.PieceCount ?? this.Picker?.PieceCount ?? 0;
                if (total > 0)
                {
                    var hashed = Volatile.Read(ref this.hashedPiecesCount);
                    if (hashed > 0)
                    {
                        return Math.Clamp((double)hashed / total, 0.0, 1.0);
                    }
                }

                return 0.0;
            }

            return Math.Max(0.0, Math.Min(1.0, this.Manager.Progress / 100.0));
        }
    }

    public long DownloadSpeed => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : (this.Manager?.Monitor?.DownloadRate ?? 0);

    public long UploadSpeed => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : (this.Manager?.Monitor?.UploadRate ?? 0);

    public int ConnectedSeeders => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : (this.Manager?.Peers?.Seeds ?? 0);

    public int ConnectedLeechers => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : (this.Manager?.Peers?.Leechs ?? 0);

    public bool IsSuperSeeding => this.Manager?.IsInitialSeeding ?? false;

    public int PieceLength => this.Manager?.Torrent?.PieceLength ?? this.Picker?.PieceLength ?? 0;

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

    public void SynchronizePieceAvailability()
    {
        lock (this.peerLock)
        {
            this.lastPeersUpdate = DateTime.MinValue;
        }

        _ = this.PieceAvailability;
    }

    public int[] PieceAvailability
    {
        get
        {
            if (this.Manager == null && this.Picker == null)
            {
                return Array.Empty<int>();
            }

            if (this.Manager == null && this.Picker != null)
            {
                return this.Picker.GetAvailability();
            }

            var pieceCount = this.Manager?.Bitfield?.Length ?? this.Picker?.PieceCount ?? 0;
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

            if (this.Picker != null)
            {
                this.Picker.SetAvailability(availability);
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
            var now = DateTime.UtcNow;

            foreach (var p in peers)
            {
                var ip = p.Uri?.Host;
                if (!string.IsNullOrEmpty(ip) && this.blocklistService != null && this.blocklistService.IsIpBlocked(ip))
                {
                    continue;
                }

                var peerKey = $"{p.Uri?.Host}:{p.Uri?.Port}";
                var isSnubbed = false;
                lock (this.peerLock)
                {
                    var currentBytes = p.Monitor?.DataBytesReceived ?? 0;
                    if (!this.peerActivity.TryGetValue(peerKey, out var act))
                    {
                        act = new PeerActivityState
                        {
                            LastReceivedBytes = currentBytes,
                            LastActivityUtc = now,
                        };
                        this.peerActivity[peerKey] = act;
                    }
                    else
                    {
                        if (currentBytes > act.LastReceivedBytes || (p.Monitor?.DownloadRate ?? 0) > 0)
                        {
                            act.LastReceivedBytes = currentBytes;
                            act.LastActivityUtc = now;
                        }

                        if ((now - act.LastActivityUtc).TotalSeconds > 60)
                        {
                            isSnubbed = true;
                        }
                    }
                }

                var flags = string.Empty;
                if (isSnubbed)
                {
                    flags += "S";
                }

                if (p.AmInterested)
                {
                    flags += p.IsChoking ? "d" : "D";
                }

                if (p.IsInterested)
                {
                    flags += p.AmChoking ? "u" : "U";
                }

                var isIncoming = IsPeerIncoming(p);
                if (isIncoming)
                {
                    flags += "I";
                }

                var isEncrypted = p.EncryptionType != MonoTorrent.Connections.EncryptionType.PlainText;
                if (isEncrypted)
                {
                    flags += "E";
                }

                var isUtp = (p.Uri?.Scheme?.Equals("utp", StringComparison.OrdinalIgnoreCase) == true) ||
                            p.ClientApp.Client.ToString().Contains("uTP", StringComparison.OrdinalIgnoreCase);
                if (isUtp)
                {
                    flags += "P";
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
                    IsChoked = p.IsChoking,
                    IsInterested = p.IsInterested,
                    ClientIsChoked = p.AmChoking,
                    ClientIsInterested = p.AmInterested,
                    IsIncoming = isIncoming,
                    IsUtp = isUtp,
                });
            }

            return list;
        }
        catch
        {
            return Array.Empty<PeerInfo>();
        }
    }

    private static readonly PropertyInfo PeerConnectionProp = typeof(PeerId).GetProperty("Connection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly PropertyInfo PeerConnectionDirectionProp = typeof(PeerId).GetProperty("ConnectionDirection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool IsPeerIncoming(PeerId peer)
    {
        if (peer == null)
        {
            return false;
        }

        try
        {
            var dir = PeerConnectionDirectionProp?.GetValue(peer);
            if (dir != null && string.Equals(dir.ToString(), "Incoming", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var conn = PeerConnectionProp?.GetValue(peer);
            if (conn != null)
            {
                var isIncomingProp = conn.GetType().GetProperty("IsIncoming", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (isIncomingProp?.GetValue(conn) is bool isIncoming)
                {
                    return isIncoming;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public async Task<bool> DisconnectPeerAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || this.Manager == null)
        {
            return false;
        }

        try
        {
            var peers = await this.Manager.GetPeersAsync().ConfigureAwait(false);
            var matched = false;
            foreach (var peer in peers)
            {
                if (string.Equals(peer.Uri?.Host, ip, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    try
                    {
                        (peer as IDisposable)?.Dispose();
                        var connProp = peer?.GetType().GetProperty("Connection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        (connProp?.GetValue(peer) as IDisposable)?.Dispose();
                    }
                    catch
                    {
                    }
                }
            }

            lock (this.peerLock)
            {
                this.cachedMonoPeers = null;
                this.lastPeersUpdate = DateTime.MinValue;
            }

            return matched;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error disconnecting peer {0} in torrent {1}", ip, this.TorrentId);
            return false;
        }
    }

    public TorrentResourceMetrics GetResourceMetrics()
    {
        if (this.Manager == null)
        {
            return new TorrentResourceMetrics
            {
                TorrentId = this.TorrentId,
                InfoHash = this.InfoHash ?? string.Empty,
                Name = this.initialTorrent?.Name ?? this.InfoHash ?? string.Empty,
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
            if (p.EncryptionType != MonoTorrent.Connections.EncryptionType.PlainText)
            {
                encryptedCount++;
            }
            else
            {
                plaintextCount++;
            }

            var isUtp = (p.Uri != null && string.Equals(p.Uri.Scheme, "utp", StringComparison.OrdinalIgnoreCase)) ||
                        p.ClientApp.Client.ToString().Contains("uTP", StringComparison.OrdinalIgnoreCase);
            if (isUtp)
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
        var inFlightBlocks = this.Picker?.InFlightBlockCount ?? 0;
        var piecesInFlight = inFlightBlocks > 0
            ? inFlightBlocks
            : (isDownloading && this.Picker == null ? Math.Min(openConns * 2, Math.Max(0, totalPieces - completedPieces)) : 0);
        var pieceLength = this.Manager.Torrent?.PieceLength ?? (totalPieces > 0 && this.Manager.Torrent != null ? (int)(this.Manager.Torrent.Size / totalPieces) : 262144);
        var hashFails = this.Manager.HashFails;
        var wastedBytes = (long)hashFails * pieceLength;
        var estMemBuffer = (long)piecesInFlight * (this.Picker != null ? PiecePicker.DefaultBlockSize : pieceLength);
        var diskPendingWrites = isDownloading ? inFlightBlocks : 0;

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

        var isInactive = this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued;
        var downSpeed = isInactive ? 0 : (monitor?.DownloadRate ?? 0);
        var upSpeed = isInactive ? 0 : (monitor?.UploadRate ?? 0);

        var now = DateTime.UtcNow;
        var protoElapsedSec = Math.Max(0.001, (now - this.lastTaskProtocolSample).TotalSeconds);
        var taskProtoDownSpeed = this.lastTaskProtoDownSpeed;
        var taskProtoUpSpeed = this.lastTaskProtoUpSpeed;

        if (isInactive)
        {
            taskProtoDownSpeed = 0;
            taskProtoUpSpeed = 0;
            this.lastTaskProtoDown = protoDown;
            this.lastTaskProtoUp = protoUp;
            this.lastTaskProtocolSample = now;
            this.lastTaskProtoDownSpeed = 0;
            this.lastTaskProtoUpSpeed = 0;
        }
        else if (protoElapsedSec >= 0.5)
        {
            var deltaProtoDown = protoDown - this.lastTaskProtoDown;
            var deltaProtoUp = protoUp - this.lastTaskProtoUp;

            taskProtoDownSpeed = (deltaProtoDown >= 0 && this.lastTaskProtoDown > 0) ? (long)Math.Max(0, Math.Round(deltaProtoDown / protoElapsedSec)) : 0;
            taskProtoUpSpeed = (deltaProtoUp >= 0 && this.lastTaskProtoUp > 0) ? (long)Math.Max(0, Math.Round(deltaProtoUp / protoElapsedSec)) : 0;

            this.lastTaskProtoDown = protoDown;
            this.lastTaskProtoUp = protoUp;
            this.lastTaskProtocolSample = now;
            this.lastTaskProtoDownSpeed = taskProtoDownSpeed;
            this.lastTaskProtoUpSpeed = taskProtoUpSpeed;
        }

        var totalSize = this.Manager.Torrent?.Size ?? (long)totalPieces * pieceLength;
        long? etaSeconds = null;
        if (!isInactive && isDownloading && downSpeed > 0 && totalSize > dataDown)
        {
            etaSeconds = (totalSize - dataDown) / downSpeed;
        }

        var effectiveDownloaded = totalSize > 0 && this.Progress > 0 ? (long)(totalSize * this.Progress) : 0;
        var divisor = effectiveDownloaded > 0
            ? Math.Max(dataDown, effectiveDownloaded)
            : (dataDown > 0 ? dataDown : totalSize);
        var ratio = divisor > 0 ? Math.Round((double)dataUp / divisor, 2) : 0.0;

        return new TorrentResourceMetrics
        {
            TorrentId = this.TorrentId,
            InfoHash = this.InfoHash ?? string.Empty,
            Name = this.Manager.Torrent?.Name ?? this.initialTorrent?.Name ?? this.InfoHash ?? string.Empty,
            Category = this.Category ?? string.Empty,
            Status = this.Status.ToString() ?? "Stopped",
            Progress = this.Progress,
            TotalBytes = totalSize,
            PayloadDownloadSpeed = downSpeed,
            PayloadUploadSpeed = upSpeed,
            ProtocolDownloadSpeed = taskProtoDownSpeed,
            ProtocolUploadSpeed = taskProtoUpSpeed,
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
            DiskPendingWrites = diskPendingWrites,
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

public class FilteringPeerConnectionListener : MonoTorrent.Connections.Peer.IPeerConnectionListener
{
    private readonly MonoTorrent.Connections.Peer.IPeerConnectionListener inner;
    private readonly IBlocklistService blocklistService;
    private readonly Action onPeerBlocked;
    private readonly int maxHalfOpenConnections;
    private readonly int maxConnectionsPerIp;
    private readonly TimeSpan handshakeTimeout;
    private readonly Func<bool> isHalted;
    private readonly ConcurrentDictionary<string, int> connectionsPerIp = new();
    private int halfOpenCount;

    public FilteringPeerConnectionListener(
        MonoTorrent.Connections.Peer.IPeerConnectionListener inner,
        IBlocklistService blocklistService = null,
        Action onPeerBlocked = null,
        int maxHalfOpenConnections = 50,
        int maxConnectionsPerIp = 5,
        TimeSpan? handshakeTimeout = null,
        Func<bool> isHalted = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.blocklistService = blocklistService;
        this.onPeerBlocked = onPeerBlocked;
        this.maxHalfOpenConnections = maxHalfOpenConnections > 0 ? maxHalfOpenConnections : 50;
        this.maxConnectionsPerIp = maxConnectionsPerIp > 0 ? maxConnectionsPerIp : 5;
        this.handshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(15);
        this.isHalted = isHalted;
        this.inner.ConnectionReceived += this.OnInnerConnectionReceived;
    }

    public int HalfOpenConnections => Volatile.Read(ref this.halfOpenCount);

    public int MaxConnectionsPerIp => this.maxConnectionsPerIp;

    public int GetActiveConnections(string ip) =>
        !string.IsNullOrEmpty(ip) && this.connectionsPerIp.TryGetValue(ip, out var count) ? count : 0;

    public IPEndPoint LocalEndPoint => this.inner.LocalEndPoint;

    public IPEndPoint PreferredLocalEndPoint => this.inner.PreferredLocalEndPoint;

    public MonoTorrent.Connections.ListenerStatus Status => this.inner.Status;

    public event EventHandler<EventArgs> StatusChanged
    {
        add => this.inner.StatusChanged += value;
        remove => this.inner.StatusChanged -= value;
    }

    public event EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs> ConnectionReceived;

    public void Start() => this.inner.Start();

    public void Stop() => this.inner.Stop();

    private void OnInnerConnectionReceived(object sender, MonoTorrent.Connections.Peer.PeerConnectionEventArgs e)
    {
        if (this.isHalted?.Invoke() == true)
        {
            try
            {
                (e.Connection as IDisposable)?.Dispose();
            }
            catch
            {
            }

            return;
        }

        if (e.Connection == null)
        {
            this.ConnectionReceived?.Invoke(this, e);
            return;
        }

        string ip = null;
        try
        {
            ip = e.Connection.Uri?.Host ?? e.Connection.EndPoint?.Address?.ToString();
            if (!string.IsNullOrEmpty(ip) && this.blocklistService != null && this.blocklistService.IsIpBlocked(ip))
            {
                this.onPeerBlocked?.Invoke();
                try
                {
                    (e.Connection as IDisposable)?.Dispose();
                }
                catch
                {
                }

                return;
            }
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(ip) && this.maxConnectionsPerIp > 0)
        {
            var ipCount = this.connectionsPerIp.AddOrUpdate(ip, 1, (_, current) => current + 1);
            if (ipCount > this.maxConnectionsPerIp)
            {
                this.DecrementIpConnection(ip);
                try
                {
                    (e.Connection as IDisposable)?.Dispose();
                }
                catch
                {
                }

                return;
            }
        }

        if (Interlocked.Increment(ref this.halfOpenCount) > this.maxHalfOpenConnections)
        {
            Interlocked.Decrement(ref this.halfOpenCount);
            if (!string.IsNullOrEmpty(ip))
            {
                this.DecrementIpConnection(ip);
            }

            try
            {
                (e.Connection as IDisposable)?.Dispose();
            }
            catch
            {
            }

            return;
        }

        var monitored = new HandshakeMonitoredPeerConnection(
            e.Connection,
            this.handshakeTimeout,
            () => Interlocked.Decrement(ref this.halfOpenCount),
            () =>
            {
                if (!string.IsNullOrEmpty(ip))
                {
                    this.DecrementIpConnection(ip);
                }
            });

        var args = new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(monitored, e.InfoHash);

        var handler = this.ConnectionReceived;
        if (handler == null)
        {
            monitored.Dispose();
            return;
        }

        handler.Invoke(this, args);
    }

    private void DecrementIpConnection(string ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            return;
        }

        this.connectionsPerIp.AddOrUpdate(
            ip,
            0,
            (_, count) => count > 1 ? count - 1 : 0);

        if (this.connectionsPerIp.TryGetValue(ip, out var current) && current <= 0)
        {
            ((ICollection<KeyValuePair<string, int>>)this.connectionsPerIp).Remove(new KeyValuePair<string, int>(ip, 0));
        }
    }
}

public sealed class HandshakeMonitoredPeerConnection : MonoTorrent.Connections.Peer.IPeerConnection, IDisposable
{
    private readonly MonoTorrent.Connections.Peer.IPeerConnection inner;
    private readonly Action onHandshakeCompletedOrClosed;
    private readonly Action onDisposed;
    private readonly CancellationTokenSource timeoutCts;
    private int completedOrDisposed;
    private int isDisposed;
    private int totalBytesReceived;

    public HandshakeMonitoredPeerConnection(
        MonoTorrent.Connections.Peer.IPeerConnection inner,
        TimeSpan handshakeTimeout,
        Action onHandshakeCompletedOrClosed,
        Action onDisposed = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.onHandshakeCompletedOrClosed = onHandshakeCompletedOrClosed;
        this.onDisposed = onDisposed;
        this.timeoutCts = new CancellationTokenSource(handshakeTimeout);
        this.timeoutCts.Token.Register(() =>
        {
            if (Interlocked.CompareExchange(ref this.completedOrDisposed, 1, 0) == 0)
            {
                try
                {
                    this.inner.Dispose();
                }
                catch
                {
                }

                this.onHandshakeCompletedOrClosed?.Invoke();
            }

            this.Dispose();
        });
    }

    public ReadOnlyMemory<byte> AddressBytes => this.inner.AddressBytes;

    public bool CanReconnect => this.inner.CanReconnect;

    public bool Disposed => this.inner.Disposed;

    public IPEndPoint EndPoint => this.inner.EndPoint;

    public bool IsIncoming => this.inner.IsIncoming;

    public Uri Uri => this.inner.Uri;

    public ReusableTasks.ReusableTask ConnectAsync() => this.inner.ConnectAsync();

    public async ReusableTasks.ReusableTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        int read;
        try
        {
            read = await this.inner.ReceiveAsync(buffer);
        }
        catch
        {
            this.Dispose();
            throw;
        }

        if (read <= 0)
        {
            this.Dispose();
            return read;
        }

        if (Volatile.Read(ref this.completedOrDisposed) == 0)
        {
            var total = Interlocked.Add(ref this.totalBytesReceived, read);
            if (total >= 68 && Interlocked.CompareExchange(ref this.completedOrDisposed, 1, 0) == 0)
            {
                try
                {
                    this.timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                    this.timeoutCts.Dispose();
                }
                catch
                {
                }

                this.onHandshakeCompletedOrClosed?.Invoke();
            }
        }

        return read;
    }

    public ReusableTasks.ReusableTask<int> SendAsync(Memory<byte> buffer) => this.inner.SendAsync(buffer);

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref this.completedOrDisposed, 1, 0) == 0)
        {
            try
            {
                this.timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                this.timeoutCts.Dispose();
            }
            catch
            {
            }

            this.onHandshakeCompletedOrClosed?.Invoke();
        }

        if (Interlocked.CompareExchange(ref this.isDisposed, 1, 0) == 0)
        {
            try
            {
                this.inner.Dispose();
            }
            catch
            {
            }

            try
            {
                this.onDisposed?.Invoke();
            }
            catch
            {
            }
        }
    }
}

public class BoundSocketConnector : MonoTorrent.Connections.ISocketConnector
{
    private readonly Func<IPAddress> getLocalIpv4;
    private readonly Func<IPAddress> getLocalIpv6;
    private readonly INetworkBindingService networkBindingService;
    private readonly Func<string> getInterfaceName;
    private readonly IBlocklistService blocklistService;
    private readonly Action onPeerBlocked;
    private readonly IConfigService configService;
    private readonly Func<bool> isKillSwitchActive;

    public BoundSocketConnector(
        IPAddress localIpv4,
        IPAddress localIpv6 = null,
        INetworkBindingService networkBindingService = null,
        Func<string> getInterfaceName = null,
        IBlocklistService blocklistService = null,
        Action onPeerBlocked = null,
        IConfigService configService = null,
        Func<bool> isKillSwitchActive = null)
        : this(() => localIpv4, () => localIpv6, networkBindingService, getInterfaceName, blocklistService, onPeerBlocked, configService, isKillSwitchActive)
    {
    }

    public BoundSocketConnector(
        Func<IPAddress> getLocalIpv4,
        Func<IPAddress> getLocalIpv6 = null,
        INetworkBindingService networkBindingService = null,
        Func<string> getInterfaceName = null,
        IBlocklistService blocklistService = null,
        Action onPeerBlocked = null,
        IConfigService configService = null,
        Func<bool> isKillSwitchActive = null)
    {
        this.getLocalIpv4 = getLocalIpv4 ?? (() => IPAddress.Any);
        this.getLocalIpv6 = getLocalIpv6 ?? (() => IPAddress.IPv6Any);
        this.networkBindingService = networkBindingService;
        this.getInterfaceName = getInterfaceName ?? (() => null);
        this.blocklistService = blocklistService;
        this.onPeerBlocked = onPeerBlocked;
        this.configService = configService;
        this.isKillSwitchActive = isKillSwitchActive;
    }

    public Socket CreateDatagramSocket(AddressFamily addressFamily = AddressFamily.InterNetwork, int localPort = 0)
    {
        var isProxyConfigured = this.configService?.ProxyType?.ToLowerInvariant() is "socks5" or "http" &&
            !string.IsNullOrWhiteSpace(this.configService?.ProxyHost);
        var isProxyActive = isProxyConfigured || (this.configService?.ForceProxy ?? false) || (this.networkBindingService?.ActiveProvider is IProxyTunnelBindingProvider);

        if (isProxyActive)
        {
            throw new SocketException((int)SocketError.AccessDenied);
        }

        return this.CreateBoundSocket(addressFamily, SocketType.Dgram, ProtocolType.Udp, localPort);
    }

    public Socket CreateBoundSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, int localPort = 0)
    {
        var localV4 = this.getLocalIpv4();
        var localV6 = this.getLocalIpv6();

        if (addressFamily == AddressFamily.InterNetwork && localV4 == null)
        {
            throw new SocketException((int)SocketError.NetworkUnreachable);
        }

        if (addressFamily == AddressFamily.InterNetworkV6 && localV6 == null)
        {
            throw new SocketException((int)SocketError.NetworkUnreachable);
        }

        var socket = new Socket(addressFamily, socketType, protocolType);
        try
        {
            this.BindSocket(socket, localPort);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void BindSocket(Socket socket, int localPort = 0)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var ifaceName = this.getInterfaceName?.Invoke();
        if (this.networkBindingService != null && !string.IsNullOrWhiteSpace(ifaceName))
        {
            this.networkBindingService.BindSocket(socket, ifaceName, localPort);
        }

        if (socket.IsBound)
        {
            return;
        }

        var localV4 = this.getLocalIpv4();
        var localV6 = this.getLocalIpv6();

        if (socket.AddressFamily == AddressFamily.InterNetwork)
        {
            if (localV4 == null)
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            if (!localV4.Equals(IPAddress.Any) && !localV4.Equals(IPAddress.None))
            {
                socket.Bind(new IPEndPoint(localV4, localPort));
            }
            else if (localPort > 0 || socket.SocketType == SocketType.Dgram)
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
            }
        }
        else if (socket.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (localV6 == null)
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            if (!localV6.Equals(IPAddress.IPv6Any) && !localV6.Equals(IPAddress.None))
            {
                socket.Bind(new IPEndPoint(localV6, localPort));
            }
            else if (localPort > 0 || socket.SocketType == SocketType.Dgram)
            {
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, localPort));
            }
        }
    }

    public async ReusableTasks.ReusableTask<System.Net.Sockets.Socket> ConnectAsync(Uri uri, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (this.isKillSwitchActive != null && this.isKillSwitchActive())
        {
            throw new SocketException((int)SocketError.NetworkUnreachable);
        }

        if (this.blocklistService != null && !string.IsNullOrWhiteSpace(uri.Host) && this.blocklistService.IsIpBlocked(uri.Host))
        {
            this.onPeerBlocked?.Invoke();
            throw new SocketException((int)SocketError.AccessDenied);
        }

        var isDatagram = string.Equals(uri.Scheme, "udp", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Scheme, "utp", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Scheme, "dgram", StringComparison.OrdinalIgnoreCase);

        var activeProvider = this.networkBindingService?.ActiveProvider;
        var isProxyConfigured = this.configService?.ProxyType?.ToLowerInvariant() is "socks5" or "http" &&
            !string.IsNullOrWhiteSpace(this.configService?.ProxyHost);
        var isProxyActive = isProxyConfigured || (this.configService?.ForceProxy ?? false) || (activeProvider is IProxyTunnelBindingProvider);

        if (isProxyActive && isDatagram)
        {
            // Strict leak prevention: datagram / UDP connections cannot be proxied through SOCKS5 TCP tunnel
            throw new SocketException((int)SocketError.AccessDenied);
        }

        if (!isDatagram && activeProvider is IProxyTunnelBindingProvider proxyProvider)
        {
            return await proxyProvider.ConnectTunnelAsync(uri.Host, uri.Port, token).ConfigureAwait(false);
        }

        if (!isDatagram && isProxyConfigured)
        {
            var fallbackProxy = new ProxyTunnelBindingProvider(this.configService, this.blocklistService) { NetworkBindingService = this.networkBindingService };
            return await fallbackProxy.ConnectTunnelAsync(uri.Host, uri.Port, token).ConfigureAwait(false);
        }

        var ifaceName = this.getInterfaceName?.Invoke();
        var hasSpecificInterface = !string.IsNullOrWhiteSpace(ifaceName) &&
            !string.Equals(ifaceName, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ifaceName, "all", StringComparison.OrdinalIgnoreCase);

        var localV4 = this.getLocalIpv4();
        var localV6 = this.getLocalIpv6();

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var parsedIp))
        {
            addresses = [parsedIp];
        }
        else
        {
            if (this.isKillSwitchActive != null && this.isKillSwitchActive())
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            if (hasSpecificInterface && this.networkBindingService != null && !this.networkBindingService.IsInterfaceUp(ifaceName))
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            if (hasSpecificInterface && localV4 == null && localV6 == null)
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            if (localV4 == null && localV6 == null)
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            addresses = await Dns.GetHostAddressesAsync(uri.Host, token).ConfigureAwait(false);
        }

        if (this.blocklistService != null && addresses.Any(a => this.blocklistService.IsIpBlocked(a.ToString())))
        {
            this.onPeerBlocked?.Invoke();
            throw new SocketException((int)SocketError.AccessDenied);
        }

        Exception lastException = null;
        var socketType = isDatagram ? SocketType.Dgram : SocketType.Stream;
        var protocolType = isDatagram ? ProtocolType.Udp : ProtocolType.Tcp;

        foreach (var address in addresses)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork && localV4 == null)
            {
                lastException = new SocketException((int)SocketError.NetworkUnreachable);
                continue;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6 && localV6 == null)
            {
                lastException = new SocketException((int)SocketError.NetworkUnreachable);
                continue;
            }

            var socket = new System.Net.Sockets.Socket(address.AddressFamily, socketType, protocolType);
            try
            {
                if (this.networkBindingService != null && !string.IsNullOrWhiteSpace(ifaceName))
                {
                    this.networkBindingService.BindSocket(socket, ifaceName);
                }
                else
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        if (localV4 != null && !localV4.Equals(IPAddress.Any) && !localV4.Equals(IPAddress.None))
                        {
                            socket.Bind(new IPEndPoint(localV4, 0));
                        }
                    }
                    else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    {
                        if (localV6 != null && !localV6.Equals(IPAddress.IPv6Any) && !localV6.Equals(IPAddress.None))
                        {
                            socket.Bind(new IPEndPoint(localV6, 0));
                        }
                    }
                }

                await socket.ConnectAsync(new IPEndPoint(address, uri.Port), token).ConfigureAwait(false);
                return socket;
            }
            catch (Exception ex)
            {
                socket.Dispose();
                lastException = ex;
            }
        }

        if (lastException != null)
        {
            throw lastException;
        }

        throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
    }
}
