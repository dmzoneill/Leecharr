#nullable enable
// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

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
    private readonly Logger logger;

    public CoreSystemCommandHandler(
        IMainDatabase? mainDatabase = null,
        IDownloadEngine? downloadEngine = null,
        IDiskProvider? diskProvider = null,
        IAppFolderInfo? appFolderInfo = null,
        IConfigService? configService = null)
    {
        this.mainDatabase = mainDatabase;
        this.downloadEngine = downloadEngine;
        this.diskProvider = diskProvider;
        this.appFolderInfo = appFolderInfo;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    // 1. RescanTorrentsCommand
    public void Execute(RescanTorrentsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RescanTorrentsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RescanTorrentsCommand: TorrentId={0}, ForceRecheck={1}", command.TorrentId, command.ForceRecheck);
        return Task.CompletedTask;
    }

    // 2. PurgeCompletedTorrentsCommand
    public void Execute(PurgeCompletedTorrentsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(PurgeCompletedTorrentsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing PurgeCompletedTorrentsCommand: RetentionDays={0}, RemoveData={1}", command.RetentionDays, command.RemoveData);
        return Task.CompletedTask;
    }

    // 3. ForceGarbageCollectionCommand
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

        GC.Collect(command.Generation, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        return Task.CompletedTask;
    }

    // 4. VacuumDatabaseCommand
    public void Execute(VacuumDatabaseCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(VacuumDatabaseCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing VacuumDatabaseCommand: AnalyzeAfterVacuum={0}", command.AnalyzeAfterVacuum);
        if (this.mainDatabase != null && this.mainDatabase.DatabaseType == DatabaseType.SQLite)
        {
            using var conn = this.mainDatabase.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
            cmd.ExecuteNonQuery();

            if (command.AnalyzeAfterVacuum)
            {
                cmd.CommandText = "PRAGMA optimize;";
                cmd.ExecuteNonQuery();
            }
        }

        return Task.CompletedTask;
    }

    // 5. UpdateTrackersCommand
    public void Execute(UpdateTrackersCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(UpdateTrackersCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing UpdateTrackersCommand: TorrentId={0}, AnnounceImmediately={1}", command.TorrentId, command.AnnounceImmediately);
        return Task.CompletedTask;
    }

    // 6. TestNotificationEndpointsCommand
    public void Execute(TestNotificationEndpointsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(TestNotificationEndpointsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing TestNotificationEndpointsCommand: NotificationId={0}", command.NotificationId);
        return Task.CompletedTask;
    }

    // 7. RecheckNetworkInterfacesCommand
    public void Execute(RecheckNetworkInterfacesCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RecheckNetworkInterfacesCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RecheckNetworkInterfacesCommand: ForceRebind={0}", command.ForceRebind);
        return Task.CompletedTask;
    }

    // 8. SyncSpeedLimitsCommand
    public void Execute(SyncSpeedLimitsCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(SyncSpeedLimitsCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing SyncSpeedLimitsCommand: ForceApplyCurrentSchedule={0}", command.ForceApplyCurrentSchedule);
        return Task.CompletedTask;
    }

    // 9. CheckDiskSpaceCommand
    public void Execute(CheckDiskSpaceCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(CheckDiskSpaceCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing CheckDiskSpaceCommand: MinFreeThreshold={0} bytes", command.MinimumFreeBytesThreshold);
        return Task.CompletedTask;
    }

    // 10. RotateLogFilesCommand
    public void Execute(RotateLogFilesCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RotateLogFilesCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RotateLogFilesCommand: RetentionDays={0}", command.RetentionDays);
        return Task.CompletedTask;
    }

    // 11. ClearDhtCacheCommand
    public void Execute(ClearDhtCacheCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(ClearDhtCacheCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ClearDhtCacheCommand: BootstrapNodes={0}", command.BootstrapNodes);
        return Task.CompletedTask;
    }

    // 12. ClearWebSessionCacheCommand
    public void Execute(ClearWebSessionCacheCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(ClearWebSessionCacheCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ClearWebSessionCacheCommand: InvalidateAll={0}", command.InvalidateAll);
        return Task.CompletedTask;
    }

    // 13. RebuildSearchIndexCommand
    public void Execute(RebuildSearchIndexCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RebuildSearchIndexCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RebuildSearchIndexCommand: FullReindex={0}", command.FullReindex);
        return Task.CompletedTask;
    }

    // 14. ExportDiagnosticsBundleCommand
    public void Execute(ExportDiagnosticsBundleCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(ExportDiagnosticsBundleCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing ExportDiagnosticsBundleCommand: IncludeSchema={0}, Anonymize={1}", command.IncludeDatabaseSchema, command.AnonymizeSecrets);
        return Task.CompletedTask;
    }

    // 15. RefreshAllMetadataCommand
    public void Execute(RefreshAllMetadataCommand command)
    {
        this.ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(RefreshAllMetadataCommand command, CancellationToken cancellationToken = default)
    {
        this.logger.Info("Executing RefreshAllMetadataCommand: TorrentId={0}, Overwrite={1}", command.TorrentId, command.OverwriteExisting);
        return Task.CompletedTask;
    }
}
