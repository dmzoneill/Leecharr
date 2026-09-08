// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using FluentAssertions;
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
        factory.MainDbConnectionString.Should().Contain("Host=postgres-host");
        factory.MainDbConnectionString.Should().Contain("Port=5432");
        factory.MainDbConnectionString.Should().Contain("Database=leecharr_db");
        factory.MainDbConnectionString.Should().Contain("Username=user1");
        factory.MainDbConnectionString.Should().Contain("Password=pass1");
        factory.MainDbConnectionString.Should().Contain("MinPoolSize=1");
        factory.MainDbConnectionString.Should().Contain("MaxPoolSize=50");
        factory.MainDbConnectionString.Should().Contain("ConnectionIdleLifetime=30");
        factory.MainDbConnectionString.Should().Contain("Timeout=15");
        factory.MainDbConnectionString.Should().Contain("CommandTimeout=30");
    }
}
