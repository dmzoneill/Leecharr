// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(39)]
public class AddTorrentThresholdAndSmallTorrentLimit : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("Threshold").AsInt32().NotNullable().WithDefaultValue(1)
            .AddColumn("SmallTorrentLimit").AsInt32().NotNullable().WithDefaultValue(50);
    }

    public override void Down()
    {
    }
}
