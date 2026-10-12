// Copyright (c) FeedItOut. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(56)]
public class TagsLabelCaseInsensitiveUnique : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!this.Schema.Table("Tags").Exists())
        {
            return;
        }

        this.Execute.Sql(
            """
            DELETE FROM "Tags"
            WHERE "Id" NOT IN (
                SELECT MIN("Id") FROM "Tags" GROUP BY LOWER("Label")
            );
            """);

        this.Execute.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tags_Label_Lower"
            ON "Tags" (LOWER("Label"));
            """);
    }

    public override void Down()
    {
    }
}
