// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(55)]
public class AddTorrentShareLimitKinds : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("TargetRatioLimitKind").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("TargetSeedTimeLimitKind").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("InactiveSeedingTimeLimitMinutes").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("InactiveSeedingTimeLimitKind").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("CumulativeInactiveSeedingTimeSeconds").AsInt64().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
