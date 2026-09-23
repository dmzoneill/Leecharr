// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(37)]
public class AddTrackerMetrics : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Create.Table("TrackerMetrics")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TrackerUrl").AsString().NotNullable()
            .WithColumn("Host").AsString().NotNullable()
            .WithColumn("Domain").AsString().NotNullable()
            .WithColumn("Protocol").AsString().NotNullable().WithDefaultValue("http")
            .WithColumn("Port").AsInt32().NotNullable().WithDefaultValue(80)
            .WithColumn("Status").AsString().NotNullable().WithDefaultValue("Working")
            .WithColumn("FirstSeen").AsDateTime().NotNullable()
            .WithColumn("LastAnnounce").AsDateTime().Nullable()
            .WithColumn("LastScrape").AsDateTime().Nullable()
            .WithColumn("LastSuccess").AsDateTime().Nullable()
            .WithColumn("LastErrorTime").AsDateTime().Nullable()
            .WithColumn("LastErrorMessage").AsString().Nullable()
            .WithColumn("TotalAnnounces").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("SuccessfulAnnounces").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("FailedAnnounces").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalScrapes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("SuccessfulScrapes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("FailedScrapes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalUploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalDownloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalLeft").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("SessionUploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("SessionDownloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalTorrentsTracked").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LastSeeders").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LastLeechers").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LastPeers").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalPeersDiscovered").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("AvgResponseTimeMs").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("LastResponseTimeMs").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("MinResponseTimeMs").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("MaxResponseTimeMs").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("ConsecutiveFailures").AsInt32().NotNullable().WithDefaultValue(0);

        this.Create.Index("IX_TrackerMetrics_TrackerUrl")
            .OnTable("TrackerMetrics")
            .OnColumn("TrackerUrl")
            .Unique();

        this.Create.Index("IX_TrackerMetrics_Domain")
            .OnTable("TrackerMetrics")
            .OnColumn("Domain");

        this.Create.Index("IX_TrackerMetrics_Host")
            .OnTable("TrackerMetrics")
            .OnColumn("Host");

        this.Create.Table("TrackerMetricSnapshots")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TrackerMetricId").AsInt32().NotNullable()
            .WithColumn("TrackerUrl").AsString().NotNullable()
            .WithColumn("Timestamp").AsDateTime().NotNullable()
            .WithColumn("ResponseTimeMs").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Uploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Downloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Seeders").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Leechers").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("PeersDiscovered").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("IsSuccess").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Operation").AsString().NotNullable().WithDefaultValue("Announce");

        this.Create.Index("IX_TrackerMetricSnapshots_TrackerMetricId")
            .OnTable("TrackerMetricSnapshots")
            .OnColumn("TrackerMetricId");

        this.Create.Index("IX_TrackerMetricSnapshots_Timestamp")
            .OnTable("TrackerMetricSnapshots")
            .OnColumn("Timestamp");
    }

    public override void Down()
    {
    }
}
