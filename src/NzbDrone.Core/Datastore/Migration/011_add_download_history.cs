// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(11)]
public class AddDownloadHistory : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Table("DownloadHistory")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().Nullable()
            .WithColumn("Title").AsString().NotNullable()
            .WithColumn("InfoHash").AsString().NotNullable()
            .WithColumn("TotalSize").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("DateAdded").AsDateTime().NotNullable()
            .WithColumn("DateCompleted").AsDateTime().Nullable()
            .WithColumn("DateRemoved").AsDateTime().Nullable()
            .WithColumn("Uploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Downloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Ratio").AsDouble().NotNullable().WithDefaultValue(0.0)
            .WithColumn("SeedingTime").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("PrimaryTracker").AsString().Nullable()
            .WithColumn("IndexerName").AsString().Nullable()
            .WithColumn("Source").AsString().Nullable()
            .WithColumn("MagnetUrl").AsString().Nullable()
            .WithColumn("DownloadUrl").AsString().Nullable()
            .WithColumn("Status").AsString().NotNullable().WithDefaultValue("Active")
            .WithColumn("RemovalReason").AsString().Nullable()
            .WithColumn("DataJson").AsString().Nullable();

        this.Create.Index("IX_DownloadHistory_InfoHash")
            .OnTable("DownloadHistory")
            .OnColumn("InfoHash");

        this.Create.Index("IX_DownloadHistory_DateAdded")
            .OnTable("DownloadHistory")
            .OnColumn("DateAdded");

        this.Create.Index("IX_DownloadHistory_DateRemoved")
            .OnTable("DownloadHistory")
            .OnColumn("DateRemoved");

        this.Create.Index("IX_DownloadHistory_Status")
            .OnTable("DownloadHistory")
            .OnColumn("Status");
    }

    public override void Down()
    {
    }
}
