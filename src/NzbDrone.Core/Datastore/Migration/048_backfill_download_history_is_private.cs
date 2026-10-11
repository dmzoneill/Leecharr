// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(48)]
public class BackfillDownloadHistoryIsPrivate : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("DownloadHistory").Exists()
            || !this.Schema.Table("DownloadHistory").Column("IsPrivate").Exists()
            || !this.Schema.Table("Torrents").Exists())
        {
            return;
        }

        // Migration 31 added IsPrivate with default false, which rewrote every existing history row to public.
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
