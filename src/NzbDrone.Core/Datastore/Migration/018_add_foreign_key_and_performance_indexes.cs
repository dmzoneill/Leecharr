// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(18)]
public class AddForeignKeyAndPerformanceIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Index("IX_TorrentFiles_TorrentId")
            .OnTable("TorrentFiles")
            .OnColumn("TorrentId");

        this.Create.Index("IX_TrackerEntries_TorrentId")
            .OnTable("TrackerEntries")
            .OnColumn("TorrentId");

        this.Create.Index("IX_Torrents_Category")
            .OnTable("Torrents")
            .OnColumn("Category");

        this.Create.Index("IX_Torrents_Status")
            .OnTable("Torrents")
            .OnColumn("Status");

        this.Create.Index("IX_UserSessions_UserId_Expiry")
            .OnTable("UserSessions")
            .OnColumn("UserId").Ascending()
            .OnColumn("Expiry").Ascending();
    }

    public override void Down()
    {
    }
}
