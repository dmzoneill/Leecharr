// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(23)]
public class AddProwlarrIndexerColumns : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("IndexerDefinitions")
            .AddColumn("ProwlarrIndexerId").AsInt32().Nullable()
            .AddColumn("IsProwlarrManaged").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
    }
}
