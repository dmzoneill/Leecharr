// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public interface IConfigService
{
    void SaveConfigDictionary(Dictionary<string, object> configValues);

    bool GetValueBoolean(string key, bool defaultValue = false);

    string GetValue(string key, string defaultValue = "");

    int GetValueInt(string key, int defaultValue = 0);

    long GetValueLong(string key, long defaultValue = 0);

    double GetValueDouble(string key, double defaultValue = 0.0);

    // Instance Identity
    string InstanceUuid { get; }

    // General
    string ActiveTorrentEngine { get; }

    string ActiveArchiveExtractor { get; }

    bool AutoExtractArchives { get; }

    string ActiveMediaInspector { get; }

    string ActiveNetworkBindingProvider { get; }

    string ActiveMediaMetadataProvider { get; }

    string ActiveHttpTransportProvider { get; }

    string ActiveGeoIpProvider { get; }

    string ActiveBlocklistProvider { get; }

    bool BlocklistEnabled { get; }

    string BlocklistUrl { get; }

    string BlocklistPath { get; }

    int BlocklistUpdateIntervalHours { get; }

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

    int MaxActiveUploads { get; }

    int MaxActiveTorrents { get; }

    bool IgnoreSlowTorrents { get; }

    long SlowTorrentDownloadRateThreshold { get; }

    long SlowTorrentUploadRateThreshold { get; }

    string GlobalShareLimitAction { get; }

    bool AppendIncompleteExtension { get; }

    string IncompleteExtension { get; }

    bool EnableIPv6 { get; }

    bool CsrfProtectionEnabled { get; }

    bool HostHeaderValidationEnabled { get; }

    string AllowedHosts { get; }

    string AutoShutdownAction { get; }

    string AutoShutdownCondition { get; }

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

    bool EnableBep27PrivateTorrents { get; }

    string EncryptionMode { get; }

    string BitTorrentUserAgent { get; }

    string PeerIdPrefix { get; }

    int AnnounceIntervalSeconds { get; }

    int MinAnnounceIntervalSeconds { get; }

    int ScrapeIntervalSeconds { get; }

    // Storage & Incomplete Staging & Preallocation
    bool EnableIncompleteDir { get; }

    string PreallocationMode { get; }

    string DiskPreAllocationMode { get; }

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

    int TrackerMaxSwarms { get; }

    // Media Enrichment
    bool AutoEnrichEnabled { get; }

    string MediaCachePath { get; }

    bool CacheArtworkThumbnails { get; }

    bool AutoPruneRemovedArtwork { get; }

    string TmdbApiKey { get; }

    // Advanced & Logging
    bool LogToFile { get; }

    string FileLogLevel { get; }

    bool DebugMode { get; }

    int UiRefreshRateSec { get; }
}

public class ConfigService : IConfigService
{
    private readonly IBasicRepository<ConfigModel> repository;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly object cacheLock = new();
    private Dictionary<string, string> cache;

    public ConfigService(
        IBasicRepository<ConfigModel> repository,
        IEventAggregator eventAggregator,
        Logger logger)
    {
        this.repository = repository;
        this.eventAggregator = eventAggregator;
        this.logger = logger;
    }

