using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(29)]
public class AddAutomationScripts : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("AutomationScripts")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Description").AsString().Nullable()
            .WithColumn("Trigger").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Language").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Code").AsString(int.MaxValue).NotNullable()
            .WithColumn("InputsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("TargetCategories").AsString(int.MaxValue).Nullable()
            .WithColumn("TargetTagIds").AsString(int.MaxValue).Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("LastExecutedAt").AsDateTime().Nullable()
            .WithColumn("LastExecutionStatus").AsString().Nullable()
            .WithColumn("LastExecutionLog").AsString(int.MaxValue).Nullable();

        Create.Index("IX_AutomationScripts_Trigger")
            .OnTable("AutomationScripts")
            .OnColumn("Trigger");
    }

    public override void Down()
    {
        Delete.Table("AutomationScripts");
    }
}
