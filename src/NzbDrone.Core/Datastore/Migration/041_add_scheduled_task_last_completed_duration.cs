// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(41)]
public class AddScheduledTaskLastCompletedDuration : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("ScheduledTasks")
            .AddColumn("LastCompletedDurationSeconds").AsInt64().Nullable();
    }

    public override void Down()
    {
    }
}
