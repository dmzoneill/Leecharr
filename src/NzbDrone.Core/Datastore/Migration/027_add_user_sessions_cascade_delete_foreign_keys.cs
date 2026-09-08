// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(27)]
public class AddUserSessionsCascadeDeleteForeignKeys : NzbDroneMigrationBase
{
    public override void Up()
    {
        this.Execute.Sql("DELETE FROM \"UserSessions\" WHERE \"UserId\" NOT IN (SELECT \"Id\" FROM \"Users\");");
        this.Execute.Sql("DELETE FROM \"UserExternalLogins\" WHERE \"UserId\" NOT IN (SELECT \"Id\" FROM \"Users\");");

        this.IfDatabase("Postgres", "Postgres92", "PostgreSQL")
            .Create.ForeignKey("FK_UserSessions_Users")
            .FromTable("UserSessions").ForeignColumn("UserId")
            .ToTable("Users").PrimaryColumn("Id")
            .OnDelete(System.Data.Rule.Cascade);

        this.IfDatabase("Postgres", "Postgres92", "PostgreSQL")
            .Create.ForeignKey("FK_UserExternalLogins_Users")
            .FromTable("UserExternalLogins").ForeignColumn("UserId")
            .ToTable("Users").PrimaryColumn("Id")
            .OnDelete(System.Data.Rule.Cascade);
    }

    public override void Down()
    {
        this.IfDatabase("Postgres", "Postgres92", "PostgreSQL")
            .Delete.ForeignKey("FK_UserSessions_Users").OnTable("UserSessions");
        this.IfDatabase("Postgres", "Postgres92", "PostgreSQL")
            .Delete.ForeignKey("FK_UserExternalLogins_Users").OnTable("UserExternalLogins");
    }
}
