// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(57)]
public class AddArrConnectionLastSyncUtc : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("ArrConnectionDefinitions")
            .AddColumn("LastSyncUtc").AsDateTime().Nullable();
    }

    public override void Down()
    {
    }
}
