// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(50)]
public class DownloadHistoryUniqueInfoHash : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("DownloadHistory").Exists())
        {
            return;
        }

        this.Execute.Sql(
            """
            UPDATE "DownloadHistory"
            SET "InfoHash" = LOWER(TRIM("InfoHash"))
            WHERE "InfoHash" IS NOT NULL;
            """);

        this.Execute.Sql(
            """
            DELETE FROM "DownloadHistory"
            WHERE "InfoHash" != ''
              AND "Id" NOT IN (
                SELECT MAX("Id") FROM "DownloadHistory" WHERE "InfoHash" != '' GROUP BY "InfoHash"
              );
            """);

        this.Execute.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_DownloadHistory_InfoHash_Unique"
            ON "DownloadHistory" ("InfoHash")
            WHERE "InfoHash" != '';
            """);
    }

    public override void Down()
    {
    }
}
