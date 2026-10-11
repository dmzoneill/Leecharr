// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(44)]
public class AddBannerLocalPath : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("TorrentMediaMetadata")
            .AddColumn("BannerLocalPath").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
