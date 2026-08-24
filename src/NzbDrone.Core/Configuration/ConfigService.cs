using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public interface IConfigService
{
    void SaveConfigDictionary(Dictionary<string, object> configValues);
    bool GetValueBoolean(string key, bool defaultValue = false);
    string GetValue(string key, string defaultValue = "");
    int GetValueInt(string key, int defaultValue = 0);
    double GetValueDouble(string key, double defaultValue = 0.0);

    // Instance Identity
    string InstanceUuid { get; }

    // General
    string ActiveTorrentEngine { get; }
    string ActiveArchiveExtractor { get; }
    string ActiveMediaInspector { get; }
    string ActiveNetworkBindingProvider { get; }
    string ActiveMediaMetadataProvider { get; }
    string ActiveHttpTransportProvider { get; }
    string ActiveGeoIpProvider { get; }
    string ActiveBlocklistProvider { get; }
    string ActiveAiProvider { get; }
    string OllamaHost { get; }
    string OllamaModel { get; }
    string GeminiApiKey { get; }
    string GeminiModel { get; }
    string OnnxModelPath { get; }
    bool EnableCopilotButton { get; }
    bool EnableNaturalSearch { get; }
    bool EnableSwarmDiagnostics { get; }
    bool AutoStart { get; }
    string ThemeStyle { get; }
    string ColorScheme { get; }
    string DefaultCategory { get; }

    // Storage & Disk
    string DownloadDir { get; }
    string IncompleteDownloadDir { get; }
    int DiskWriteCacheSizeMb { get; }
    string DiskPreAllocationMode { get; }
    int DiskFlushIntervalSeconds { get; }
    int FastResumeIntervalMinutes { get; }

    // Watch Folder
    bool WatchFolderEnabled { get; }
    string WatchFolderPath { get; }
    int WatchFolderScanIntervalSeconds { get; }
    bool WatchFolderAutoStartTorrents { get; }
    bool WatchFolderDeleteAddedTorrents { get; }

    // Connection & Swarm
    string BindInterface { get; }
    bool EnableVpnKillSwitch { get; }
    int ListeningPort { get; }
    bool UpnpEnabled { get; }
    int MaxGlobalConnections { get; }
    int MaxPerTorrentConnections { get; }
    int MaxUploadSlots { get; }
    int MaxActiveDownloads { get; }
    int MaxActiveTorrents { get; }

    // Proxy
    string ProxyType { get; }
    string ProxyHost { get; }
    int ProxyPort { get; }
    bool ProxyAuthEnabled { get; }
    string ProxyUsername { get; }
    string ProxyPassword { get; }

    // BitTorrent Core
    bool EnableDht { get; }
    bool EnablePex { get; }
    bool EnableLpd { get; }
    string EncryptionMode { get; }
    string BitTorrentUserAgent { get; }
    string PeerIdPrefix { get; }
    int AnnounceIntervalSeconds { get; }
    int MinAnnounceIntervalSeconds { get; }
    int ScrapeIntervalSeconds { get; }

    // Storage & Incomplete Staging & Preallocation
    bool EnableIncompleteDir { get; }
    string PreallocationMode { get; }
    bool RenamePartialFiles { get; }
    string Umask { get; }

    // Queue & Concurrency Management
    int DownloadQueueSize { get; }
    int SeedQueueSize { get; }
    bool QueueStalledEnabled { get; }
    int QueueStalledMinutes { get; }
    int IdleSeedingLimitMinutes { get; }

    // Network & Sockets Extended
    string NetworkInterfaceBinding { get; }
    int MaxConnectionsPerIp { get; }
    int MaximumHalfOpenConnections { get; }
    bool AnonymousMode { get; }
    bool ForceProxy { get; }
    int PeerDscp { get; }
    bool PeerPortRandomOnStart { get; }
    int PeerPortRandomLow { get; }
    int PeerPortRandomHigh { get; }

    // MonoTorrent Specific
    int DiskCacheBytes { get; }
    string DiskCachePolicy { get; }
    string FastResumeMode { get; }
    int AutoSaveFastResumeIntervalSeconds { get; }
    bool AutoSaveLoadMagnetMetadata { get; }
    bool AutoSaveLoadDhtCache { get; }
    string PiecePickerStrategy { get; }
    bool EndGamePickerEnabled { get; }
    int StaleRequestTimeoutSeconds { get; }
    int WebSeedDelaySeconds { get; }
    int MaximumDiskReadRateKbps { get; }
    int MaximumDiskWriteRateKbps { get; }

    // libtorrent Specific
    int HashingThreads { get; }
    int AioThreads { get; }
    string DiskIoWriteMode { get; }
    string DiskIoReadMode { get; }
    int FilePoolSize { get; }
    string ChokingAlgorithm { get; }
    string SeedChokingAlgorithm { get; }
    string MixedModeAlgorithm { get; }
    string AlertMask { get; }

    // Transmission Specific
    string ScriptTorrentDoneFilename { get; }
    string ScriptTorrentAddedFilename { get; }
    string ScriptTorrentDoneSeedingFilename { get; }
    bool PrefetchEnabled { get; }
    bool ScrapePausedTorrentsEnabled { get; }
    bool RpcWhitelistEnabled { get; }
    string RpcWhitelist { get; }

    // Swarm & Scripts
    string OnDownloadCompleteScript { get; }
    string OnSeedGoalReachedScript { get; }
    string DefaultTrackers { get; }
    string DhtBootstrapNodes { get; }

    // Speed
    int MaxUploadSpeedKbps { get; }
    int MaxDownloadSpeedKbps { get; }
    bool AlternativeSpeedEnabled { get; }
    int AltUploadSpeedKbps { get; }
    int AltDownloadSpeedKbps { get; }
    double GlobalSeedRatioLimit { get; }

    // Speed Distribution
    string UploadDistributionAlgorithm { get; }
    int UploadDistributionSpreadPercentage { get; }
    string UploadRedistributionMode { get; }
    int UploadCustomIntervalMinutes { get; }
    int UploadStoppedMinPercentage { get; }
    int UploadStoppedMaxPercentage { get; }
    string DownloadDistributionAlgorithm { get; }
    int DownloadDistributionSpreadPercentage { get; }
    string DownloadRedistributionMode { get; }
    int DownloadCustomIntervalMinutes { get; }
    int DownloadStoppedMinPercentage { get; }
    int DownloadStoppedMaxPercentage { get; }
    double SpeedVariationMin { get; }
    double SpeedVariationMax { get; }
    int DownloadThresholdPercent { get; }

    // Scheduler
    bool SchedulerEnabled { get; }
    int SchedulerStartHour { get; }
    int SchedulerStartMinute { get; }
    int SchedulerEndHour { get; }
    int SchedulerEndMinute { get; }
    bool SchedulerMonday { get; }
    bool SchedulerTuesday { get; }
    bool SchedulerWednesday { get; }
    bool SchedulerThursday { get; }
    bool SchedulerFriday { get; }
    bool SchedulerSaturday { get; }
    bool SchedulerSunday { get; }

    // Peer Protocol
    int HandshakeTimeoutSeconds { get; }
    int MessageReadTimeoutSeconds { get; }
    int KeepAliveIntervalSeconds { get; }
    int PeerContactIntervalSeconds { get; }
    int UdpTrackerTimeoutSeconds { get; }
    int HttpTrackerTimeoutSeconds { get; }
    int PeerRequestCount { get; }

    // Peer Behavior
    double SeederUploadActivityProbability { get; }
    double PeerIdleChance { get; }
    double PeerDropoutProbability { get; }
    double ConnectionRotationPercentage { get; }

    // Protocol Extensions
    bool ExtensionUtMetadata { get; }
    bool ExtensionUtPex { get; }
    bool ExtensionLtDontHave { get; }
    bool ExtensionFastExtension { get; }
    bool UtpEnabled { get; }
    bool TcpFallback { get; }
    int TransportConnectionTimeoutSeconds { get; }
    int PexInterval { get; }
    int PexMaxPeersPerMessage { get; }

    // Multi-Tracker
    bool MultiTrackerEnabled { get; }
    bool MultiTrackerFailoverEnabled { get; }
    bool AnnounceToAllTiers { get; }
    bool AnnounceToAllInTier { get; }
    int FailoverMaxConsecutiveFailures { get; }
    int FailoverBackoffBaseSeconds { get; }
    int FailoverMaxBackoffSeconds { get; }

    // DHT
    int DhtRoutingTableSize { get; }
    int DhtAnnouncementInterval { get; }
    int DhtBootstrapTimeout { get; }
    int DhtQueryTimeout { get; }
    int DhtMaxNodes { get; }
    int DhtBucketSize { get; }
    int DhtConcurrentQueries { get; }
    bool DhtAutoBootstrap { get; }
    bool DhtRateLimitEnabled { get; }
    int DhtMaxQueriesPerSecond { get; }

    // Simulation
    bool ClientBehaviorEngineEnabled { get; }
    string PrimaryClient { get; }
    double BehaviorVariation { get; }
    bool ClientProfileSwitching { get; }
    double SwitchClientProbability { get; }
    string TrafficPatternProfile { get; }
    bool RealisticVariations { get; }
    bool TimeBasedPatterns { get; }
    bool SwarmIntelligenceEnabled { get; }
    double SwarmAdaptationRate { get; }
    int SwarmPeerAnalysisDepth { get; }

    // Tracker Server
    bool TrackerServerEnabled { get; }
    bool TrackerHttpEnabled { get; }
    int TrackerHttpPort { get; }
    bool TrackerUdpEnabled { get; }
    int TrackerUdpPort { get; }
    string TrackerBindAddress { get; }
    int TrackerAnnounceInterval { get; }
    int TrackerMaxPeersPerAnnounce { get; }
    bool TrackerEnableScrape { get; }
    bool TrackerPrivateMode { get; }
    bool TrackerLogAnnounces { get; }
    int TrackerRateLimitPerMinute { get; }

    // Media Enrichment
    bool AutoEnrichEnabled { get; }
    string MediaCachePath { get; }
    bool CacheArtworkThumbnails { get; }
    bool AutoPruneRemovedArtwork { get; }

    // Advanced & Logging
    bool LogToFile { get; }
    string FileLogLevel { get; }
    bool DebugMode { get; }
    int UiRefreshRateSec { get; }
}

