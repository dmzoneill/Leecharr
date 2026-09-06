// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(19)]
public class CleanupOrphanedForeignKeyRecords : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Execute.Sql("DELETE FROM TorrentFiles WHERE TorrentId NOT IN (SELECT Id FROM Torrents);");
        this.Execute.Sql("DELETE FROM TrackerEntries WHERE TorrentId NOT IN (SELECT Id FROM Torrents);");
        this.Execute.Sql("DELETE FROM TorrentMediaMetadata WHERE TorrentId NOT IN (SELECT Id FROM Torrents);");
        this.Execute.Sql("DELETE FROM UserSessions WHERE UserId NOT IN (SELECT Id FROM Users);");
        this.Execute.Sql("DELETE FROM UserExternalLogins WHERE UserId NOT IN (SELECT Id FROM Users);");
    }

    public override void Down()
    {
    }
}
