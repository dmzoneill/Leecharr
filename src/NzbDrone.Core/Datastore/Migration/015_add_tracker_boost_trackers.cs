// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(15)]
public class AddTrackerBoostTrackers : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("TrackerBoostTrackers").Exists())
        {
            this.Create.Table("TrackerBoostTrackers")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Url").AsString().NotNullable()
                .WithColumn("Host").AsString().NotNullable()
                .WithColumn("Port").AsInt32().NotNullable().WithDefaultValue(80)
                .WithColumn("Protocol").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("SourceName").AsString().NotNullable().WithDefaultValue("Manual")
                .WithColumn("LatencyMs").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("LastScraped").AsDateTime().Nullable()
                .WithColumn("LastSuccess").AsDateTime().Nullable()
                .WithColumn("SuccessfulScrapes").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("FailedScrapes").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TotalSwarmsFound").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TotalVerifiedTorrents").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(true);

            this.Create.Index("IX_TrackerBoostTrackers_Url")
                .OnTable("TrackerBoostTrackers")
                .OnColumn("Url");

            this.Create.Index("IX_TrackerBoostTrackers_Status")
                .OnTable("TrackerBoostTrackers")
                .OnColumn("Status");
        }
    }

    public override void Down()
    {
    }
}
