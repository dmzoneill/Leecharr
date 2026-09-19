// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(34)]
public class AddTorrentEventLogs : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Table("TorrentEventLogs")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().NotNullable()
            .WithColumn("Level").AsString().NotNullable()
            .WithColumn("Source").AsString().NotNullable()
            .WithColumn("Message").AsString().NotNullable()
            .WithColumn("Timestamp").AsDateTime().NotNullable();

        this.Create.Index("IX_TorrentEventLogs_TorrentId")
            .OnTable("TorrentEventLogs")
            .OnColumn("TorrentId");

        this.Create.Index("IX_TorrentEventLogs_Timestamp")
            .OnTable("TorrentEventLogs")
            .OnColumn("Timestamp");

        this.Create.Index("IX_TorrentEventLogs_TorrentId_Timestamp")
            .OnTable("TorrentEventLogs")
            .OnColumn("TorrentId").Ascending()
            .OnColumn("Timestamp").Ascending();
    }

    public override void Down()
    {
    }
}
