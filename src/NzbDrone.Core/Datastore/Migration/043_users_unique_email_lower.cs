// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(43)]
public class UsersUniqueEmailLower : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("Users").Exists())
        {
            return;
        }

        this.Execute.Sql(
            """
            DELETE FROM "Users"
            WHERE "Email" IS NOT NULL
              AND "Id" NOT IN (
                SELECT MIN("Id") FROM "Users" WHERE "Email" IS NOT NULL GROUP BY LOWER("Email")
              );
            """);

        this.Execute.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email_Lower"
            ON "Users" (LOWER("Email"))
            WHERE "Email" IS NOT NULL;
            """);
    }

    public override void Down()
    {
    }
}
