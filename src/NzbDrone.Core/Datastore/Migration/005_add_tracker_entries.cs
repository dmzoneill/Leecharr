// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(5)]
public class AddTrackerEntries : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Table("TrackerEntries")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().NotNullable().ForeignKey("Torrents", "Id")
            .WithColumn("Url").AsString().NotNullable()
            .WithColumn("Tier").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Seeders").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Leechers").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Downloaded").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalAnnounces").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("SuccessfulAnnounces").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ConsecutiveFailures").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LastResponseTime").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("AnnounceInterval").AsInt32().NotNullable().WithDefaultValue(1800)
            .WithColumn("LastAnnounce").AsDateTime().Nullable()
            .WithColumn("NextAnnounce").AsDateTime().Nullable()
            .WithColumn("ErrorMessage").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
