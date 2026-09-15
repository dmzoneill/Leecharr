// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(31)]
public class AddDownloadHistoryIsPrivateAndTrackers : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (this.Schema.Table("DownloadHistory").Exists())
        {
            if (!this.Schema.Table("DownloadHistory").Column("IsPrivate").Exists())
            {
                this.Alter.Table("DownloadHistory")
                    .AddColumn("IsPrivate").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!this.Schema.Table("DownloadHistory").Column("Trackers").Exists())
            {
                this.Alter.Table("DownloadHistory")
                    .AddColumn("Trackers").AsString().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
