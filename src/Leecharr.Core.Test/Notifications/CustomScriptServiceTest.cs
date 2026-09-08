// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class CustomScriptServiceTest
{
    private CustomScriptService service = null!;
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new CustomScriptService();
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "LeecharrCustomScriptTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, true);
            }
            catch
            {
            }
        }
    }

    private string CreateExecutableScript(string scriptContent)
    {
        var scriptName = OperatingSystem.IsWindows() ? "test_script.bat" : "test_script.sh";
        var scriptPath = Path.Combine(this.tempDirectory, scriptName);
        File.WriteAllText(scriptPath, scriptContent);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return scriptPath;
    }

    [Test]
    public async Task ExecuteScriptAsync_WhenScriptDoesNotExist_ReturnsFalse()
    {
        var result = await this.service.ExecuteScriptAsync("/path/to/nonexistent/script.sh", new Torrent(), "OnDownloadComplete");
        result.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteScriptAsync_WhenScriptSucceeds_ReturnsTrue()
    {
        var script = OperatingSystem.IsWindows()
            ? "@echo off\r\necho Success stdout\r\necho Success stderr 1>&2\r\nexit /b 0"
            : "#!/bin/sh\necho \"Success stdout\"\necho \"Success stderr\" >&2\nexit 0\n";

        var scriptPath = this.CreateExecutableScript(script);
        var result = await this.service.ExecuteScriptAsync(scriptPath, new Torrent { Id = 1 }, "OnDownloadComplete");

        result.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteScriptAsync_WhenScriptFails_ReturnsFalse()
    {
        var script = OperatingSystem.IsWindows()
            ? "@echo off\r\necho Failure\r\nexit /b 1"
            : "#!/bin/sh\necho \"Failure\"\nexit 1\n";

        var scriptPath = this.CreateExecutableScript(script);
        var result = await this.service.ExecuteScriptAsync(scriptPath, new Torrent { Id = 1 }, "OnDownloadComplete");

        result.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteScriptAsync_WhenScriptTimesOut_ReturnsFalseAndKillsProcess()
    {
        var timeoutService = new CustomScriptService(
            scriptTimeout: TimeSpan.FromMilliseconds(200),
            streamDrainTimeout: TimeSpan.FromMilliseconds(100));

        var script = OperatingSystem.IsWindows()
            ? "@echo off\r\nping 127.0.0.1 -n 10 >nul\r\nexit /b 0"
            : "#!/bin/sh\nsleep 5\nexit 0\n";

        var scriptPath = this.CreateExecutableScript(script);
        var startTime = DateTime.UtcNow;
        var result = await timeoutService.ExecuteScriptAsync(scriptPath, new Torrent { Id = 1 }, "OnDownloadComplete");
        var elapsed = DateTime.UtcNow - startTime;

        result.Should().BeFalse();
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task ExecuteScriptAsync_WhenChildProcessInheritsStdioPipes_CompletesWithinDrainTimeout()
    {
        // Spawns background process that keeps stdio pipe open and exits parent immediately
        var drainService = new CustomScriptService(
            scriptTimeout: TimeSpan.FromSeconds(10),
            streamDrainTimeout: TimeSpan.FromMilliseconds(500));

        var script = OperatingSystem.IsWindows()
            ? "@echo off\r\nstart /b cmd /c \"ping 127.0.0.1 -n 10 >nul\"\r\nexit /b 0"
            : "#!/bin/sh\n(sleep 10 &)\nexit 0\n";

        var scriptPath = this.CreateExecutableScript(script);
        var startTime = DateTime.UtcNow;
        var result = await drainService.ExecuteScriptAsync(scriptPath, new Torrent { Id = 1 }, "OnDownloadComplete");
        var elapsed = DateTime.UtcNow - startTime;

        result.Should().BeTrue();
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public void BuildEnvironmentVariables_UnderNonEnglishCulture_FormatsDecimalsWithPeriod()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var torrent = new Torrent
            {
                Id = 123,
                Name = "Sample Torrent",
                Ratio = 1.75,
                TotalSize = 1048576,
            };

            var meta = new NzbDrone.Core.MediaEnrichment.TorrentMediaMetadata
            {
                TorrentId = 123,
                Title = "Sample Movie",
                Year = 2024,
                Rating = 8.5,
            };

            var env = CustomScriptService.BuildEnvironmentVariables("OnDownloadComplete", torrent, meta);

            env["TORRENT_RATIO"].Should().Be("1.75");
            env["LEECHARR_TORRENT_RATIO"].Should().Be("1.75");
            env["LEECHARR_MEDIA_RATING"].Should().Be("8.5");
            env["TORRENT_ID"].Should().Be("123");
            env["LEECHARR_MEDIA_YEAR"].Should().Be("2024");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void SanitizeEnvironment_RemovesSensitiveVariablesAndPreservesSafeVariables()
    {
        var env = new System.Collections.Specialized.StringDictionary
        {
            ["LEECHARR__POSTGRES_PASSWORD"] = "secret_pg_pass",
            ["DATABASE_URL"] = "postgresql://usr:pass@localhost/db",
            ["DB_PASSWORD"] = "db_pass_123",
            ["LEECHARR_API_KEY"] = "api_key_abc",
            ["PROXY_PASSWORD"] = "proxy_secret",
            ["GITHUB_TOKEN"] = "ghp_123456",
            ["MY_AUTH_TOKEN"] = "token_xyz",
            ["MY_SECRET_KEY"] = "sec_123",
            ["PATH"] = "/usr/bin:/bin",
            ["HOME"] = "/home/user",
            ["USER"] = "user",
            ["LEECHARR_TORRENT_NAME"] = "Ubuntu.iso",
            ["TR_TORRENT_NAME"] = "Ubuntu.iso",
        };

        CustomScriptService.SanitizeEnvironment(env);

        env.ContainsKey("LEECHARR__POSTGRES_PASSWORD").Should().BeFalse();
        env.ContainsKey("DATABASE_URL").Should().BeFalse();
        env.ContainsKey("DB_PASSWORD").Should().BeFalse();
        env.ContainsKey("LEECHARR_API_KEY").Should().BeFalse();
        env.ContainsKey("PROXY_PASSWORD").Should().BeFalse();
        env.ContainsKey("GITHUB_TOKEN").Should().BeFalse();
        env.ContainsKey("MY_AUTH_TOKEN").Should().BeFalse();
        env.ContainsKey("MY_SECRET_KEY").Should().BeFalse();

        env.ContainsKey("PATH").Should().BeTrue();
        env.ContainsKey("HOME").Should().BeTrue();
        env.ContainsKey("USER").Should().BeTrue();
        env.ContainsKey("LEECHARR_TORRENT_NAME").Should().BeTrue();
        env.ContainsKey("TR_TORRENT_NAME").Should().BeTrue();
    }

    [Test]
    public void ResolveInterpreter_ResolvesInterpreterAppropriately()
    {
        var (shFile, shArgs) = CustomScriptService.ResolveInterpreter("/path/to/script.sh", "-v");
        var (pyFile, pyArgs) = CustomScriptService.ResolveInterpreter("/path/to/script.py", "--flag");

        if (OperatingSystem.IsWindows())
        {
            pyFile.Should().Be("python");
            pyArgs.Should().Contain("/path/to/script.py");
        }
        else
        {
            shFile.Should().Be("/bin/sh");
            shArgs.Should().Contain("/path/to/script.sh");
            pyFile.Should().Be("python3");
            pyArgs.Should().Contain("/path/to/script.py");
        }
    }

    [Test]
    public void Constructor_WithConfigService_SetsCustomScriptTimeout()
    {
        var configService = NSubstitute.Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.CustomScriptTimeoutSeconds.Returns(120);

        var customService = new CustomScriptService(configService: configService);
        customService.ScriptTimeout.Should().Be(TimeSpan.FromSeconds(120));
    }
}
