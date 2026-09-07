// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(21)]
public class AddArrConnectionExternalUrl : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("ArrConnectionDefinitions")
            .AddColumn("ExternalUrl").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