    public void SaveConfigDictionary(Dictionary<string, object> configValues)
    {
        var allConfig = this.repository.All().ToDictionary(c => c.Key, c => c, StringComparer.OrdinalIgnoreCase);

        if (configValues.ContainsKey("DownloadQueueSize") && !configValues.ContainsKey("MaxActiveDownloads"))
        {
            configValues["MaxActiveDownloads"] = configValues["DownloadQueueSize"];
        }
        else if (configValues.ContainsKey("MaxActiveDownloads") && !configValues.ContainsKey("DownloadQueueSize"))
        {
            configValues["DownloadQueueSize"] = configValues["MaxActiveDownloads"];
        }

        if (configValues.ContainsKey("SeedQueueSize") && !configValues.ContainsKey("MaxActiveUploads"))
        {
            configValues["MaxActiveUploads"] = configValues["SeedQueueSize"];
        }
        else if (configValues.ContainsKey("MaxActiveUploads") && !configValues.ContainsKey("SeedQueueSize"))
        {
            configValues["SeedQueueSize"] = configValues["MaxActiveUploads"];
        }

        foreach (var (key, value) in configValues)
        {
            var strValue = value?.ToString() ?? string.Empty;

            if (allConfig.TryGetValue(key, out var existing))
            {
                existing.Value = strValue;
                this.repository.Update(existing);
            }
            else
            {
                this.repository.Insert(new ConfigModel { Key = key, Value = strValue });
            }
        }

        lock (this.cacheLock)
        {
            this.cache = this.repository.All()
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        }

        this.eventAggregator.PublishEvent(new ConfigSavedEvent());
    }

