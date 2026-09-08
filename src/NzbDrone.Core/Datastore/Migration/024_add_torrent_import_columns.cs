// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(24)]
public class AddTorrentImportColumns : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("Torrents")
            .AddColumn("IsImported").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("ImportedAt").AsDateTime().Nullable()
            .AddColumn("ImportedByArr").AsString().Nullable()
            .AddColumn("ImportPath").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
