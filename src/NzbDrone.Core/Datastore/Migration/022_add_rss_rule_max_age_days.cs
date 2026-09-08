// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(22)]
public class AddRssRuleMaxAgeDays : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("RssRules")
            .AddColumn("MaxAgeDays").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
