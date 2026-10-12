// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(54)]
public class AddTorrentDownloadPath : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("DownloadPath").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
