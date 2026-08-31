// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(13)]
public class AddAuthAndIdentityProviders : NzbDroneMigrationBase
{
    public override void Up()
    {
        // 1. Users Table
        this.Create.Table("Users")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Identifier").AsGuid().NotNullable().Unique()
            .WithColumn("Username").AsString(255).NotNullable().Unique()
            .WithColumn("PasswordHash").AsString(500).Nullable()
            .WithColumn("Salt").AsString(255).Nullable()
            .WithColumn("Iterations").AsInt32().NotNullable().WithDefaultValue(100000)
            .WithColumn("Email").AsString(255).Nullable()
            .WithColumn("DisplayName").AsString(255).Nullable()
            .WithColumn("Roles").AsString().NotNullable().WithDefaultValue("[\"Admin\"]")
            .WithColumn("AvatarUrl").AsString(1000).Nullable()
            .WithColumn("ExternalProviderId").AsString(100).Nullable()
            .WithColumn("ExternalSubjectId").AsString(255).Nullable()
            .WithColumn("LastLogin").AsDateTime().Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable();

        // 2. IdentityProviders Table
        this.Create.Table("IdentityProviders")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ProviderId").AsString(100).NotNullable().Unique()
            .WithColumn("Name").AsString(255).NotNullable()
            .WithColumn("ProviderType").AsInt32().NotNullable()
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("ClientId").AsString(500).Nullable()
            .WithColumn("ClientSecretEncrypted").AsString(1000).Nullable()
            .WithColumn("IssuerUrl").AsString(1000).Nullable()
            .WithColumn("MetadataUrl").AsString(1000).Nullable()
            .WithColumn("Scopes").AsString(500).Nullable()
            .WithColumn("Certificate").AsString().Nullable()
            .WithColumn("RoleMappingRules").AsString().Nullable()
            .WithColumn("IconUrl").AsString(1000).Nullable()
            .WithColumn("ButtonText").AsString(255).Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable();

        // 3. UserSessions Table
        this.Create.Table("UserSessions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("UserId").AsInt32().NotNullable()
            .WithColumn("SessionToken").AsString(255).NotNullable().Unique()
            .WithColumn("RefreshToken").AsString(255).Nullable()
            .WithColumn("Expiry").AsDateTime().NotNullable()
            .WithColumn("IpAddress").AsString(100).Nullable()
            .WithColumn("UserAgent").AsString(1000).Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("LastActivity").AsDateTime().NotNullable();

        // 4. UserExternalLogins Table
        this.Create.Table("UserExternalLogins")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("UserId").AsInt32().NotNullable()
            .WithColumn("LoginProvider").AsString(100).NotNullable()
            .WithColumn("ProviderKey").AsString(255).NotNullable()
            .WithColumn("ProviderDisplayName").AsString(255).Nullable()
            .WithColumn("LinkedAt").AsDateTime().NotNullable();
    }

    public override void Down()
    {
        this.Delete.Table("UserExternalLogins");
        this.Delete.Table("UserSessions");
        this.Delete.Table("IdentityProviders");
        this.Delete.Table("Users");
    }
}
