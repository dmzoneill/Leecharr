using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Configuration;

[TestFixture]
public class ConfigFileProviderTest
{
    private string _tempDir = null!;
    private IAppFolderInfo _appFolderInfo = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"leecharr-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void Constructor_WhenAppFolderInfoNull_ThrowsArgumentNullException()
    {
        Action act = () => new ConfigFileProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Constructor_WhenConfigFileDoesNotExist_GeneratesApiKeyAndDefaults()
    {
        var provider = new ConfigFileProvider(_appFolderInfo);

        provider.ApiKey.Should().NotBeNullOrWhiteSpace();
        provider.Port.Should().Be(7889);
        provider.BindAddress.Should().Be("*");
        provider.EnableSsl.Should().BeFalse();
        provider.LogLevel.Should().Be("info");
        provider.UrlBase.Should().BeEmpty();
        provider.PostgresPort.Should().Be(5432);

        var configFile = Path.Combine(_tempDir, "config.xml");
        File.Exists(configFile).Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenConfigFileExists_LoadsValuesFromXml()
    {
        var configFile = Path.Combine(_tempDir, "config.xml");
        var xDoc = new XDocument(
            new XElement("Config",
                new XElement("Port", "8989"),
                new XElement("BindAddress", "127.0.0.1"),
                new XElement("ApiKey", "test-api-key-12345"),
                new XElement("EnableSsl", "true"),
                new XElement("LogLevel", "debug"),
                new XElement("UrlBase", "/leecharr"),
                new XElement("PostgresHost", "db.example.com"),
                new XElement("PostgresPort", "5433"),
                new XElement("PostgresMainDb", "leecharr_db"),
                new XElement("PostgresUser", "dbuser"),
                new XElement("PostgresPassword", "dbpass")));
        xDoc.Save(configFile);

        var provider = new ConfigFileProvider(_appFolderInfo);

        provider.Port.Should().Be(8989);
        provider.BindAddress.Should().Be("127.0.0.1");
        provider.ApiKey.Should().Be("test-api-key-12345");
        provider.EnableSsl.Should().BeTrue();
        provider.LogLevel.Should().Be("debug");
        provider.UrlBase.Should().Be("/leecharr");
        provider.PostgresHost.Should().Be("db.example.com");
        provider.PostgresPort.Should().Be(5433);
        provider.PostgresMainDb.Should().Be("leecharr_db");
        provider.PostgresUser.Should().Be("dbuser");
        provider.PostgresPassword.Should().Be("dbpass");
    }

    [Test]
    public void SaveConfigDictionary_UpdatesValuesAndSavesToFile()
    {
        var provider = new ConfigFileProvider(_appFolderInfo);

        provider.SaveConfigDictionary(new Dictionary<string, object>
        {
            { "Port", 9090 },
            { "LogLevel", "trace" },
            { "UrlBase", "/custom" }
        });

        provider.Port.Should().Be(9090);
        provider.LogLevel.Should().Be("trace");
        provider.UrlBase.Should().Be("/custom");

        // Verify reloaded from new instance
        var reloaded = new ConfigFileProvider(_appFolderInfo);
        reloaded.Port.Should().Be(9090);
        reloaded.LogLevel.Should().Be("trace");
        reloaded.UrlBase.Should().Be("/custom");
    }

    [Test]
    public void SaveConfigDictionary_WhenNull_DoesNotThrow()
    {
        var provider = new ConfigFileProvider(_appFolderInfo);
        Action act = () => provider.SaveConfigDictionary(null!);
        act.Should().NotThrow();
    }
}
