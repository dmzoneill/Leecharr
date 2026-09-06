// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(20)]
public class AddTorrentCumulativeSeedingTime : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("CumulativeSeedingTimeSeconds").AsInt64().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
