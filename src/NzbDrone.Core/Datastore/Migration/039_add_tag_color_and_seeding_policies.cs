// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(39)]
public class AddTagColorAndSeedingPolicies : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Tags")
            .AddColumn("Color").AsString().Nullable()
            .AddColumn("UploadLimitKbps").AsInt32().Nullable()
            .AddColumn("DownloadLimitKbps").AsInt32().Nullable()
            .AddColumn("MinSeedRatio").AsDouble().Nullable()
            .AddColumn("MinSeedTimeSeconds").AsInt64().Nullable();
    }

    public override void Down()
    {
    }
}
