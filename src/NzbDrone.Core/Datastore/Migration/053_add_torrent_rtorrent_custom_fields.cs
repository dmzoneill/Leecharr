// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(53)]
public class AddTorrentRtorrentCustomFields : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("Custom2").AsString().Nullable()
            .AddColumn("Custom3").AsString().Nullable()
            .AddColumn("Custom4").AsString().Nullable()
            .AddColumn("Custom5").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
