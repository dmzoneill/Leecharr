// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(32)]
public class AddUserExternalLoginsIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (this.Schema.Table("UserExternalLogins").Exists())
        {
            this.Create.Index("IX_UserExternalLogins_UserId")
                .OnTable("UserExternalLogins")
                .OnColumn("UserId");

            this.Create.Index("IX_UserExternalLogins_Provider_Key")
                .OnTable("UserExternalLogins")
                .OnColumn("LoginProvider").Ascending()
                .OnColumn("ProviderKey").Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
    }
}
