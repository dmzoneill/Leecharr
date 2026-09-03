// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private string tempDir = null!;
    private IAppFolderInfo appFolderInfo = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), $"leecharr-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            try
            {
                Directory.Delete(this.tempDir, true);
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
        var provider = new ConfigFileProvider(this.appFolderInfo);

        provider.ApiKey.Should().NotBeNullOrWhiteSpace();
        provider.Port.Should().Be(7889);
        provider.BindAddress.Should().Be("*");
        provider.EnableSsl.Should().BeFalse();
        provider.SslPort.Should().Be(7890);
        provider.SslCertPath.Should().BeEmpty();
        provider.SslKeyPath.Should().BeEmpty();
        provider.SslCertPassword.Should().BeEmpty();
        provider.RedirectHttpToHttps.Should().BeFalse();
        provider.LogLevel.Should().Be("info");
        provider.UrlBase.Should().BeEmpty();
        provider.PostgresPort.Should().Be(5432);

        var configFile = Path.Combine(this.tempDir, "config.xml");
        File.Exists(configFile).Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenConfigFileExists_LoadsValuesFromXml()
    {
        var configFile = Path.Combine(this.tempDir, "config.xml");
        var xDoc = new XDocument(
            new XElement(
                "Config",
                new XElement("Port", "8989"),
                new XElement("BindAddress", "127.0.0.1"),
                new XElement("ApiKey", "test-key-123"),
                new XElement("EnableSsl", "true"),
                new XElement("SslPort", "9890"),
                new XElement("SslCertPath", "/etc/ssl/cert.pfx"),
                new XElement("SslKeyPath", "/etc/ssl/key.pem"),
                new XElement("SslCertPassword", "certpass"),
                new XElement("RedirectHttpToHttps", "true"),
                new XElement("LogLevel", "debug"),
                new XElement("UrlBase", "/leecharr"),
                new XElement("PostgresHost", "db.example.com"),
                new XElement("PostgresPort", "5433"),
                new XElement("PostgresMainDb", "leecharr_db"),
                new XElement("PostgresUser", "dbuser"),
                new XElement("PostgresPassword", "dbpass")));
        xDoc.Save(configFile);

        var provider = new ConfigFileProvider(this.appFolderInfo);

        provider.Port.Should().Be(8989);
        provider.BindAddress.Should().Be("127.0.0.1");
        provider.ApiKey.Should().Be("test-key-123");
        provider.EnableSsl.Should().BeTrue();
        provider.SslPort.Should().Be(9890);
        provider.SslCertPath.Should().Be("/etc/ssl/cert.pfx");
        provider.SslKeyPath.Should().Be("/etc/ssl/key.pem");
        provider.SslCertPassword.Should().Be("certpass");
        provider.RedirectHttpToHttps.Should().BeTrue();
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
        var provider = new ConfigFileProvider(this.appFolderInfo);

        provider.SaveConfigDictionary(new Dictionary<string, object>
        {
            { "Port", 9090 },
            { "LogLevel", "trace" },
            { "UrlBase", "/custom" },
            { "EnableSsl", true },
            { "SslPort", 9443 },
            { "SslCertPath", "/custom/cert.pfx" },
            { "SslKeyPath", "/custom/key.pem" },
            { "SslCertPassword", "pass456" },
            { "RedirectHttpToHttps", true },
        });

        provider.EnableSsl.Should().BeTrue();
        provider.SslPort.Should().Be(9443);
        provider.SslCertPath.Should().Be("/custom/cert.pfx");
        provider.SslKeyPath.Should().Be("/custom/key.pem");
        provider.SslCertPassword.Should().Be("pass456");
        provider.RedirectHttpToHttps.Should().BeTrue();

        provider.Port.Should().Be(9090);
        provider.LogLevel.Should().Be("trace");
        provider.UrlBase.Should().Be("/custom");

        // Verify reloaded from new instance
        var reloaded = new ConfigFileProvider(this.appFolderInfo);
        reloaded.Port.Should().Be(9090);
        reloaded.LogLevel.Should().Be("trace");
        reloaded.UrlBase.Should().Be("/custom");
    }

    [Test]
    public void SaveConfigDictionary_WhenNull_DoesNotThrow()
    {
        var provider = new ConfigFileProvider(this.appFolderInfo);
        Action act = () => provider.SaveConfigDictionary(null!);
        act.Should().NotThrow();
    }
}
