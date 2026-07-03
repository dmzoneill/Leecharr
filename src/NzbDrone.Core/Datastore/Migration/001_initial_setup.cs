using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(1)]
public class InitialSetup : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("Config")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Key").AsString().NotNullable().Unique()
            .WithColumn("Value").AsString().NotNullable();

        Create.Table("ScheduledTasks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TypeName").AsString().NotNullable().Unique()
            .WithColumn("Interval").AsInt32().NotNullable()
            .WithColumn("LastExecution").AsDateTime().NotNullable()
            .WithColumn("LastStartTime").AsDateTime().Nullable();

        Create.Table("Commands")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Body").AsString().NotNullable()
            .WithColumn("Status").AsInt32().NotNullable()
            .WithColumn("QueuedAt").AsDateTime().NotNullable()
            .WithColumn("StartedAt").AsDateTime().Nullable()
            .WithColumn("EndedAt").AsDateTime().Nullable()
            .WithColumn("Message").AsString().Nullable()
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Trigger").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("Tags")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Label").AsString().NotNullable().Unique();
    }

    public override void Down()
    {
    }
}