public class ConfigService : IConfigService
{
    private readonly IBasicRepository<ConfigModel> _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly object _cacheLock = new();
    private Dictionary<string, string> _cache;

    public ConfigService(
        IBasicRepository<ConfigModel> repository,
        IEventAggregator eventAggregator,
        Logger logger)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _logger = logger;
    }

    public void SaveConfigDictionary(Dictionary<string, object> configValues)
    {
        var allConfig = _repository.All().ToDictionary(c => c.Key, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in configValues)
        {
            var strValue = value?.ToString() ?? string.Empty;

            if (allConfig.TryGetValue(key, out var existing))
            {
                existing.Value = strValue;
                _repository.Update(existing);
            }
            else
            {
                _repository.Insert(new ConfigModel { Key = key, Value = strValue });
            }
        }

        lock (_cacheLock)
        {
            _cache = _repository.All()
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        }

        _eventAggregator.PublishEvent(new ConfigSavedEvent());
    }

    public bool GetValueBoolean(string key, bool defaultValue = false)
    {
        var value = GetValue(key, string.Empty);

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public string GetValue(string key, string defaultValue = "")
    {
        var snapshot = _cache;

        if (snapshot == null)
        {
            lock (_cacheLock)
            {
                snapshot = _cache;

                if (snapshot == null)
                {
                    snapshot = _repository.All()
                        .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
                    _cache = snapshot;
                }
            }
        }

        return snapshot.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public int GetValueInt(string key, int defaultValue = 0)
    {
        var value = GetValue(key, string.Empty);

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public double GetValueDouble(string key, double defaultValue = 0.0)
    {
        var value = GetValue(key, string.Empty);

        if (double.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    // Instance Identity
    public string InstanceUuid
    {
        get
        {
            var uuid = GetValue("InstanceUuid", string.Empty);
            if (string.IsNullOrWhiteSpace(uuid))
            {
                uuid = Guid.NewGuid().ToString().ToLowerInvariant();
                SaveConfigDictionary(new Dictionary<string, object> { { "InstanceUuid", uuid } });
                _logger.Info("Generated and saved new instance UUID: {0}", uuid);
            }

            return uuid;
        }
    }

    // General
    public string ActiveTorrentEngine => GetValue("ActiveTorrentEngine", "MonoTorrent");
    public string ActiveArchiveExtractor => GetValue("ActiveArchiveExtractor", "SharpCompress");
    public string ActiveMediaInspector => GetValue("ActiveMediaInspector", "TagLib");
    public string ActiveNetworkBindingProvider => GetValue("ActiveNetworkBindingProvider", "ManagedSocket");
    public string ActiveMediaMetadataProvider => GetValue("ActiveMediaMetadataProvider", "ServarrSync");
    public string ActiveHttpTransportProvider => GetValue("ActiveHttpTransportProvider", "SocketsHttpHandler");
    public string ActiveGeoIpProvider => GetValue("ActiveGeoIpProvider", "MaxMind");
    public string ActiveBlocklistProvider => GetValue("ActiveBlocklistProvider", "RadixTree");
    public string ActiveAiProvider => GetValue("ActiveAiProvider", "RuleHeuristic");
    public string OllamaHost => GetValue("OllamaHost", "http://127.0.0.1:11434");
    public string OllamaModel => GetValue("OllamaModel", "llama3");
    public string GeminiApiKey => GetValue("GeminiApiKey", string.Empty);
    public string GeminiModel => GetValue("GeminiModel", "gemini-2.0-flash");
    public string OnnxModelPath => GetValue("OnnxModelPath", "/config/models/leecharr-ai.onnx");
    public bool EnableCopilotButton => GetValueBoolean("EnableCopilotButton", true);
    public bool EnableNaturalSearch => GetValueBoolean("EnableNaturalSearch", true);
    public bool EnableSwarmDiagnostics => GetValueBoolean("EnableSwarmDiagnostics", true);
    public bool AutoStart => GetValueBoolean("AutoStart", true);
    public string ThemeStyle => GetValue("ThemeStyle", "dark");
    public string ColorScheme => GetValue("ColorScheme", "auto");
    public string DefaultCategory => GetValue("DefaultCategory", string.Empty);

    // Storage & Disk
    public string DownloadDir => GetValue("DownloadDir", string.Empty);
    public string IncompleteDownloadDir => GetValue("IncompleteDownloadDir", string.Empty);
    public int DiskWriteCacheSizeMb => GetValueInt("DiskWriteCacheSizeMb", 128);
    public string DiskPreAllocationMode => GetValue("DiskPreAllocationMode", "sparse");
    public int DiskFlushIntervalSeconds => GetValueInt("DiskFlushIntervalSeconds", 30);
    public int FastResumeIntervalMinutes => GetValueInt("FastResumeIntervalMinutes", 5);

    // Watch Folder
    public bool WatchFolderEnabled => GetValueBoolean("WatchFolderEnabled", false);
    public string WatchFolderPath => GetValue("WatchFolderPath", string.Empty);
    public int WatchFolderScanIntervalSeconds => GetValueInt("WatchFolderScanIntervalSeconds", 10);
    public bool WatchFolderAutoStartTorrents => GetValueBoolean("WatchFolderAutoStartTorrents", true);
    public bool WatchFolderDeleteAddedTorrents => GetValueBoolean("WatchFolderDeleteAddedTorrents", false);

    // Connection & Swarm
    public string BindInterface => GetValue("BindInterface", string.Empty);
    public bool EnableVpnKillSwitch => GetValueBoolean("EnableVpnKillSwitch", false);
    public int ListeningPort => GetValueInt("ListeningPort", 51413);
    public bool UpnpEnabled => GetValueBoolean("UpnpEnabled", true);
    public int MaxGlobalConnections => GetValueInt("MaxGlobalConnections", 300);
    public int MaxPerTorrentConnections => GetValueInt("MaxPerTorrentConnections", 50);
    public int MaxUploadSlots => GetValueInt("MaxUploadSlots", 8);
    public int MaxActiveDownloads => GetValueInt("MaxActiveDownloads", 3);
    public int MaxActiveTorrents => GetValueInt("MaxActiveTorrents", 10);

    // Proxy
    public string ProxyType => GetValue("ProxyType", "none");
    public string ProxyHost => GetValue("ProxyHost", string.Empty);
    public int ProxyPort => GetValueInt("ProxyPort", 8080);
    public bool ProxyAuthEnabled => GetValueBoolean("ProxyAuthEnabled", false);
    public string ProxyUsername => GetValue("ProxyUsername", string.Empty);
    public string ProxyPassword => GetValue("ProxyPassword", string.Empty);

    // BitTorrent Core
    public bool EnableDht => GetValueBoolean("EnableDht", true);
    public bool EnablePex => GetValueBoolean("EnablePex", true);
    public bool EnableLpd => GetValueBoolean("EnableLpd", true);
    public string EncryptionMode => GetValue("EncryptionMode", "preferEncrypted");
    public string BitTorrentUserAgent => GetValue("BitTorrentUserAgent", "Leecharr/1.0");
    public string PeerIdPrefix => GetValue("PeerIdPrefix", "-LC1000-");
    public int AnnounceIntervalSeconds => GetValueInt("AnnounceIntervalSeconds", 1800);
    public int MinAnnounceIntervalSeconds => GetValueInt("MinAnnounceIntervalSeconds", 300);
    public int ScrapeIntervalSeconds => GetValueInt("ScrapeIntervalSeconds", 900);

    // Storage & Incomplete Staging & Preallocation
    public bool EnableIncompleteDir => GetValueBoolean("EnableIncompleteDir", true);
    public string PreallocationMode => GetValue("PreallocationMode", "Sparse");
    public bool RenamePartialFiles => GetValueBoolean("RenamePartialFiles", true);
    public string Umask => GetValue("Umask", "022");

    // Queue & Concurrency Management
    public int DownloadQueueSize => GetValueInt("DownloadQueueSize", 5);
    public int SeedQueueSize => GetValueInt("SeedQueueSize", 10);
    public bool QueueStalledEnabled => GetValueBoolean("QueueStalledEnabled", true);
    public int QueueStalledMinutes => GetValueInt("QueueStalledMinutes", 30);
    public int IdleSeedingLimitMinutes => GetValueInt("IdleSeedingLimitMinutes", 0);

    // Network & Sockets Extended
    public string NetworkInterfaceBinding => GetValue("NetworkInterfaceBinding", string.Empty);
    public int MaxConnectionsPerIp => GetValueInt("MaxConnectionsPerIp", 5);
    public int MaximumHalfOpenConnections => GetValueInt("MaximumHalfOpenConnections", 50);
    public bool AnonymousMode => GetValueBoolean("AnonymousMode", false);
    public bool ForceProxy => GetValueBoolean("ForceProxy", false);
    public int PeerDscp => GetValueInt("PeerDscp", 4);
    public bool PeerPortRandomOnStart => GetValueBoolean("PeerPortRandomOnStart", false);
    public int PeerPortRandomLow => GetValueInt("PeerPortRandomLow", 49152);
    public int PeerPortRandomHigh => GetValueInt("PeerPortRandomHigh", 65535);

    // MonoTorrent Specific
    public int DiskCacheBytes => GetValueInt("DiskCacheBytes", 67108864);
    public string DiskCachePolicy => GetValue("DiskCachePolicy", "ReadsAndWrites");
    public string FastResumeMode => GetValue("FastResumeMode", "BestEffort");
    public int AutoSaveFastResumeIntervalSeconds => GetValueInt("AutoSaveFastResumeIntervalSeconds", 300);
    public bool AutoSaveLoadMagnetMetadata => GetValueBoolean("AutoSaveLoadMagnetMetadata", true);
    public bool AutoSaveLoadDhtCache => GetValueBoolean("AutoSaveLoadDhtCache", true);
    public string PiecePickerStrategy => GetValue("PiecePickerStrategy", "RarestFirst");
    public bool EndGamePickerEnabled => GetValueBoolean("EndGamePickerEnabled", true);
    public int StaleRequestTimeoutSeconds => GetValueInt("StaleRequestTimeoutSeconds", 20);
    public int WebSeedDelaySeconds => GetValueInt("WebSeedDelaySeconds", 30);
    public int MaximumDiskReadRateKbps => GetValueInt("MaximumDiskReadRateKbps", 0);
    public int MaximumDiskWriteRateKbps => GetValueInt("MaximumDiskWriteRateKbps", 0);

    // libtorrent Specific
    public int HashingThreads => GetValueInt("HashingThreads", 2);
    public int AioThreads => GetValueInt("AioThreads", 4);
    public string DiskIoWriteMode => GetValue("DiskIoWriteMode", "OsCacheEnabled");
    public string DiskIoReadMode => GetValue("DiskIoReadMode", "OsCacheEnabled");
    public int FilePoolSize => GetValueInt("FilePoolSize", 256);
    public string ChokingAlgorithm => GetValue("ChokingAlgorithm", "FixedSlots");
    public string SeedChokingAlgorithm => GetValue("SeedChokingAlgorithm", "RoundRobin");
    public string MixedModeAlgorithm => GetValue("MixedModeAlgorithm", "PeerProportional");
    public string AlertMask => GetValue("AlertMask", "Error,Status,Storage,Tracker");

    // Transmission Specific
    public string ScriptTorrentDoneFilename => GetValue("ScriptTorrentDoneFilename", string.Empty);
    public string ScriptTorrentAddedFilename => GetValue("ScriptTorrentAddedFilename", string.Empty);
    public string ScriptTorrentDoneSeedingFilename => GetValue("ScriptTorrentDoneSeedingFilename", string.Empty);
    public bool PrefetchEnabled => GetValueBoolean("PrefetchEnabled", true);
    public bool ScrapePausedTorrentsEnabled => GetValueBoolean("ScrapePausedTorrentsEnabled", true);
    public bool RpcWhitelistEnabled => GetValueBoolean("RpcWhitelistEnabled", false);
    public string RpcWhitelist => GetValue("RpcWhitelist", "127.0.0.1,::1");

    // Swarm & Scripts
    public string OnDownloadCompleteScript => GetValue("OnDownloadCompleteScript", string.Empty);
    public string OnSeedGoalReachedScript => GetValue("OnSeedGoalReachedScript", string.Empty);
    public string DefaultTrackers => GetValue("DefaultTrackers", string.Empty);
    public string DhtBootstrapNodes => GetValue("DhtBootstrapNodes", "router.bittorrent.com:6881,dht.transmissionbt.com:6881,router.utorrent.com:6881,dht.aelitis.com:6881");

    // Speed & Bandwidth
    public int MaxUploadSpeedKbps => GetValueInt("MaxUploadSpeedKbps", 0);
    public int MaxDownloadSpeedKbps => GetValueInt("MaxDownloadSpeedKbps", 0);
    public bool AlternativeSpeedEnabled => GetValueBoolean("AlternativeSpeedEnabled", false);
    public int AltUploadSpeedKbps => GetValueInt("AltUploadSpeedKbps", 500);
    public int AltDownloadSpeedKbps => GetValueInt("AltDownloadSpeedKbps", 2000);
    public double GlobalSeedRatioLimit => GetValueDouble("GlobalSeedRatioLimit", 0.0);

    // Speed Distribution
    public string UploadDistributionAlgorithm => GetValue("UploadDistributionAlgorithm", "Equal");
    public int UploadDistributionSpreadPercentage => GetValueInt("UploadDistributionSpreadPercentage", 50);
    public string UploadRedistributionMode => GetValue("UploadRedistributionMode", "tick");
    public int UploadCustomIntervalMinutes => GetValueInt("UploadCustomIntervalMinutes", 5);
    public int UploadStoppedMinPercentage => GetValueInt("UploadStoppedMinPercentage", 20);
    public int UploadStoppedMaxPercentage => GetValueInt("UploadStoppedMaxPercentage", 40);
    public string DownloadDistributionAlgorithm => GetValue("DownloadDistributionAlgorithm", "Equal");
    public int DownloadDistributionSpreadPercentage => GetValueInt("DownloadDistributionSpreadPercentage", 50);
    public string DownloadRedistributionMode => GetValue("DownloadRedistributionMode", "tick");
    public int DownloadCustomIntervalMinutes => GetValueInt("DownloadCustomIntervalMinutes", 5);
    public int DownloadStoppedMinPercentage => GetValueInt("DownloadStoppedMinPercentage", 20);
    public int DownloadStoppedMaxPercentage => GetValueInt("DownloadStoppedMaxPercentage", 40);
    public double SpeedVariationMin => GetValueDouble("SpeedVariationMin", 0.2);
    public double SpeedVariationMax => GetValueDouble("SpeedVariationMax", 0.8);
    public int DownloadThresholdPercent => GetValueInt("DownloadThresholdPercent", 80);

    // Scheduler
    public bool SchedulerEnabled => GetValueBoolean("SchedulerEnabled", false);
    public int SchedulerStartHour => GetValueInt("SchedulerStartHour", 8);
    public int SchedulerStartMinute => GetValueInt("SchedulerStartMinute", 0);
    public int SchedulerEndHour => GetValueInt("SchedulerEndHour", 23);
    public int SchedulerEndMinute => GetValueInt("SchedulerEndMinute", 0);
    public bool SchedulerMonday => GetValueBoolean("SchedulerMonday", true);
    public bool SchedulerTuesday => GetValueBoolean("SchedulerTuesday", true);
    public bool SchedulerWednesday => GetValueBoolean("SchedulerWednesday", true);
    public bool SchedulerThursday => GetValueBoolean("SchedulerThursday", true);
    public bool SchedulerFriday => GetValueBoolean("SchedulerFriday", true);
    public bool SchedulerSaturday => GetValueBoolean("SchedulerSaturday", true);
    public bool SchedulerSunday => GetValueBoolean("SchedulerSunday", true);

    // Peer Protocol
    public int HandshakeTimeoutSeconds => GetValueInt("HandshakeTimeoutSeconds", 30);
    public int MessageReadTimeoutSeconds => GetValueInt("MessageReadTimeoutSeconds", 60);
    public int KeepAliveIntervalSeconds => GetValueInt("KeepAliveIntervalSeconds", 120);
    public int PeerContactIntervalSeconds => GetValueInt("PeerContactIntervalSeconds", 30);
    public int UdpTrackerTimeoutSeconds => GetValueInt("UdpTrackerTimeoutSeconds", 15);
    public int HttpTrackerTimeoutSeconds => GetValueInt("HttpTrackerTimeoutSeconds", 30);
    public int PeerRequestCount => GetValueInt("PeerRequestCount", 16);

    // Peer Behavior
    public double SeederUploadActivityProbability => GetValueDouble("SeederUploadActivityProbability", 0.7);
    public double PeerIdleChance => GetValueDouble("PeerIdleChance", 0.1);
    public double PeerDropoutProbability => GetValueDouble("PeerDropoutProbability", 0.05);
    public double ConnectionRotationPercentage => GetValueDouble("ConnectionRotationPercentage", 0.2);

    // Protocol Extensions
    public bool ExtensionUtMetadata => GetValueBoolean("ExtensionUtMetadata", true);
    public bool ExtensionUtPex => GetValueBoolean("ExtensionUtPex", true);
    public bool ExtensionLtDontHave => GetValueBoolean("ExtensionLtDontHave", true);
    public bool ExtensionFastExtension => GetValueBoolean("ExtensionFastExtension", true);
    public bool UtpEnabled => GetValueBoolean("UtpEnabled", true);
    public bool TcpFallback => GetValueBoolean("TcpFallback", true);
    public int TransportConnectionTimeoutSeconds => GetValueInt("TransportConnectionTimeoutSeconds", 30);
    public int PexInterval => GetValueInt("PexInterval", 60);
    public int PexMaxPeersPerMessage => GetValueInt("PexMaxPeersPerMessage", 50);

    // Multi-Tracker
    public bool MultiTrackerEnabled => GetValueBoolean("MultiTrackerEnabled", true);
    public bool MultiTrackerFailoverEnabled => GetValueBoolean("MultiTrackerFailoverEnabled", true);
    public bool AnnounceToAllTiers => GetValueBoolean("AnnounceToAllTiers", true);
    public bool AnnounceToAllInTier => GetValueBoolean("AnnounceToAllInTier", false);
    public int FailoverMaxConsecutiveFailures => GetValueInt("FailoverMaxConsecutiveFailures", 3);
    public int FailoverBackoffBaseSeconds => GetValueInt("FailoverBackoffBaseSeconds", 30);
    public int FailoverMaxBackoffSeconds => GetValueInt("FailoverMaxBackoffSeconds", 1800);

    // DHT
    public int DhtRoutingTableSize => GetValueInt("DhtRoutingTableSize", 200);
    public int DhtAnnouncementInterval => GetValueInt("DhtAnnouncementInterval", 1800);
    public int DhtBootstrapTimeout => GetValueInt("DhtBootstrapTimeout", 30);
    public int DhtQueryTimeout => GetValueInt("DhtQueryTimeout", 15);
    public int DhtMaxNodes => GetValueInt("DhtMaxNodes", 1000);
    public int DhtBucketSize => GetValueInt("DhtBucketSize", 8);
    public int DhtConcurrentQueries => GetValueInt("DhtConcurrentQueries", 4);
    public bool DhtAutoBootstrap => GetValueBoolean("DhtAutoBootstrap", true);
    public bool DhtRateLimitEnabled => GetValueBoolean("DhtRateLimitEnabled", true);
    public int DhtMaxQueriesPerSecond => GetValueInt("DhtMaxQueriesPerSecond", 30);

    // Simulation
    public bool ClientBehaviorEngineEnabled => GetValueBoolean("ClientBehaviorEngineEnabled", true);
    public string PrimaryClient => GetValue("PrimaryClient", "qBittorrent");
    public double BehaviorVariation => GetValueDouble("BehaviorVariation", 0.15);
    public bool ClientProfileSwitching => GetValueBoolean("ClientProfileSwitching", false);
    public double SwitchClientProbability => GetValueDouble("SwitchClientProbability", 0.05);
    public string TrafficPatternProfile => GetValue("TrafficPatternProfile", "HomeUser");
    public bool RealisticVariations => GetValueBoolean("RealisticVariations", true);
    public bool TimeBasedPatterns => GetValueBoolean("TimeBasedPatterns", true);
    public bool SwarmIntelligenceEnabled => GetValueBoolean("SwarmIntelligenceEnabled", true);
    public double SwarmAdaptationRate => GetValueDouble("SwarmAdaptationRate", 0.1);
    public int SwarmPeerAnalysisDepth => GetValueInt("SwarmPeerAnalysisDepth", 10);

    // Tracker Server
    public bool TrackerServerEnabled => GetValueBoolean("TrackerServerEnabled", false);
    public bool TrackerHttpEnabled => GetValueBoolean("TrackerHttpEnabled", true);
    public int TrackerHttpPort => GetValueInt("TrackerHttpPort", 6969);
    public bool TrackerUdpEnabled => GetValueBoolean("TrackerUdpEnabled", true);
    public int TrackerUdpPort => GetValueInt("TrackerUdpPort", 6969);
    public string TrackerBindAddress => GetValue("TrackerBindAddress", "0.0.0.0");
    public int TrackerAnnounceInterval => GetValueInt("TrackerAnnounceInterval", 1800);
    public int TrackerMaxPeersPerAnnounce => GetValueInt("TrackerMaxPeersPerAnnounce", 50);
    public bool TrackerEnableScrape => GetValueBoolean("TrackerEnableScrape", true);
    public bool TrackerPrivateMode => GetValueBoolean("TrackerPrivateMode", false);
    public bool TrackerLogAnnounces => GetValueBoolean("TrackerLogAnnounces", false);
    public int TrackerRateLimitPerMinute => GetValueInt("TrackerRateLimitPerMinute", 60);

    // Media Enrichment
    public bool AutoEnrichEnabled => GetValueBoolean("AutoEnrichEnabled", true);
    public string MediaCachePath => GetValue("MediaCachePath", string.Empty);
    public bool CacheArtworkThumbnails => GetValueBoolean("CacheArtworkThumbnails", true);
    public bool AutoPruneRemovedArtwork => GetValueBoolean("AutoPruneRemovedArtwork", true);

    // Advanced & Logging
    public bool LogToFile => GetValueBoolean("LogToFile", true);
    public string FileLogLevel => GetValue("FileLogLevel", "Info");
    public bool DebugMode => GetValueBoolean("DebugMode", false);
    public int UiRefreshRateSec => GetValueInt("UiRefreshRateSec", 2);
}

public class ConfigSavedEvent : IEvent
{
}
