using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(4)]
public class AddMediaMetadata : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("TorrentMediaMetadata")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().NotNullable().Unique().ForeignKey("Torrents", "Id")
            .WithColumn("ArrType").AsString().NotNullable()
            .WithColumn("ArrMediaId").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Title").AsString().NotNullable()
            .WithColumn("Year").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Overview").AsString().Nullable()
            .WithColumn("PosterUrl").AsString().Nullable()
            .WithColumn("PosterLocalPath").AsString().Nullable()
            .WithColumn("BackdropUrl").AsString().Nullable()
            .WithColumn("BackdropLocalPath").AsString().Nullable()
            .WithColumn("MediaInfoJson").AsString().Nullable()
            .WithColumn("Genres").AsString().Nullable()
            .WithColumn("Rating").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("ImdbId").AsString().Nullable()
            .WithColumn("TmdbId").AsString().Nullable()
            .WithColumn("TvdbId").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
