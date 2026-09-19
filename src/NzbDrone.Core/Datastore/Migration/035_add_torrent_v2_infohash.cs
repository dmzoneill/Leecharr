// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(35)]
public class AddTorrentV2InfoHash : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("V2InfoHash").AsString().Nullable();

        this.Create.Index("IX_Torrents_V2InfoHash")
            .OnTable("Torrents")
            .OnColumn("V2InfoHash");
    }

    public override void Down()
    {
    }
}
