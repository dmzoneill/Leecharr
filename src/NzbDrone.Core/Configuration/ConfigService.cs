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

    // BitTorrent
    bool EnableDht { get; }
    bool EnablePex { get; }
    bool EnableLpd { get; }
    string EncryptionMode { get; }
    string BitTorrentUserAgent { get; }
    string PeerIdPrefix { get; }
    int AnnounceIntervalSeconds { get; }
    int MinAnnounceIntervalSeconds { get; }
    int ScrapeIntervalSeconds { get; }

    // Protocol Extensions
    bool ExtensionUtMetadata { get; }
    bool ExtensionUtPex { get; }
    bool ExtensionLtDontHave { get; }
    bool ExtensionFastExtension { get; }
    bool UtpEnabled { get; }
    bool TcpFallback { get; }
    int TransportConnectionTimeoutSeconds { get; }

    // Speed & Bandwidth
    int MaxUploadSpeedKbps { get; }
    int MaxDownloadSpeedKbps { get; }
    bool AlternativeSpeedEnabled { get; }
    int AltUploadSpeedKbps { get; }
    int AltDownloadSpeedKbps { get; }
    double GlobalSeedRatioLimit { get; }

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
    private volatile Dictionary<string, string> _cache;

    public ConfigService(IBasicRepository<ConfigModel> repository, IEventAggregator eventAggregator)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void SaveConfigDictionary(Dictionary<string, object> configValues)
    {
        if (configValues == null)
        {
            return;
        }

        lock (_cacheLock)
        {
            var all = _repository.All().ToList();

            foreach (var configValue in configValues)
            {
                var existing = all.FirstOrDefault(c =>
                    string.Equals(c.Key, configValue.Key, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    _repository.Insert(new ConfigModel { Key = configValue.Key, Value = configValue.Value?.ToString() ?? string.Empty });
                }
                else
                {
                    existing.Value = configValue.Value?.ToString() ?? string.Empty;
                    _repository.Update(existing);
                }
            }

            _cache = null;
        }

        _logger.Debug("Saved {0} config values", configValues.Count);
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
    public bool AutoStart => GetValueBoolean("AutoStart", true);
    public string ThemeStyle => GetValue("ThemeStyle", "dark");
    public string ColorScheme => GetValue("ColorScheme", "auto");
    public string DefaultCategory => GetValue("DefaultCategory", "");

    // Storage & Disk
    public string DownloadDir => GetValue("DownloadDir", "");
    public string IncompleteDownloadDir => GetValue("IncompleteDownloadDir", "");
    public int DiskWriteCacheSizeMb => GetValueInt("DiskWriteCacheSizeMb", 128);
    public string DiskPreAllocationMode => GetValue("DiskPreAllocationMode", "sparse");
    public int DiskFlushIntervalSeconds => GetValueInt("DiskFlushIntervalSeconds", 30);
    public int FastResumeIntervalMinutes => GetValueInt("FastResumeIntervalMinutes", 5);

    // Watch Folder
    public bool WatchFolderEnabled => GetValueBoolean("WatchFolderEnabled", false);
    public string WatchFolderPath => GetValue("WatchFolderPath", "");
    public int WatchFolderScanIntervalSeconds => GetValueInt("WatchFolderScanIntervalSeconds", 10);
    public bool WatchFolderAutoStartTorrents => GetValueBoolean("WatchFolderAutoStartTorrents", true);
    public bool WatchFolderDeleteAddedTorrents => GetValueBoolean("WatchFolderDeleteAddedTorrents", false);

    // Connection & Swarm
    public string BindInterface => GetValue("BindInterface", "");
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
    public string ProxyHost => GetValue("ProxyHost", "");
    public int ProxyPort => GetValueInt("ProxyPort", 8080);
    public bool ProxyAuthEnabled => GetValueBoolean("ProxyAuthEnabled", false);
    public string ProxyUsername => GetValue("ProxyUsername", "");
    public string ProxyPassword => GetValue("ProxyPassword", "");

    // BitTorrent
    public bool EnableDht => GetValueBoolean("EnableDht", true);
    public bool EnablePex => GetValueBoolean("EnablePex", true);
    public bool EnableLpd => GetValueBoolean("EnableLpd", true);
    public string EncryptionMode => GetValue("EncryptionMode", "preferEncrypted");
    public string BitTorrentUserAgent => GetValue("BitTorrentUserAgent", "Leecharr/1.0");
    public string PeerIdPrefix => GetValue("PeerIdPrefix", "-LC1000-");
    public int AnnounceIntervalSeconds => GetValueInt("AnnounceIntervalSeconds", 1800);
    public int MinAnnounceIntervalSeconds => GetValueInt("MinAnnounceIntervalSeconds", 300);
    public int ScrapeIntervalSeconds => GetValueInt("ScrapeIntervalSeconds", 900);

    // Protocol Extensions
    public bool ExtensionUtMetadata => GetValueBoolean("ExtensionUtMetadata", true);
    public bool ExtensionUtPex => GetValueBoolean("ExtensionUtPex", true);
    public bool ExtensionLtDontHave => GetValueBoolean("ExtensionLtDontHave", true);
    public bool ExtensionFastExtension => GetValueBoolean("ExtensionFastExtension", true);
    public bool UtpEnabled => GetValueBoolean("UtpEnabled", true);
    public bool TcpFallback => GetValueBoolean("TcpFallback", true);
    public int TransportConnectionTimeoutSeconds => GetValueInt("TransportConnectionTimeoutSeconds", 30);

    // Speed & Bandwidth
    public int MaxUploadSpeedKbps => GetValueInt("MaxUploadSpeedKbps", 0);
    public int MaxDownloadSpeedKbps => GetValueInt("MaxDownloadSpeedKbps", 0);
    public bool AlternativeSpeedEnabled => GetValueBoolean("AlternativeSpeedEnabled", false);
    public int AltUploadSpeedKbps => GetValueInt("AltUploadSpeedKbps", 500);
    public int AltDownloadSpeedKbps => GetValueInt("AltDownloadSpeedKbps", 2000);
    public double GlobalSeedRatioLimit => GetValueDouble("GlobalSeedRatioLimit", 0.0);

    // Media Enrichment
    public bool AutoEnrichEnabled => GetValueBoolean("AutoEnrichEnabled", true);
    public string MediaCachePath => GetValue("MediaCachePath", "");
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
