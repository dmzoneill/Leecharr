// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(7)]
public class AddSpeedSchedules : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Table("SpeedSchedules")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Days").AsInt32().NotNullable().WithDefaultValue(127)
            .WithColumn("StartTime").AsString().NotNullable().WithDefaultValue("00:00:00")
            .WithColumn("EndTime").AsString().NotNullable().WithDefaultValue("23:59:59")
            .WithColumn("MaxDownloadSpeed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("MaxUploadSpeed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
