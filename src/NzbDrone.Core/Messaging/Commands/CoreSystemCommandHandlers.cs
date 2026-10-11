// Copyright (c) FeedItOut. All rights reserved.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers.Search;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Messaging.Commands;

public interface ICoreSystemCommandHandler :
    IExecute<RescanTorrentsCommand>, IExecuteAsync<RescanTorrentsCommand>,
    IExecute<PurgeCompletedTorrentsCommand>, IExecuteAsync<PurgeCompletedTorrentsCommand>,
    IExecute<ForceGarbageCollectionCommand>, IExecuteAsync<ForceGarbageCollectionCommand>,
    IExecute<VacuumDatabaseCommand>, IExecuteAsync<VacuumDatabaseCommand>,
    IExecute<UpdateTrackersCommand>, IExecuteAsync<UpdateTrackersCommand>,
    IExecute<TestNotificationEndpointsCommand>, IExecuteAsync<TestNotificationEndpointsCommand>,
    IExecute<RecheckNetworkInterfacesCommand>, IExecuteAsync<RecheckNetworkInterfacesCommand>,
    IExecute<SyncSpeedLimitsCommand>, IExecuteAsync<SyncSpeedLimitsCommand>,
    IExecute<CheckDiskSpaceCommand>, IExecuteAsync<CheckDiskSpaceCommand>,
    IExecute<RotateLogFilesCommand>, IExecuteAsync<RotateLogFilesCommand>,
    IExecute<ClearDhtCacheCommand>, IExecuteAsync<ClearDhtCacheCommand>,
    IExecute<ClearWebSessionCacheCommand>, IExecuteAsync<ClearWebSessionCacheCommand>,
    IExecute<RebuildSearchIndexCommand>, IExecuteAsync<RebuildSearchIndexCommand>,
    IExecute<ExportDiagnosticsBundleCommand>, IExecuteAsync<ExportDiagnosticsBundleCommand>,
    IExecute<RefreshAllMetadataCommand>, IExecuteAsync<RefreshAllMetadataCommand>
{
}

public class CoreSystemCommandHandler : ICoreSystemCommandHandler
{
    private readonly IMainDatabase? mainDatabase;
    private readonly IDownloadEngine? downloadEngine;
    private readonly IDiskProvider? diskProvider;
    private readonly IAppFolderInfo? appFolderInfo;
    private readonly IConfigService? configService;
    private readonly ITorrentService? torrentService;
    private readonly ISpeedSchedulerService? speedSchedulerService;
    private readonly INetworkSecurityService? networkSecurityService;
    private readonly INotificationEndpointTester? notificationEndpointTester;
    private readonly IUserSessionCache? userSessionCache;
    private readonly IUserSessionRepository? userSessionRepository;
    private readonly IQBittorrentSearchService? searchService;
    private readonly IBackupService? backupService;
    private readonly IMediaEnrichmentService? mediaEnrichmentService;
    private readonly Logger logger;

