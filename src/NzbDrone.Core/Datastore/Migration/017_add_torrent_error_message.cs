// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(17)]
public class AddTorrentErrorMessage : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("ErrorMessage").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
