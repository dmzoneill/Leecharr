// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(42)]
public class IdentityProviderCaseInsensitiveProviderId : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("IdentityProviders").Exists())
        {
            return;
        }

        this.Execute.Sql(
            """
            DELETE FROM "IdentityProviders"
            WHERE "Id" NOT IN (
                SELECT MIN("Id") FROM "IdentityProviders" GROUP BY LOWER("ProviderId")
            );
            """);

        this.Execute.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_IdentityProviders_ProviderId_Lower"
            ON "IdentityProviders" (LOWER("ProviderId"));
            """);
    }

    public override void Down()
    {
    }
}