    public CoreSystemCommandHandler(
        IMainDatabase? mainDatabase = null,
        IDownloadEngine? downloadEngine = null,
        IDiskProvider? diskProvider = null,
        IAppFolderInfo? appFolderInfo = null,
        IConfigService? configService = null,
        ITorrentService? torrentService = null,
        ISpeedSchedulerService? speedSchedulerService = null,
        INetworkSecurityService? networkSecurityService = null,
        INotificationEndpointTester? notificationEndpointTester = null,
        IUserSessionCache? userSessionCache = null,
        IUserSessionRepository? userSessionRepository = null,
        IQBittorrentSearchService? searchService = null,
        IBackupService? backupService = null,
        IMediaEnrichmentService? mediaEnrichmentService = null)
    {
        this.mainDatabase = mainDatabase;
        this.downloadEngine = downloadEngine;
        this.diskProvider = diskProvider;
        this.appFolderInfo = appFolderInfo;
        this.configService = configService;
        this.torrentService = torrentService;
        this.speedSchedulerService = speedSchedulerService;
        this.networkSecurityService = networkSecurityService;
        this.notificationEndpointTester = notificationEndpointTester;
        this.userSessionCache = userSessionCache;
        this.userSessionRepository = userSessionRepository;
        this.searchService = searchService;
        this.backupService = backupService;
        this.mediaEnrichmentService = mediaEnrichmentService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(RescanTorrentsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(RescanTorrentsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RescanTorrentsCommand: TorrentId={0}, ForceRecheck={1}", command.TorrentId, command.ForceRecheck);
        if (this.torrentService == null)
        {
            return;
        }

        var torrents = this.GetTargetTorrents(command.TorrentId);
        foreach (var torrent in torrents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command.ForceRecheck)
            {
                await this.torrentService.ForceRecheckAsync(torrent.Id).ConfigureAwait(false);
            }
            else
            {
                await this.torrentService.ForceAnnounceAsync(torrent.Id).ConfigureAwait(false);
            }
        }
    }

    public void Execute(PurgeCompletedTorrentsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(PurgeCompletedTorrentsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing PurgeCompletedTorrentsCommand: RetentionDays={0}, RemoveData={1}", command.RetentionDays, command.RemoveData);
        if (this.torrentService == null)
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(command.RetentionDays, 0));
        var toPurge = this.torrentService.GetAll()
            .Where(t => t.Status is TorrentStatus.Completed or TorrentStatus.Seeding)
            .Where(t => t.DateCompleted == null || t.DateCompleted < cutoff)
            .ToList();

        foreach (var torrent in toPurge)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this.torrentService.DeleteAsync(torrent.Id, command.RemoveData).ConfigureAwait(false);
        }

        this.logger.Info("Purged {0} completed torrent(s)", toPurge.Count);
    }

    public void Execute(ForceGarbageCollectionCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(ForceGarbageCollectionCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ForceGarbageCollectionCommand: Gen={0}, CompactLOH={1}", command.Generation, command.CompactLargeObjectHeap);
        if (command.CompactLargeObjectHeap)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        }

        GC.Collect(command.Generation, GCCollectionMode.Forced, true, command.CompactLargeObjectHeap);
        GC.WaitForPendingFinalizers();
        return Task.CompletedTask;
    }

