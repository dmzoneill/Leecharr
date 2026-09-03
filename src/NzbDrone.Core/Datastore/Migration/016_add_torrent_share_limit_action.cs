// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(16)]
public class AddTorrentShareLimitAction : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("ShareLimitAction").AsString().NotNullable().WithDefaultValue("Pause");
    }

    public override void Down()
    {
    }
}
