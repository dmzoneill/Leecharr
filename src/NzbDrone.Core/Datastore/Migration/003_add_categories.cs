using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(3)]
public class AddCategories : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("Categories")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable().Unique()
            .WithColumn("SavePath").AsString().NotNullable()
            .WithColumn("DefaultUploadLimit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("DefaultDownloadLimit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TargetRatio").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("TargetSeedTimeMinutes").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("AutoStop").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
    }
}
