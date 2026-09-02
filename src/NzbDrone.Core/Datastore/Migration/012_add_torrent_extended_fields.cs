// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(12)]
public class AddTorrentExtendedFields : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("TrackerUrl").AsString().Nullable()
            .AddColumn("QueuePosition").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("Label").AsString().Nullable()
            .AddColumn("InitialSeeding").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("ForceStart").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
    }
}
