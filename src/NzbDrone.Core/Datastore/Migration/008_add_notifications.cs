using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(8)]
public class AddNotifications : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("NotificationDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("ConfigContract").AsString().Nullable()
            .WithColumn("Settings").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnGrab").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnDownloadComplete").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnMediaInspected").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnExtractComplete").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnSeedGoalReached").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnTorrentDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnHealthIssue").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnHealthRestored").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnManualInteractionRequired").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnApplicationUpdate").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
    }

    public override void Down()
    {
    }
}
