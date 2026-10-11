// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(47)]
public class BackfillArrConnectionWebhookAndAutoAddFalse : NzbDroneMigrationBase
{
    public override void Up()
    {
        // Migration 38 originally defaulted both flags to true, which opted every existing connection into webhooks and automatic adds.
        this.Execute.Sql(
            "UPDATE \"ArrConnectionDefinitions\" SET \"EnableAutomaticAdd\" = 0, \"WebhookEnabled\" = 0 WHERE \"EnableAutomaticAdd\" = 1 OR \"WebhookEnabled\" = 1;");
    }

    public override void Down()
    {
    }
}