    public bool GetValueBoolean(string key, bool defaultValue = false)
    {
        var value = this.GetValue(key, string.Empty);

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public string GetValue(string key, string defaultValue = "")
    {
        var snapshot = this.cache;

        if (snapshot == null)
        {
            lock (this.cacheLock)
            {
                snapshot = this.cache;

                if (snapshot == null)
                {
                    snapshot = this.repository.All()
                        .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
                    this.cache = snapshot;
                }
            }
        }

        return snapshot.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public int GetValueInt(string key, int defaultValue = 0)
    {
        var value = this.GetValue(key, string.Empty);

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public long GetValueLong(string key, long defaultValue = 0)
    {
        var value = this.GetValue(key, string.Empty);

        if (long.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public double GetValueDouble(string key, double defaultValue = 0.0)
    {
        var value = this.GetValue(key, string.Empty);

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
            var uuid = this.GetValue("InstanceUuid", string.Empty);
            if (string.IsNullOrWhiteSpace(uuid))
            {
                uuid = Guid.NewGuid().ToString().ToLowerInvariant();
                this.SaveConfigDictionary(new Dictionary<string, object> { { "InstanceUuid", uuid } });
                this.logger.Info("Generated and saved new instance UUID: {0}", uuid);
            }

            return uuid;
        }
    }

    // General
    public string ActiveTorrentEngine => this.GetValue("ActiveTorrentEngine", "MonoTorrent");

    public string ActiveArchiveExtractor => this.GetValue("ActiveArchiveExtractor", "SharpCompress");

    public bool AutoExtractArchives => this.GetValueBoolean("AutoExtractArchives", false);

    public string ActiveMediaInspector => this.GetValue("ActiveMediaInspector", "TagLib");

    public string ActiveNetworkBindingProvider => this.GetValue("ActiveNetworkBindingProvider", "ManagedSocket");

    public string ActiveMediaMetadataProvider => this.GetValue("ActiveMediaMetadataProvider", "ServarrSync");

    public string ActiveHttpTransportProvider => this.GetValue("ActiveHttpTransportProvider", "SocketsHttpHandler");

    public string ActiveGeoIpProvider => this.GetValue("ActiveGeoIpProvider", "MaxMind");

    public string ActiveBlocklistProvider => this.GetValue("ActiveBlocklistProvider", "RadixTree");

    public bool BlocklistEnabled => this.GetValueBoolean("BlocklistEnabled", false);

    public string BlocklistUrl => this.GetValue("BlocklistUrl", string.Empty);

    public string BlocklistPath => this.GetValue("BlocklistPath", string.Empty);

    public int BlocklistUpdateIntervalHours => this.GetValueInt("BlocklistUpdateIntervalHours", 24);

    public string ActiveAiProvider => this.GetValue("ActiveAiProvider", "RuleHeuristic");

    public string OllamaHost => this.GetValue("OllamaHost", "http://127.0.0.1:11434");

    public string OllamaModel => this.GetValue("OllamaModel", "llama3");

    public string GeminiApiKey => this.GetValue("GeminiApiKey", string.Empty);

    public string GeminiModel => this.GetValue("GeminiModel", "gemini-2.0-flash");

    public string OnnxModelPath => this.GetValue("OnnxModelPath", "/config/models/leecharr-ai.onnx");

    public bool EnableCopilotButton => this.GetValueBoolean("EnableCopilotButton", true);

    public bool EnableNaturalSearch => this.GetValueBoolean("EnableNaturalSearch", true);

    public bool EnableSwarmDiagnostics => this.GetValueBoolean("EnableSwarmDiagnostics", true);

    public bool AutoStart => this.GetValueBoolean("AutoStart", true);

    public string ThemeStyle => this.GetValue("ThemeStyle", "dark");

    public string ColorScheme => this.GetValue("ColorScheme", "auto");

    public string DefaultCategory => this.GetValue("DefaultCategory", string.Empty);

    // Storage & Disk
    public string DownloadDir => this.GetValue("DownloadDir", string.Empty);

    public string IncompleteDownloadDir => this.GetValue("IncompleteDownloadDir", string.Empty);

    public int DiskWriteCacheSizeMb => this.GetValueInt("DiskWriteCacheSizeMb", 128);

    public int DiskFlushIntervalSeconds => this.GetValueInt("DiskFlushIntervalSeconds", 30);

    public int FastResumeIntervalMinutes => this.GetValueInt("FastResumeIntervalMinutes", 5);

    // Watch Folder
    public bool WatchFolderEnabled => this.GetValueBoolean("WatchFolderEnabled", false);

    public string WatchFolderPath => this.GetValue("WatchFolderPath", string.Empty);

    public int WatchFolderScanIntervalSeconds => this.GetValueInt("WatchFolderScanIntervalSeconds", 10);

    public bool WatchFolderAutoStartTorrents => this.GetValueBoolean("WatchFolderAutoStartTorrents", true);

    public bool WatchFolderDeleteAddedTorrents => this.GetValueBoolean("WatchFolderDeleteAddedTorrents", false);

    // Connection & Swarm
    public string BindInterface => this.GetValue("BindInterface", string.Empty);

    public bool EnableVpnKillSwitch => this.GetValueBoolean("EnableVpnKillSwitch", false);

    public int ListeningPort => this.GetValueInt("ListeningPort", 51413);

    public bool UpnpEnabled => this.GetValueBoolean("UpnpEnabled", true);

    public int MaxGlobalConnections => this.GetValueInt("MaxGlobalConnections", 300);

    public int MaxPerTorrentConnections => this.GetValueInt("MaxPerTorrentConnections", 50);

    public int MaxUploadSlots => this.GetValueInt("MaxUploadSlots", 8);

    public int MaxActiveDownloads => this.GetValueInt("MaxActiveDownloads", this.GetValueInt("DownloadQueueSize", 3));

    public int MaxActiveUploads => this.GetValueInt("MaxActiveUploads", this.GetValueInt("SeedQueueSize", 3));

    public int MaxActiveTorrents => this.GetValueInt("MaxActiveTorrents", 10);

    public bool IgnoreSlowTorrents => this.GetValueBoolean("IgnoreSlowTorrents", false);

    public long SlowTorrentDownloadRateThreshold => this.GetValueLong("SlowTorrentDownloadRateThreshold", 2048);

    public long SlowTorrentUploadRateThreshold => this.GetValueLong("SlowTorrentUploadRateThreshold", 2048);

    public string GlobalShareLimitAction => this.GetValue("GlobalShareLimitAction", "Pause");

    public bool AppendIncompleteExtension => this.GetValueBoolean("AppendIncompleteExtension", false);

    public string IncompleteExtension => this.GetValue("IncompleteExtension", ".!leech");

    public bool EnableIPv6 => this.GetValueBoolean("EnableIPv6", true);

    public bool CsrfProtectionEnabled => this.GetValueBoolean("CsrfProtectionEnabled", true);

    public bool HostHeaderValidationEnabled => this.GetValueBoolean("HostHeaderValidationEnabled", false);

    public string AllowedHosts => this.GetValue("AllowedHosts", string.Empty);

    public string AutoShutdownAction => this.GetValue("AutoShutdownAction", "None");

    public string AutoShutdownCondition => this.GetValue("AutoShutdownCondition", "None");

    // Proxy
    public string ProxyType => this.GetValue("ProxyType", "none");

    public string ProxyHost => this.GetValue("ProxyHost", string.Empty);

    public int ProxyPort => this.GetValueInt("ProxyPort", 8080);

    public bool ProxyAuthEnabled => this.GetValueBoolean("ProxyAuthEnabled", false);

    public string ProxyUsername => this.GetValue("ProxyUsername", string.Empty);

    public string ProxyPassword => this.GetValue("ProxyPassword", string.Empty);

    // BitTorrent Core
    public bool EnableDht => this.GetValueBoolean("EnableDht", true);

    public bool EnablePex => this.GetValueBoolean("EnablePex", true);

    public bool EnableLpd => this.GetValueBoolean("EnableLpd", true);

    public bool EnableBep27PrivateTorrents => this.GetValueBoolean("EnableBep27PrivateTorrents", true);

    public string EncryptionMode => this.GetValue("EncryptionMode", "preferEncrypted");

    public string BitTorrentUserAgent
    {
        get
        {
            var val = this.GetValue("BitTorrentUserAgent", string.Empty);
            if (!string.IsNullOrWhiteSpace(val) && val != "Leecharr/1.0")
            {
                return val;
            }

            return ClientEmulationPresets.GetPreset(this.PrimaryClient).UserAgent;
        }
    }

    public string PeerIdPrefix
    {
        get
        {
            var val = this.GetValue("PeerIdPrefix", string.Empty);
            if (!string.IsNullOrWhiteSpace(val) && val != "-LC1000-")
            {
                return val;
            }

            return ClientEmulationPresets.GetPreset(this.PrimaryClient).PeerIdPrefix;
        }
    }

    public int AnnounceIntervalSeconds => this.GetValueInt("AnnounceIntervalSeconds", 1800);

    public int MinAnnounceIntervalSeconds => this.GetValueInt("MinAnnounceIntervalSeconds", 300);

    public int ScrapeIntervalSeconds => this.GetValueInt("ScrapeIntervalSeconds", 900);

    // Storage & Incomplete Staging & Preallocation
    public bool EnableIncompleteDir => this.GetValueBoolean("EnableIncompleteDir", true);

    public string PreallocationMode => this.GetValue("PreallocationMode", this.GetValue("DiskPreAllocationMode", "Sparse"));

    public string DiskPreAllocationMode => this.PreallocationMode;

    public bool RenamePartialFiles => this.GetValueBoolean("RenamePartialFiles", true);

    public string Umask => this.GetValue("Umask", "022");

    // Queue & Concurrency Management
    public int DownloadQueueSize => this.GetValueInt("DownloadQueueSize", this.GetValueInt("MaxActiveDownloads", 5));

    public int SeedQueueSize => this.GetValueInt("SeedQueueSize", this.GetValueInt("MaxActiveUploads", 10));

    public bool QueueStalledEnabled => this.GetValueBoolean("QueueStalledEnabled", true);

    public int QueueStalledMinutes => this.GetValueInt("QueueStalledMinutes", 30);

    public int IdleSeedingLimitMinutes => this.GetValueInt("IdleSeedingLimitMinutes", 0);

    // Network & Sockets Extended
    public string NetworkInterfaceBinding => this.GetValue("NetworkInterfaceBinding", string.Empty);

    public int MaxConnectionsPerIp => this.GetValueInt("MaxConnectionsPerIp", 5);

    public int MaximumHalfOpenConnections => this.GetValueInt("MaximumHalfOpenConnections", 50);

    public bool AnonymousMode => this.GetValueBoolean("AnonymousMode", false);

    public bool ForceProxy => this.GetValueBoolean("ForceProxy", false);

    public int PeerDscp => this.GetValueInt("PeerDscp", 4);

    public bool PeerPortRandomOnStart => this.GetValueBoolean("PeerPortRandomOnStart", false);

    public int PeerPortRandomLow => this.GetValueInt("PeerPortRandomLow", 49152);

    public int PeerPortRandomHigh => this.GetValueInt("PeerPortRandomHigh", 65535);

    // MonoTorrent Specific
    public int DiskCacheBytes => this.GetValueInt("DiskCacheBytes", 67108864);

    public string DiskCachePolicy => this.GetValue("DiskCachePolicy", "ReadsAndWrites");

    public string FastResumeMode => this.GetValue("FastResumeMode", "BestEffort");

    public int AutoSaveFastResumeIntervalSeconds => this.GetValueInt("AutoSaveFastResumeIntervalSeconds", 300);

    public bool AutoSaveLoadMagnetMetadata => this.GetValueBoolean("AutoSaveLoadMagnetMetadata", true);

    public bool AutoSaveLoadDhtCache => this.GetValueBoolean("AutoSaveLoadDhtCache", true);

    public string PiecePickerStrategy => this.GetValue("PiecePickerStrategy", "RarestFirst");

    public bool EndGamePickerEnabled => this.GetValueBoolean("EndGamePickerEnabled", true);

    public int StaleRequestTimeoutSeconds => this.GetValueInt("StaleRequestTimeoutSeconds", 20);

    public int WebSeedDelaySeconds => this.GetValueInt("WebSeedDelaySeconds", 30);

    public int MaximumDiskReadRateKbps => this.GetValueInt("MaximumDiskReadRateKbps", 0);

    public int MaximumDiskWriteRateKbps => this.GetValueInt("MaximumDiskWriteRateKbps", 0);

    // libtorrent Specific
    public int HashingThreads => this.GetValueInt("HashingThreads", 2);

    public int AioThreads => this.GetValueInt("AioThreads", 4);

    public string DiskIoWriteMode => this.GetValue("DiskIoWriteMode", "OsCacheEnabled");

    public string DiskIoReadMode => this.GetValue("DiskIoReadMode", "OsCacheEnabled");

    public int FilePoolSize => this.GetValueInt("FilePoolSize", 256);

    public string ChokingAlgorithm => this.GetValue("ChokingAlgorithm", "FixedSlots");

    public string SeedChokingAlgorithm => this.GetValue("SeedChokingAlgorithm", "RoundRobin");

    public string MixedModeAlgorithm => this.GetValue("MixedModeAlgorithm", "PeerProportional");

    public string AlertMask => this.GetValue("AlertMask", "Error,Status,Storage,Tracker");

    // Transmission Specific
    public string ScriptTorrentDoneFilename => this.GetValue("ScriptTorrentDoneFilename", string.Empty);

    public string ScriptTorrentAddedFilename => this.GetValue("ScriptTorrentAddedFilename", string.Empty);

    public string ScriptTorrentDoneSeedingFilename => this.GetValue("ScriptTorrentDoneSeedingFilename", string.Empty);

    public bool PrefetchEnabled => this.GetValueBoolean("PrefetchEnabled", true);

    public bool ScrapePausedTorrentsEnabled => this.GetValueBoolean("ScrapePausedTorrentsEnabled", true);

    public bool RpcWhitelistEnabled => this.GetValueBoolean("RpcWhitelistEnabled", false);

    public string RpcWhitelist => this.GetValue("RpcWhitelist", "127.0.0.1,::1");

    // Swarm & Scripts
    public string OnDownloadCompleteScript => this.GetValue("OnDownloadCompleteScript", string.Empty);

    public string OnSeedGoalReachedScript => this.GetValue("OnSeedGoalReachedScript", string.Empty);

    public string DefaultTrackers => this.GetValue("DefaultTrackers", string.Empty);

    public string DhtBootstrapNodes => this.GetValue("DhtBootstrapNodes", "router.bittorrent.com:6881,dht.transmissionbt.com:6881,router.utorrent.com:6881,dht.aelitis.com:6881");

    // Speed & Bandwidth
    public int MaxUploadSpeedKbps => this.GetValueInt("MaxUploadSpeedKbps", 0);

    public int MaxDownloadSpeedKbps => this.GetValueInt("MaxDownloadSpeedKbps", 0);

    public bool AlternativeSpeedEnabled => this.GetValueBoolean("AlternativeSpeedEnabled", false);

    public int AltUploadSpeedKbps => this.GetValueInt("AltUploadSpeedKbps", 500);

    public int AltDownloadSpeedKbps => this.GetValueInt("AltDownloadSpeedKbps", 2000);

    public double GlobalSeedRatioLimit => this.GetValueDouble("GlobalSeedRatioLimit", 0.0);

    // Speed Distribution
    public string UploadDistributionAlgorithm => this.GetValue("UploadDistributionAlgorithm", "Equal");

    public int UploadDistributionSpreadPercentage => this.GetValueInt("UploadDistributionSpreadPercentage", 50);

    public string UploadRedistributionMode => this.GetValue("UploadRedistributionMode", "tick");

    public int UploadCustomIntervalMinutes => this.GetValueInt("UploadCustomIntervalMinutes", 5);

    public int UploadStoppedMinPercentage => this.GetValueInt("UploadStoppedMinPercentage", 20);

    public int UploadStoppedMaxPercentage => this.GetValueInt("UploadStoppedMaxPercentage", 40);

    public string DownloadDistributionAlgorithm => this.GetValue("DownloadDistributionAlgorithm", "Equal");

    public int DownloadDistributionSpreadPercentage => this.GetValueInt("DownloadDistributionSpreadPercentage", 50);

    public string DownloadRedistributionMode => this.GetValue("DownloadRedistributionMode", "tick");

    public int DownloadCustomIntervalMinutes => this.GetValueInt("DownloadCustomIntervalMinutes", 5);

    public int DownloadStoppedMinPercentage => this.GetValueInt("DownloadStoppedMinPercentage", 20);

    public int DownloadStoppedMaxPercentage => this.GetValueInt("DownloadStoppedMaxPercentage", 40);

    public double SpeedVariationMin => this.GetValueDouble("SpeedVariationMin", 0.2);

    public double SpeedVariationMax => this.GetValueDouble("SpeedVariationMax", 0.8);

    public int DownloadThresholdPercent => this.GetValueInt("DownloadThresholdPercent", 80);

    // Scheduler
    public bool SchedulerEnabled => this.GetValueBoolean("SchedulerEnabled", false);

    public int SchedulerStartHour => this.GetValueInt("SchedulerStartHour", 8);

    public int SchedulerStartMinute => this.GetValueInt("SchedulerStartMinute", 0);

    public int SchedulerEndHour => this.GetValueInt("SchedulerEndHour", 23);

    public int SchedulerEndMinute => this.GetValueInt("SchedulerEndMinute", 0);

    public bool SchedulerMonday => this.GetValueBoolean("SchedulerMonday", true);

    public bool SchedulerTuesday => this.GetValueBoolean("SchedulerTuesday", true);

    public bool SchedulerWednesday => this.GetValueBoolean("SchedulerWednesday", true);

    public bool SchedulerThursday => this.GetValueBoolean("SchedulerThursday", true);

    public bool SchedulerFriday => this.GetValueBoolean("SchedulerFriday", true);

    public bool SchedulerSaturday => this.GetValueBoolean("SchedulerSaturday", true);

    public bool SchedulerSunday => this.GetValueBoolean("SchedulerSunday", true);

    // Peer Protocol
    public int HandshakeTimeoutSeconds => this.GetValueInt("HandshakeTimeoutSeconds", 30);

    public int MessageReadTimeoutSeconds => this.GetValueInt("MessageReadTimeoutSeconds", 60);

    public int KeepAliveIntervalSeconds => this.GetValueInt("KeepAliveIntervalSeconds", 120);

    public int PeerContactIntervalSeconds => this.GetValueInt("PeerContactIntervalSeconds", 30);

    public int UdpTrackerTimeoutSeconds => this.GetValueInt("UdpTrackerTimeoutSeconds", 15);

    public int HttpTrackerTimeoutSeconds => this.GetValueInt("HttpTrackerTimeoutSeconds", 30);

    public int PeerRequestCount => this.GetValueInt("PeerRequestCount", 16);

    // Peer Behavior
    public double SeederUploadActivityProbability => this.GetValueDouble("SeederUploadActivityProbability", 0.7);

    public double PeerIdleChance => this.GetValueDouble("PeerIdleChance", 0.1);

    public double PeerDropoutProbability => this.GetValueDouble("PeerDropoutProbability", 0.05);

    public double ConnectionRotationPercentage => this.GetValueDouble("ConnectionRotationPercentage", 0.2);

    // Protocol Extensions
    public bool ExtensionUtMetadata => this.GetValueBoolean("ExtensionUtMetadata", true);

    public bool ExtensionUtPex => this.GetValueBoolean("ExtensionUtPex", true);

    public bool ExtensionLtDontHave => this.GetValueBoolean("ExtensionLtDontHave", true);

    public bool ExtensionFastExtension => this.GetValueBoolean("ExtensionFastExtension", true);

    public bool UtpEnabled => this.GetValueBoolean("UtpEnabled", true);

    public bool TcpFallback => this.GetValueBoolean("TcpFallback", true);

    public int TransportConnectionTimeoutSeconds => this.GetValueInt("TransportConnectionTimeoutSeconds", 30);

    public int PexInterval => this.GetValueInt("PexInterval", 60);

    public int PexMaxPeersPerMessage => this.GetValueInt("PexMaxPeersPerMessage", 50);

    // Multi-Tracker
    public bool MultiTrackerEnabled => this.GetValueBoolean("MultiTrackerEnabled", true);

    public bool MultiTrackerFailoverEnabled => this.GetValueBoolean("MultiTrackerFailoverEnabled", true);

    public bool AnnounceToAllTiers => this.GetValueBoolean("AnnounceToAllTiers", true);

    public bool AnnounceToAllInTier => this.GetValueBoolean("AnnounceToAllInTier", false);

    public int FailoverMaxConsecutiveFailures => this.GetValueInt("FailoverMaxConsecutiveFailures", 3);

    public int FailoverBackoffBaseSeconds => this.GetValueInt("FailoverBackoffBaseSeconds", 30);

    public int FailoverMaxBackoffSeconds => this.GetValueInt("FailoverMaxBackoffSeconds", 1800);

    // DHT
    public int DhtRoutingTableSize => this.GetValueInt("DhtRoutingTableSize", 200);

    public int DhtAnnouncementInterval => this.GetValueInt("DhtAnnouncementInterval", 1800);

    public int DhtBootstrapTimeout => this.GetValueInt("DhtBootstrapTimeout", 30);

    public int DhtQueryTimeout => this.GetValueInt("DhtQueryTimeout", 15);

    public int DhtMaxNodes => this.GetValueInt("DhtMaxNodes", 1000);

    public int DhtBucketSize => this.GetValueInt("DhtBucketSize", 8);

    public int DhtConcurrentQueries => this.GetValueInt("DhtConcurrentQueries", 4);

    public bool DhtAutoBootstrap => this.GetValueBoolean("DhtAutoBootstrap", true);

    public bool DhtRateLimitEnabled => this.GetValueBoolean("DhtRateLimitEnabled", true);

    public int DhtMaxQueriesPerSecond => this.GetValueInt("DhtMaxQueriesPerSecond", 30);

    // Simulation
    public bool ClientBehaviorEngineEnabled => this.GetValueBoolean("ClientBehaviorEngineEnabled", true);

    public string PrimaryClient => this.GetValue("PrimaryClient", "qBittorrent");

    public double BehaviorVariation => this.GetValueDouble("BehaviorVariation", 0.15);

    public bool ClientProfileSwitching => this.GetValueBoolean("ClientProfileSwitching", false);

    public double SwitchClientProbability => this.GetValueDouble("SwitchClientProbability", 0.05);

    public string TrafficPatternProfile => this.GetValue("TrafficPatternProfile", "HomeUser");

    public bool RealisticVariations => this.GetValueBoolean("RealisticVariations", true);

    public bool TimeBasedPatterns => this.GetValueBoolean("TimeBasedPatterns", true);

    public bool SwarmIntelligenceEnabled => this.GetValueBoolean("SwarmIntelligenceEnabled", true);

    public double SwarmAdaptationRate => this.GetValueDouble("SwarmAdaptationRate", 0.1);

    public int SwarmPeerAnalysisDepth => this.GetValueInt("SwarmPeerAnalysisDepth", 10);

    // Tracker Server
    public bool TrackerServerEnabled => this.GetValueBoolean("TrackerServerEnabled", false);

    public bool TrackerHttpEnabled => this.GetValueBoolean("TrackerHttpEnabled", true);

    public int TrackerHttpPort => this.GetValueInt("TrackerHttpPort", 6969);

    public bool TrackerUdpEnabled => this.GetValueBoolean("TrackerUdpEnabled", true);

    public int TrackerUdpPort => this.GetValueInt("TrackerUdpPort", 6969);

    public string TrackerBindAddress => this.GetValue("TrackerBindAddress", "0.0.0.0");

    public int TrackerAnnounceInterval => this.GetValueInt("TrackerAnnounceInterval", 1800);

    public int TrackerMaxPeersPerAnnounce => this.GetValueInt("TrackerMaxPeersPerAnnounce", 50);

    public bool TrackerEnableScrape => this.GetValueBoolean("TrackerEnableScrape", true);

    public bool TrackerPrivateMode => this.GetValueBoolean("TrackerPrivateMode", false);

    public bool TrackerLogAnnounces => this.GetValueBoolean("TrackerLogAnnounces", false);

    public int TrackerRateLimitPerMinute => this.GetValueInt("TrackerRateLimitPerMinute", 60);

    public int TrackerMaxSwarms => this.GetValueInt("TrackerMaxSwarms", 20000);

    // Media Enrichment
    public bool AutoEnrichEnabled => this.GetValueBoolean("AutoEnrichEnabled", true);

    public string MediaCachePath => this.GetValue("MediaCachePath", string.Empty);

    public bool CacheArtworkThumbnails => this.GetValueBoolean("CacheArtworkThumbnails", true);

    public bool AutoPruneRemovedArtwork => this.GetValueBoolean("AutoPruneRemovedArtwork", true);

    public string TmdbApiKey => this.GetValue("TmdbApiKey", Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? string.Empty);

    // Advanced & Logging
    public bool LogToFile => this.GetValueBoolean("LogToFile", true);

    public string FileLogLevel => this.GetValue("FileLogLevel", "Info");

    public bool DebugMode => this.GetValueBoolean("DebugMode", false);

    public int UiRefreshRateSec => this.GetValueInt("UiRefreshRateSec", 2);
}

public class ConfigSavedEvent : IEvent
{
}

public class ConfigFileSavedEvent : IEvent
{
}
