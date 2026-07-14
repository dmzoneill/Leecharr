using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(9)]
public class AddIndexers : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("IndexerDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("ConfigContract").AsString().Nullable()
            .WithColumn("Settings").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("Url").AsString().NotNullable()
            .WithColumn("ApiKey").AsString().NotNullable().WithDefaultValue(string.Empty)
            .WithColumn("Categories").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("EnableRss").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("EnableSearch").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("FreeleechOnly").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("MinSeeders").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("DownloadClientId").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");

        Create.Table("RssRules")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("MustContain").AsString().Nullable()
            .WithColumn("MustNotContain").AsString().Nullable()
            .WithColumn("MinSeeders").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("MinSizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("MaxSizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("FreeleechOnly").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("CategoryId").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("IndexerIds").AsString().NotNullable().WithDefaultValue("[]");
    }

    public override void Down()
    {
    }
}
