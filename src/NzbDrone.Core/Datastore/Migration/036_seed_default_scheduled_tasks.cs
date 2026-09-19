// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(36)]
public class SeedDefaultScheduledTasks : NzbDroneMigrationBase
{
    public override void Up()
    {
        var tasks = new[]
        {
            ("WatchFolderScanTask", 1),
            ("RssSyncTask", 15),
            ("VpnKillSwitchCheckTask", 1),
            ("BackupTask", 1440),
            ("ProwlarrSyncTask", 60),
            ("SessionCleanupTask", 15),
            ("BlocklistUpdateTask", 1440),
            ("GeoIpUpdateTask", 43200),
            ("DownloadHistoryCleanupTask", 1440),
        };

        foreach (var (typeName, interval) in tasks)
        {
            this.Execute.Sql(
                $"INSERT INTO \"ScheduledTasks\" (\"TypeName\", \"Interval\", \"LastExecution\") " +
                $"SELECT '{typeName}', {interval}, CURRENT_TIMESTAMP " +
                $"WHERE NOT EXISTS (SELECT 1 FROM \"ScheduledTasks\" WHERE \"TypeName\" = '{typeName}');");
        }
    }

    public override void Down()
    {
    }
}
