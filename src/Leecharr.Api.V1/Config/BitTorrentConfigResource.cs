// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class BitTorrentConfigResource : RestResource
{
    // Active Engine
    public string ActiveTorrentEngine { get; set; }

    // BitTorrent Core
    public bool EnableDht { get; set; }

    public bool EnablePex { get; set; }

    public bool EnableLpd { get; set; }

    public string EncryptionMode { get; set; }

    public string BitTorrentUserAgent { get; set; }

    public string PeerIdPrefix { get; set; }

    public int AnnounceIntervalSeconds { get; set; }

    public int MinAnnounceIntervalSeconds { get; set; }

    public int ScrapeIntervalSeconds { get; set; }

    // Storage & Incomplete Staging & Preallocation
    public string IncompleteDownloadDir { get; set; }

    public bool EnableIncompleteDir { get; set; }

    public string PreallocationMode { get; set; }

    public bool RenamePartialFiles { get; set; }

    public string Umask { get; set; }

    // Queue & Concurrency Management
    public int DownloadQueueSize { get; set; }

    public int SeedQueueSize { get; set; }

    public bool QueueStalledEnabled { get; set; }

    public int QueueStalledMinutes { get; set; }

    public int IdleSeedingLimitMinutes { get; set; }

    // Network & Sockets Extended
    public string NetworkInterfaceBinding { get; set; }

    public int MaxConnectionsPerIp { get; set; }

    public int MaximumHalfOpenConnections { get; set; }

    public bool AnonymousMode { get; set; }

    public bool ForceProxy { get; set; }

    public int PeerDscp { get; set; }

    public bool PeerPortRandomOnStart { get; set; }

    public int PeerPortRandomLow { get; set; }

    public int PeerPortRandomHigh { get; set; }

    // MonoTorrent Specific
    public int DiskCacheBytes { get; set; }

    public string DiskCachePolicy { get; set; }

    public string FastResumeMode { get; set; }

    public int AutoSaveFastResumeIntervalSeconds { get; set; }

    public bool AutoSaveLoadMagnetMetadata { get; set; }

    public bool AutoSaveLoadDhtCache { get; set; }

    public string PiecePickerStrategy { get; set; }

    public bool EndGamePickerEnabled { get; set; }

    public int StaleRequestTimeoutSeconds { get; set; }

    public int WebSeedDelaySeconds { get; set; }

    public int MaximumDiskReadRateKbps { get; set; }

    public int MaximumDiskWriteRateKbps { get; set; }

    // libtorrent Specific
    public int HashingThreads { get; set; }

    public int AioThreads { get; set; }

    public string DiskIoWriteMode { get; set; }

    public string DiskIoReadMode { get; set; }

    public int FilePoolSize { get; set; }

    public string ChokingAlgorithm { get; set; }

    public string SeedChokingAlgorithm { get; set; }

    public string MixedModeAlgorithm { get; set; }

    public string AlertMask { get; set; }

    // Transmission Specific
    public string ScriptTorrentDoneFilename { get; set; }

    public string ScriptTorrentAddedFilename { get; set; }

    public string ScriptTorrentDoneSeedingFilename { get; set; }

    public bool PrefetchEnabled { get; set; }

    public bool ScrapePausedTorrentsEnabled { get; set; }

    public bool RpcWhitelistEnabled { get; set; }

    public string RpcWhitelist { get; set; }

    // Swarm & Scripts
    public string OnDownloadCompleteScript { get; set; }

    public string OnSeedGoalReachedScript { get; set; }

    public string DefaultTrackers { get; set; }

    public string DhtBootstrapNodes { get; set; }
}

