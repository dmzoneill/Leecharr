// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(46)]
public class BackfillTorrentIsImportedTrue : NzbDroneMigrationBase
{
    public override void Up()
    {
        // Migration 24 backfilled every existing torrent with IsImported=false, which defers seed-goal removal.
        this.Execute.Sql(
            "UPDATE \"Torrents\" SET \"IsImported\" = 1 WHERE \"IsImported\" = 0;");
    }

    public override void Down()
    {
    }
}
