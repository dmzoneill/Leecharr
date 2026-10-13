// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
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
        provider.Branch.Should().Be("main");
        provider.PostgresPort.Should().Be(5432);

        var configFile = Path.Combine(this.tempDir, "config.xml");
        File.Exists(configFile).Should().BeTrue();

        if (UnixPrivatePermissions.IsUnixLike)
        {
            File.GetUnixFileMode(configFile).Should().Be(UnixPrivatePermissions.PrivateFileMode);
        }
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
                new XElement("Branch", "develop"),
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
        provider.Branch.Should().Be("develop");
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
            { "Branch", "nightly" },
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
        provider.Branch.Should().Be("nightly");

        // Verify reloaded from new instance
        var reloaded = new ConfigFileProvider(this.appFolderInfo);
        reloaded.Port.Should().Be(9090);
        reloaded.LogLevel.Should().Be("trace");
        reloaded.UrlBase.Should().Be("/custom");
        reloaded.Branch.Should().Be("nightly");
    }

    [Test]
    public void SaveConfigDictionary_WhenNull_DoesNotThrow()
    {
        var provider = new ConfigFileProvider(this.appFolderInfo);
        Action act = () => provider.SaveConfigDictionary(null!);
        act.Should().NotThrow();
    }

    [Test]
    public void SaveConfigDictionary_ReplacesConfigFileInsteadOfTruncatingTheLiveInode()
    {
        if (!UnixPrivatePermissions.IsUnixLike)
        {
            Assert.Ignore("Hard link replacement check requires a Unix filesystem.");
        }

        var configFile = Path.Combine(this.tempDir, "config.xml");
        var preservedFile = Path.Combine(this.tempDir, "config.xml.preserved");
        var original = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                "Config",
                new XElement("ApiKey", "preserve-me"),
                new XElement("Port", "4242")));
        original.Save(configFile);
        CreateHardLink(configFile, preservedFile);

        var provider = new ConfigFileProvider(this.appFolderInfo);
        provider.SaveConfigDictionary(new Dictionary<string, object>
        {
            { "Port", 5151 },
        });

        XDocument.Load(configFile).Root!.Element("Port")!.Value.Should().Be("5151");
        XDocument.Load(preservedFile).Root!.Element("Port")!.Value.Should().Be("4242");
        XDocument.Load(preservedFile).Root!.Element("ApiKey")!.Value.Should().Be("preserve-me");
        provider.Port.Should().Be(5151);
        provider.ApiKey.Should().Be("preserve-me");

        foreach (var path in Directory.GetFiles(this.tempDir))
        {
            var name = Path.GetFileName(path);
            (name is "config.xml" or "config.xml.preserved").Should().BeTrue($"unexpected file left behind: {name}");
        }

        File.GetUnixFileMode(configFile).Should().Be(UnixPrivatePermissions.PrivateFileMode);
    }

    private static void CreateHardLink(string source, string linkPath)
    {
        using var process = new Process();
        process.StartInfo.FileName = "ln";
        process.StartInfo.ArgumentList.Add(source);
        process.StartInfo.ArgumentList.Add(linkPath);
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start().Should().BeTrue();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
    }

    [Test]
    public void GetValue_SupportsSnakeCaseEnvironmentVariables()
    {
        try
        {
            Environment.SetEnvironmentVariable("LEECHARR__POSTGRES_HOST", "pg-snake.internal");
            Environment.SetEnvironmentVariable("LEECHARR__POSTGRES_PORT", "5439");
            Environment.SetEnvironmentVariable("LEECHARR__API_KEY", "api-key-snake-test");
            Environment.SetEnvironmentVariable("LEECHARR__BIND_ADDRESS", "0.0.0.0");
            Environment.SetEnvironmentVariable("LEECHARR__ENABLE_SSL", "true");
            Environment.SetEnvironmentVariable("LEECHARR__SSL_PORT", "8443");
            Environment.SetEnvironmentVariable("LEECHARR__SSL_CERT_PATH", "/etc/ssl/snake_cert.pfx");
            Environment.SetEnvironmentVariable("LEECHARR__LOG_LEVEL", "warn");
            Environment.SetEnvironmentVariable("LEECHARR__URL_BASE", "/snake-base");

            var provider = new ConfigFileProvider(this.appFolderInfo);

            provider.PostgresHost.Should().Be("pg-snake.internal");
            provider.PostgresPort.Should().Be(5439);
            provider.ApiKey.Should().Be("api-key-snake-test");
            provider.BindAddress.Should().Be("0.0.0.0");
            provider.EnableSsl.Should().BeTrue();
            provider.SslPort.Should().Be(8443);
            provider.SslCertPath.Should().Be("/etc/ssl/snake_cert.pfx");
            provider.LogLevel.Should().Be("warn");
            provider.UrlBase.Should().Be("/snake-base");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEECHARR__POSTGRES_HOST", null);
            Environment.SetEnvironmentVariable("LEECHARR__POSTGRES_PORT", null);
            Environment.SetEnvironmentVariable("LEECHARR__API_KEY", null);
            Environment.SetEnvironmentVariable("LEECHARR__BIND_ADDRESS", null);
            Environment.SetEnvironmentVariable("LEECHARR__ENABLE_SSL", null);
            Environment.SetEnvironmentVariable("LEECHARR__SSL_PORT", null);
            Environment.SetEnvironmentVariable("LEECHARR__SSL_CERT_PATH", null);
            Environment.SetEnvironmentVariable("LEECHARR__LOG_LEVEL", null);
            Environment.SetEnvironmentVariable("LEECHARR__URL_BASE", null);
        }
    }

    [Test]
    public void MaxTorrentFileSizeBytes_DefaultsTo250MB_AndCanBeConfigured()
    {
        var provider = new ConfigFileProvider(this.appFolderInfo);
        provider.MaxTorrentFileSizeBytes.Should().Be(250L * 1024 * 1024);

        try
        {
            Environment.SetEnvironmentVariable("LEECHARR__MAX_TORRENT_FILE_SIZE_BYTES", "524288000");
            var customProvider = new ConfigFileProvider(this.appFolderInfo);
            customProvider.MaxTorrentFileSizeBytes.Should().Be(524288000L);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEECHARR__MAX_TORRENT_FILE_SIZE_BYTES", null);
        }
    }

    [Test]
    public void GetValueBool_SupportsOneAndZeroEnvironmentValues()
    {
        try
        {
            Environment.SetEnvironmentVariable("LEECHARR_AUTHENTICATION_ENABLED", "1");
            Environment.SetEnvironmentVariable("LEECHARR_ALLOW_PRIVATE_NETWORK_REQUESTS", "0");
            Environment.SetEnvironmentVariable("LEECHARR_ENABLE_SSL", "0");
            Environment.SetEnvironmentVariable("LEECHARR_REDIRECT_HTTP_TO_HTTPS", "1");
            Environment.SetEnvironmentVariable("LEECHARR_TERMINAL_ACCESS_ENABLED", "0");

            var provider = new ConfigFileProvider(this.appFolderInfo);

            provider.AuthenticationEnabled.Should().BeTrue();
            provider.AllowPrivateNetworkRequests.Should().BeFalse();
            provider.EnableSsl.Should().BeFalse();
            provider.RedirectHttpToHttps.Should().BeTrue();
            provider.TerminalAccessEnabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEECHARR_AUTHENTICATION_ENABLED", null);
            Environment.SetEnvironmentVariable("LEECHARR_ALLOW_PRIVATE_NETWORK_REQUESTS", null);
            Environment.SetEnvironmentVariable("LEECHARR_ENABLE_SSL", null);
            Environment.SetEnvironmentVariable("LEECHARR_REDIRECT_HTTP_TO_HTTPS", null);
            Environment.SetEnvironmentVariable("LEECHARR_TERMINAL_ACCESS_ENABLED", null);
        }
    }

    [Test]
    public void GetValueBool_SupportsOneAndZeroConfigXmlValues()
    {
        var configFile = Path.Combine(this.tempDir, "config.xml");
        var xDoc = new XDocument(
            new XElement(
                "Config",
                new XElement("ApiKey", "test-key-123"),
                new XElement("AuthenticationEnabled", "1"),
                new XElement("AllowPrivateNetworkRequests", "0")));
        xDoc.Save(configFile);

        var provider = new ConfigFileProvider(this.appFolderInfo);

        provider.AuthenticationEnabled.Should().BeTrue();
        provider.AllowPrivateNetworkRequests.Should().BeFalse();
    }

    [Test]
    public void AuthenticationRequired_WhenAuthenticationEnabledFalse_DefaultsToDisabledForLocalAddresses()
    {
        var provider = new ConfigFileProvider(this.appFolderInfo);
        provider.AuthenticationEnabled.Should().BeFalse();
        provider.AuthenticationRequired.Should().Be(AuthenticationRequiredType.DisabledForLocalAddresses);
    }

    [Test]
    public void AuthenticationRequired_CanBeSavedAndReloaded()
    {
        var provider = new ConfigFileProvider(this.appFolderInfo);
        provider.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["AuthenticationEnabled"] = true,
            ["AuthenticationRequired"] = AuthenticationRequiredType.DisabledForLocalhost,
        });

        var reloadedProvider = new ConfigFileProvider(this.appFolderInfo);
        reloadedProvider.AuthenticationEnabled.Should().BeTrue();
        reloadedProvider.AuthenticationRequired.Should().Be(AuthenticationRequiredType.DisabledForLocalhost);
    }

    [Test]
    public void GetValue_ConcurrentWithSaveConfigDictionary_DoesNotThrow()
    {
        var provider = new ConfigFileProvider(this.appFolderInfo);
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(
            0,
            200,
            i =>
            {
                try
                {
                    if (i % 2 == 0)
                    {
                        provider.SaveConfigDictionary(new Dictionary<string, object>
                        {
                            { "Port", 8000 + (i % 50) },
                            { "AuthenticationEnabled", i % 4 == 0 },
                            { "LogLevel", i % 3 == 0 ? "debug" : "info" },
                        });
                    }
                    else
                    {
                        _ = provider.Port;
                        _ = provider.ApiKey;
                        _ = provider.AuthenticationEnabled;
                        _ = provider.BindAddress;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        exceptions.Should().BeEmpty();
    }
}
