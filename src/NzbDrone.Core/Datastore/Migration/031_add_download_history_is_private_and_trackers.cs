// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(31)]
public class AddDownloadHistoryIsPrivateAndTrackers : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (this.Schema.Table("DownloadHistory").Exists())
        {
            if (!this.Schema.Table("DownloadHistory").Column("IsPrivate").Exists())
            {
                this.Alter.Table("DownloadHistory")
                    .AddColumn("IsPrivate").AsBoolean().NotNullable().WithDefaultValue(false);

                this.BackfillDownloadHistoryIsPrivate();
            }

            if (!this.Schema.Table("DownloadHistory").Column("Trackers").Exists())
            {
                this.Alter.Table("DownloadHistory")
                    .AddColumn("Trackers").AsString().Nullable();
            }
        }
    }

    private void BackfillDownloadHistoryIsPrivate()
    {
        if (!this.Schema.Table("Torrents").Exists())
        {
            return;
        }

        // Copy private status from live torrent rows instead of leaving the migration default (public).
        this.Execute.Sql(
            @"UPDATE ""DownloadHistory""
              SET ""IsPrivate"" = 1
              WHERE ""IsPrivate"" = 0
                AND EXISTS (
                  SELECT 1 FROM ""Torrents"" t
                  WHERE t.""IsPrivate"" = 1
                    AND (t.""Id"" = ""DownloadHistory"".""TorrentId""
                         OR t.""InfoHash"" = ""DownloadHistory"".""InfoHash""));");

        this.Execute.Sql(
            @"UPDATE ""DownloadHistory""
              SET ""IsPrivate"" = 1
              WHERE ""IsPrivate"" = 0
                AND ""Source"" = 'Private Tracker';");
    }

    public override void Down()
    {
    }
}