public static class BitTorrentConfigResourceMapper
{
    public static BitTorrentConfigResource ToResource(IConfigService model)
    {
        return new BitTorrentConfigResource
        {
            ActiveTorrentEngine = model.ActiveTorrentEngine,
            EnableDht = model.EnableDht,
            EnablePex = model.EnablePex,
            EnableLpd = model.EnableLpd,
            EncryptionMode = model.EncryptionMode,
            BitTorrentUserAgent = model.BitTorrentUserAgent,
            PeerIdPrefix = model.PeerIdPrefix,
            AnnounceIntervalSeconds = model.AnnounceIntervalSeconds,
            MinAnnounceIntervalSeconds = model.MinAnnounceIntervalSeconds,
            ScrapeIntervalSeconds = model.ScrapeIntervalSeconds,

            IncompleteDownloadDir = model.IncompleteDownloadDir,
            EnableIncompleteDir = model.EnableIncompleteDir,
            PreallocationMode = model.PreallocationMode,
            RenamePartialFiles = model.RenamePartialFiles,
            Umask = model.Umask,

            DownloadQueueSize = model.DownloadQueueSize,
            SeedQueueSize = model.SeedQueueSize,
            QueueStalledEnabled = model.QueueStalledEnabled,
            QueueStalledMinutes = model.QueueStalledMinutes,
            IdleSeedingLimitMinutes = model.IdleSeedingLimitMinutes,

            NetworkInterfaceBinding = model.NetworkInterfaceBinding,
            MaxConnectionsPerIp = model.MaxConnectionsPerIp,
            MaximumHalfOpenConnections = model.MaximumHalfOpenConnections,
            AnonymousMode = model.AnonymousMode,
            ForceProxy = model.ForceProxy,
            PeerDscp = model.PeerDscp,
            PeerPortRandomOnStart = model.PeerPortRandomOnStart,
            PeerPortRandomLow = model.PeerPortRandomLow,
            PeerPortRandomHigh = model.PeerPortRandomHigh,

            DiskCacheBytes = model.DiskCacheBytes,
            DiskCachePolicy = model.DiskCachePolicy,
            FastResumeMode = model.FastResumeMode,
            AutoSaveFastResumeIntervalSeconds = model.AutoSaveFastResumeIntervalSeconds,
            AutoSaveLoadMagnetMetadata = model.AutoSaveLoadMagnetMetadata,
            AutoSaveLoadDhtCache = model.AutoSaveLoadDhtCache,
            PiecePickerStrategy = model.PiecePickerStrategy,
            EndGamePickerEnabled = model.EndGamePickerEnabled,
            StaleRequestTimeoutSeconds = model.StaleRequestTimeoutSeconds,
            WebSeedDelaySeconds = model.WebSeedDelaySeconds,
            MaximumDiskReadRateKbps = model.MaximumDiskReadRateKbps,
            MaximumDiskWriteRateKbps = model.MaximumDiskWriteRateKbps,

            HashingThreads = model.HashingThreads,
            AioThreads = model.AioThreads,
            DiskIoWriteMode = model.DiskIoWriteMode,
            DiskIoReadMode = model.DiskIoReadMode,
            FilePoolSize = model.FilePoolSize,
            ChokingAlgorithm = model.ChokingAlgorithm,
            SeedChokingAlgorithm = model.SeedChokingAlgorithm,
            MixedModeAlgorithm = model.MixedModeAlgorithm,
            AlertMask = model.AlertMask,

            ScriptTorrentDoneFilename = model.ScriptTorrentDoneFilename,
            ScriptTorrentAddedFilename = model.ScriptTorrentAddedFilename,
            ScriptTorrentDoneSeedingFilename = model.ScriptTorrentDoneSeedingFilename,
            PrefetchEnabled = model.PrefetchEnabled,
            ScrapePausedTorrentsEnabled = model.ScrapePausedTorrentsEnabled,
            RpcWhitelistEnabled = model.RpcWhitelistEnabled,
            RpcWhitelist = model.RpcWhitelist,

            OnDownloadCompleteScript = model.OnDownloadCompleteScript,
            OnSeedGoalReachedScript = model.OnSeedGoalReachedScript,
            DefaultTrackers = model.DefaultTrackers,
            DhtBootstrapNodes = model.DhtBootstrapNodes,
        };
    }
}
