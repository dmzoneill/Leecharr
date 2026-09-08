// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(25)]
public class AddMusicbrainzAndMediaFields : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Alter.Table("TorrentMediaMetadata")
            .AddColumn("MusicBrainzId").AsString().Nullable()
            .AddColumn("ArtistName").AsString().Nullable()
            .AddColumn("AlbumTitle").AsString().Nullable()
            .AddColumn("Cast").AsString().Nullable()
            .AddColumn("BannerUrl").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
