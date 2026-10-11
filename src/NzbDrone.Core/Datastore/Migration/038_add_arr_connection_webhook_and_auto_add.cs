// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(38)]
public class AddArrConnectionWebhookAndAutoAdd : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("ArrConnectionDefinitions")
            .AddColumn("EnableAutomaticAdd").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("WebhookEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("WebhookHost").AsString().Nullable()
            .AddColumn("Category").AsString().Nullable()
            .AddColumn("SavePath").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
