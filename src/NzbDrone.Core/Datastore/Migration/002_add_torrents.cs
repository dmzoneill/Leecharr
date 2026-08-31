using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(2)]
public class AddTorrents : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("Torrents")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("InfoHash").AsString().NotNullable().Unique()
            .WithColumn("TotalSize").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceLength").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Comment").AsString().Nullable()
            .WithColumn("CreatedBy").AsString().Nullable()
            .WithColumn("CreationDate").AsDateTime().Nullable()
            .WithColumn("IsPrivate").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Downloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Uploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Ratio").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Progress").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("DownloadSpeed").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("UploadSpeed").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Eta").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Seeders").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Leechers").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("SavePath").AsString().Nullable()
            .WithColumn("Category").AsString().Nullable()
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("DownloadLimit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("UploadLimit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("SequentialDownload").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("TargetRatio").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("TargetSeedTimeMinutes").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("DateAdded").AsDateTime().NotNullable()
            .WithColumn("DateCompleted").AsDateTime().Nullable()
            .WithColumn("LastActive").AsDateTime().Nullable()
            .WithColumn("TagIds").AsString().NotNullable().WithDefaultValue("[]");

        Create.Table("TorrentFiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().NotNullable().ForeignKey("Torrents", "Id")
            .WithColumn("Path").AsString().NotNullable()
            .WithColumn("Size").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceOffset").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("Progress").AsDouble().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
