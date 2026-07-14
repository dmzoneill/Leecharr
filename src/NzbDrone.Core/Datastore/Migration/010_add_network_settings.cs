using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(10)]
public class AddNetworkSettings : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("NetworkSettings")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("BindInterface").AsString().Nullable()
            .WithColumn("EnableVpnKillSwitch").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("EnableUpnp").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("EnableNatPmp").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("ListenPort").AsInt32().NotNullable().WithDefaultValue(51413)
            .WithColumn("RandomizePortOnLaunch").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("EnableProxy").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("ProxyType").AsString().NotNullable().WithDefaultValue("SOCKS5")
            .WithColumn("ProxyHost").AsString().Nullable()
            .WithColumn("ProxyPort").AsInt32().NotNullable().WithDefaultValue(1080)
            .WithColumn("ProxyUsername").AsString().Nullable()
            .WithColumn("ProxyPassword").AsString().Nullable()
            .WithColumn("ProxyPeers").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("ProxyTrackers").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("ProxyIndexers").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("AnonymousMode").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("ClientEmulationPreset").AsString().NotNullable().WithDefaultValue("Leecharr");
    }

    public override void Down()
    {
    }
}
