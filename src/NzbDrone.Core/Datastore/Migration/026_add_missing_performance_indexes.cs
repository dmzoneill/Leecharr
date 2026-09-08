// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(26)]
public class AddMissingPerformanceIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Index("IX_DownloadHistory_TorrentId")
            .OnTable("DownloadHistory")
            .OnColumn("TorrentId");

        this.Create.Index("IX_Commands_Status_QueuedAt")
            .OnTable("Commands")
            .OnColumn("Status").Ascending()
            .OnColumn("QueuedAt").Ascending();

        this.Create.Index("IX_Commands_Status_EndedAt")
            .OnTable("Commands")
            .OnColumn("Status").Ascending()
            .OnColumn("EndedAt").Ascending();

        this.Create.Index("IX_TrackerBoostTrackers_Enabled_Status_LatencyMs")
            .OnTable("TrackerBoostTrackers")
            .OnColumn("Enabled").Ascending()
            .OnColumn("Status").Ascending()
            .OnColumn("LatencyMs").Ascending();
    }

    public override void Down()
    {
    }
}
