// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(30)]
public class AddMissingForeignKeyIndices : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (this.Schema.Table("RssRules").Exists())
        {
            this.Create.Index("IX_RssRules_CategoryId")
                .OnTable("RssRules")
                .OnColumn("CategoryId");
        }

        if (this.Schema.Table("IndexerDefinitions").Exists())
        {
            this.Create.Index("IX_IndexerDefinitions_Implementation")
                .OnTable("IndexerDefinitions")
                .OnColumn("Implementation");
        }
    }

    public override void Down()
    {
    }
}
