using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(14)]
public class AddDownloadClients : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("DownloadClientDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("ClientType").AsString().NotNullable()
            .WithColumn("Host").AsString().NotNullable()
            .WithColumn("Port").AsInt32().NotNullable().WithDefaultValue(8080)
            .WithColumn("UseSsl").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Username").AsString().Nullable()
            .WithColumn("Password").AsString().Nullable()
            .WithColumn("Category").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1);
    }

    public override void Down()
    {
    }
}
