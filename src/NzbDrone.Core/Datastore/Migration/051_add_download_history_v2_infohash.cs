// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(51)]
public class AddDownloadHistoryV2InfoHash : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("DownloadHistory").Exists())
        {
            return;
        }

        if (!this.Schema.Table("DownloadHistory").Column("V2InfoHash").Exists())
        {
            this.Alter.Table("DownloadHistory")
                .AddColumn("V2InfoHash").AsString().Nullable();

            this.Create.Index("IX_DownloadHistory_V2InfoHash")
                .OnTable("DownloadHistory")
                .OnColumn("V2InfoHash");
        }

        if (!this.Schema.Table("Torrents").Exists())
        {
            return;
        }

        this.Execute.Sql(
            @"UPDATE ""DownloadHistory""
              SET ""V2InfoHash"" = (
                SELECT LOWER(TRIM(t.""V2InfoHash""))
                FROM ""Torrents"" t
                WHERE t.""V2InfoHash"" IS NOT NULL
                  AND t.""V2InfoHash"" != ''
                  AND (t.""Id"" = ""DownloadHistory"".""TorrentId""
                       OR t.""InfoHash"" = ""DownloadHistory"".""InfoHash""
                       OR t.""V2InfoHash"" = ""DownloadHistory"".""InfoHash"")
                LIMIT 1)
              WHERE (""V2InfoHash"" IS NULL OR ""V2InfoHash"" = '')
                AND EXISTS (
                  SELECT 1 FROM ""Torrents"" t
                  WHERE t.""V2InfoHash"" IS NOT NULL
                    AND t.""V2InfoHash"" != ''
                    AND (t.""Id"" = ""DownloadHistory"".""TorrentId""
                         OR t.""InfoHash"" = ""DownloadHistory"".""InfoHash""
                         OR t.""V2InfoHash"" = ""DownloadHistory"".""InfoHash""));");
    }

    public override void Down()
    {
    }
}