    public void Execute(VacuumDatabaseCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(VacuumDatabaseCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing VacuumDatabaseCommand: AnalyzeAfterVacuum={0}", command.AnalyzeAfterVacuum);
        if (this.mainDatabase == null)
        {
            return;
        }

        using var conn = this.mainDatabase.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition("VACUUM;", cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (command.AnalyzeAfterVacuum)
        {
            var analyzeSql = this.mainDatabase.DatabaseType == DatabaseType.SQLite
                ? "PRAGMA optimize;"
                : "ANALYZE;";
            await conn.ExecuteAsync(new CommandDefinition(analyzeSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public void Execute(UpdateTrackersCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(UpdateTrackersCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing UpdateTrackersCommand: TorrentId={0}, AnnounceImmediately={1}", command.TorrentId, command.AnnounceImmediately);
        if (this.torrentService == null)
        {
            return;
        }

        var torrents = this.GetTargetTorrents(command.TorrentId);
        foreach (var torrent in torrents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command.AnnounceImmediately)
            {
                await this.torrentService.ForceAnnounceAsync(torrent.Id).ConfigureAwait(false);
            }
        }
    }

    public void Execute(TestNotificationEndpointsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(TestNotificationEndpointsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing TestNotificationEndpointsCommand: NotificationId={0}", command.NotificationId);
        if (this.notificationEndpointTester == null)
        {
            return;
        }

        var tested = await this.notificationEndpointTester
            .TestEndpointsAsync(command.NotificationId, command.SendTestPayload, cancellationToken)
            .ConfigureAwait(false);
        this.logger.Info("Notification endpoint tests completed: {0} succeeded", tested);
    }

    public void Execute(RecheckNetworkInterfacesCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(RecheckNetworkInterfacesCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RecheckNetworkInterfacesCommand: ForceRebind={0}", command.ForceRebind);
        if (this.networkSecurityService != null)
        {
            var interfaces = this.networkSecurityService.GetAvailableNetworkInterfaces();
            this.logger.Info("Detected {0} active network interface(s)", interfaces.Count());
            this.networkSecurityService.CheckVpnKillSwitch();
        }

        if (this.downloadEngine != null)
        {
            await this.downloadEngine.RecheckNetworkInterfacesAsync(command.ForceRebind, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Execute(SyncSpeedLimitsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(SyncSpeedLimitsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing SyncSpeedLimitsCommand: ForceApplyCurrentSchedule={0}", command.ForceApplyCurrentSchedule);
        if (this.speedSchedulerService == null)
        {
            return;
        }

        if (command.ForceApplyCurrentSchedule)
        {
            await this.speedSchedulerService.ApplyCurrentLimitsAsync().ConfigureAwait(false);
        }
    }

    public void Execute(CheckDiskSpaceCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(CheckDiskSpaceCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing CheckDiskSpaceCommand: MinFreeThreshold={0} bytes", command.MinimumFreeBytesThreshold);
        this.downloadEngine?.CheckDiskSpaceHealth();
        return Task.CompletedTask;
    }

    public void Execute(RotateLogFilesCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RotateLogFilesCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RotateLogFilesCommand: RetentionDays={0}", command.RetentionDays);
        if (this.appFolderInfo == null)
        {
            return Task.CompletedTask;
        }

        var logDir = Path.Combine(this.appFolderInfo.AppDataFolder, "logs");
        if (!Directory.Exists(logDir))
        {
            return Task.CompletedTask;
        }

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(command.RetentionDays, 0));
        foreach (var file in Directory.GetFiles(logDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to delete old log file {0}", file);
            }
        }

        return Task.CompletedTask;
    }

    public void Execute(ClearDhtCacheCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(ClearDhtCacheCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ClearDhtCacheCommand: BootstrapNodes={0}", command.BootstrapNodes);
        if (this.downloadEngine != null)
        {
            await this.downloadEngine.ClearDhtCacheAsync(command.BootstrapNodes, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Execute(ClearWebSessionCacheCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(ClearWebSessionCacheCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ClearWebSessionCacheCommand: InvalidateAll={0}", command.InvalidateAll);
        if (command.InvalidateAll)
        {
            this.userSessionCache?.ClearCache();
            if (this.userSessionRepository != null)
            {
                foreach (var session in this.userSessionRepository.All().ToList())
                {
                    this.userSessionRepository.Delete(session.Id);
                }
            }
        }
        else
        {
            this.userSessionCache?.PruneExpired();
            this.userSessionRepository?.DeleteExpiredSessions();
        }

        return Task.CompletedTask;
    }

    public void Execute(RebuildSearchIndexCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RebuildSearchIndexCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RebuildSearchIndexCommand: FullReindex={0}", command.FullReindex);
        if (this.searchService == null)
        {
            return Task.CompletedTask;
        }

        foreach (var status in this.searchService.GetAllStatuses())
        {
            this.searchService.StopSearch(status.Id);
            this.searchService.DeleteSearch(status.Id);
        }

        this.searchService.PruneExpiredJobs(TimeSpan.Zero);
        return Task.CompletedTask;
    }

    public void Execute(ExportDiagnosticsBundleCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(ExportDiagnosticsBundleCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ExportDiagnosticsBundleCommand: IncludeSchema={0}, Anonymize={1}", command.IncludeDatabaseSchema, command.AnonymizeSecrets);
        if (this.backupService == null)
        {
            return Task.CompletedTask;
        }

        var result = this.backupService.CreateBackup("Diagnostics");
        this.logger.Info("Diagnostics bundle created at {0} ({1} bytes)", result.Path, result.Size);
        return Task.CompletedTask;
    }

    public void Execute(RefreshAllMetadataCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(RefreshAllMetadataCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RefreshAllMetadataCommand: TorrentId={0}, Overwrite={1}", command.TorrentId, command.OverwriteExisting);
        if (this.torrentService == null || this.mediaEnrichmentService == null)
        {
            return;
        }

        var torrents = this.GetTargetTorrents(command.TorrentId);
        foreach (var torrent in torrents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command.OverwriteExisting)
            {
                this.mediaEnrichmentService.DeleteMetadata(torrent.Id);
            }

            await this.mediaEnrichmentService.EnrichTorrentAsync(torrent).ConfigureAwait(false);
        }
    }

    private IEnumerable<Torrent> GetTargetTorrents(int? torrentId)
    {
        if (this.torrentService == null)
        {
            return Array.Empty<Torrent>();
        }

        if (torrentId.HasValue)
        {
            var torrent = this.torrentService.Get(torrentId.Value);
            return torrent != null ? new[] { torrent } : Array.Empty<Torrent>();
        }

        return this.torrentService.GetAll();
    }
}
