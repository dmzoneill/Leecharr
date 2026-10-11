// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(52)]
public class NetworkSettingsSingleton : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("NetworkSettings").Exists())
        {
            return;
        }

        this.Execute.Sql(
            """
            DELETE FROM "NetworkSettings"
            WHERE "Id" NOT IN (
                SELECT MAX("Id") FROM "NetworkSettings"
            );
            """);

        this.Execute.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_NetworkSettings_Singleton"
            ON "NetworkSettings" ((1));
            """);
    }

    public override void Down()
    {
    }
}
