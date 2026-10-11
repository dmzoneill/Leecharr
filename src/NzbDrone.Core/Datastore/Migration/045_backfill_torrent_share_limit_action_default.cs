// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(45)]
public class BackfillTorrentShareLimitActionDefault : NzbDroneMigrationBase
{
    public override void Up()
    {
        // Migration 16 backfilled every existing torrent with "Pause", which blocks GlobalShareLimitAction.
        this.Execute.Sql(
            "UPDATE \"Torrents\" SET \"ShareLimitAction\" = 'Default' WHERE \"ShareLimitAction\" = 'Pause';");
    }

    public override void Down()
    {
    }
}
