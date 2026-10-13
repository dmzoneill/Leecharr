// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using FluentAssertions;
using Npgsql;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class ConnectionStringFactoryTest
{
    private IAppFolderInfo appFolderInfo = null!;
    private IConfigFileProvider configFileProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns("/app/data");
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
    }

    [Test]
    public void SQLite_ConnectionString_Contains_DefaultTimeout_And_ForeignKeys()
    {
        this.configFileProvider.PostgresHost.Returns(string.Empty);

        var factory = new ConnectionStringFactory(this.appFolderInfo, this.configFileProvider);

        factory.DatabaseType.Should().Be(DatabaseType.SQLite);
        factory.MainDbConnectionString.Should().Contain("Default Timeout=30");
        factory.MainDbConnectionString.Should().Contain("Foreign Keys=True");
        factory.MainDbConnectionString.Should().NotContain("DateTimeKind");
        factory.MainDbConnectionString.Should().Contain(Path.Combine("/app/data", "leecharr.db"));
    }

    [Test]
    public void Postgres_ConnectionString_Contains_Pool_And_Timeout_Bounds()
    {
        this.configFileProvider.PostgresHost.Returns("postgres-host");
        this.configFileProvider.PostgresPort.Returns(5432);
        this.configFileProvider.PostgresMainDb.Returns("leecharr_db");
        this.configFileProvider.PostgresUser.Returns("user1");
        this.configFileProvider.PostgresPassword.Returns("pass1");

        var factory = new ConnectionStringFactory(this.appFolderInfo, this.configFileProvider);

        factory.DatabaseType.Should().Be(DatabaseType.PostgreSQL);
        var parsed = new NpgsqlConnectionStringBuilder(factory.MainDbConnectionString);
        parsed.Host.Should().Be("postgres-host");
        parsed.Port.Should().Be(5432);
        parsed.Database.Should().Be("leecharr_db");
        parsed.Username.Should().Be("user1");
        parsed.Password.Should().Be("pass1");
        parsed.Pooling.Should().BeTrue();
        parsed.MinPoolSize.Should().Be(1);
        parsed.MaxPoolSize.Should().Be(50);
        parsed.Timeout.Should().Be(15);
        parsed.CommandTimeout.Should().Be(30);
    }

    [Test]
    public void Postgres_ConnectionString_Escapes_Special_Characters_In_Credentials()
    {
        const string password = "secret;Ssl Mode=Disable";
        this.configFileProvider.PostgresHost.Returns("postgres-host");
        this.configFileProvider.PostgresPort.Returns(5432);
        this.configFileProvider.PostgresMainDb.Returns("leecharr_db");
        this.configFileProvider.PostgresUser.Returns("user=evil");
        this.configFileProvider.PostgresPassword.Returns(password);

        var factory = new ConnectionStringFactory(this.appFolderInfo, this.configFileProvider);

        var parsed = new NpgsqlConnectionStringBuilder(factory.MainDbConnectionString);
        parsed.Host.Should().Be("postgres-host");
        parsed.Port.Should().Be(5432);
        parsed.Database.Should().Be("leecharr_db");
        parsed.Username.Should().Be("user=evil");
        parsed.Password.Should().Be(password);
        parsed.SslMode.Should().NotBe(SslMode.Disable);
    }
}
