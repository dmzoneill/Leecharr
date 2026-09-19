// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(33)]
public class AddQueryPerformanceCompositeIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (this.Schema.Table("Torrents").Exists())
        {
            this.Create.Index("IX_Torrents_QueuePosition")
                .OnTable("Torrents")
                .OnColumn("QueuePosition");

            this.Create.Index("IX_Torrents_Status_Category")
                .OnTable("Torrents")
                .OnColumn("Status").Ascending()
                .OnColumn("Category").Ascending();
        }

        if (this.Schema.Table("TorrentFiles").Exists())
        {
            this.Create.Index("IX_TorrentFiles_TorrentId_Id")
                .OnTable("TorrentFiles")
                .OnColumn("TorrentId").Ascending()
                .OnColumn("Id").Ascending();
        }

        if (this.Schema.Table("DownloadHistory").Exists())
        {
            this.Create.Index("IX_DownloadHistory_Status_DateAdded")
                .OnTable("DownloadHistory")
                .OnColumn("Status").Ascending()
                .OnColumn("DateAdded").Ascending();

            this.Create.Index("IX_DownloadHistory_InfoHash_Id")
                .OnTable("DownloadHistory")
                .OnColumn("InfoHash").Ascending()
                .OnColumn("Id").Ascending();
        }
    }

    public override void Down()
    {
    }
}
