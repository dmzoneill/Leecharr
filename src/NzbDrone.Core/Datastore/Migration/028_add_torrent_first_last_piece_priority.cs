// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(28)]
public class AddTorrentFirstLastPiecePriority : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("FirstLastPiecePriority").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
    }
}
