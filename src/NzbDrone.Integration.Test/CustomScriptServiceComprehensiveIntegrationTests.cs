// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CustomScriptServiceComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void CustomScriptService_TokenizeArguments_ParsesQuotesAndEscapesCorrectly()
    {
        // 1. Empty / whitespace arguments
        CustomScriptService.TokenizeArguments(null).Should().BeEmpty();
        CustomScriptService.TokenizeArguments(string.Empty).Should().BeEmpty();
        CustomScriptService.TokenizeArguments("    ").Should().BeEmpty();

        // 2. Simple arguments
        var simple = CustomScriptService.TokenizeArguments("arg1 arg2 arg3");
        simple.Should().ContainInOrder("arg1", "arg2", "arg3");

        // 3. Double quoted arguments with spaces
        var doubleQuoted = CustomScriptService.TokenizeArguments("param1 \"param with spaces\" param3");
        doubleQuoted.Should().ContainInOrder("param1", "param with spaces", "param3");

        // 4. Single quoted arguments
        var singleQuoted = CustomScriptService.TokenizeArguments("cmd 'quoted value' last");
        singleQuoted.Should().ContainInOrder("cmd", "quoted value", "last");

        // 5. Escapes
        var escaped = CustomScriptService.TokenizeArguments("escaped\\\"value path\\\\subpath");
        escaped.Should().NotBeEmpty();
    }

    [Test]
    public void CustomScriptService_ResolveInterpreter_IdentifiesCorrectBinaries()
    {
        var testArgs = new[] { "--flag", "value" };

        // 1. Shell scripts
        var (shBin, shArgs) = CustomScriptService.ResolveInterpreter("/scripts/test.sh", testArgs);
        if (!OperatingSystem.IsWindows())
        {
            shBin.Should().Be("/bin/sh");
            shArgs.Should().Contain("/scripts/test.sh");
        }

        var (bashBin, bashArgs) = CustomScriptService.ResolveInterpreter("/scripts/test.bash", testArgs);
        if (!OperatingSystem.IsWindows())
        {
            bashBin.Should().Be("/bin/bash");
            bashArgs.Should().Contain("/scripts/test.bash");
        }

        // 2. Python scripts
        var (pyBin, pyArgs) = CustomScriptService.ResolveInterpreter("C:\\scripts\\test.py", testArgs);
        pyBin.Should().BeOneOf("python3", "python");
        pyArgs.Should().Contain("C:\\scripts\\test.py");

        // 3. Node.js scripts
        var (jsBin, jsArgs) = CustomScriptService.ResolveInterpreter("index.js", testArgs);
        jsBin.Should().Be("node");
        jsArgs.Should().Contain("index.js");

        // 4. Overload with string arguments
        var (strBin, strArgs) = CustomScriptService.ResolveInterpreter("run.sh", "--arg1 test");
        if (!OperatingSystem.IsWindows())
        {
            strBin.Should().Be("/bin/sh");
            strArgs.Should().Contain("run.sh");
        }
    }

    [Test]
    public void CustomScriptService_SensitiveEnvironment_IdentifiesKeysCorrectly()
    {
        CustomScriptService.IsSensitiveEnvironmentVariable("DATABASE_PASSWORD").Should().BeTrue();
        CustomScriptService.IsSensitiveEnvironmentVariable("POSTGRES_PASS").Should().BeTrue();
        CustomScriptService.IsSensitiveEnvironmentVariable("AUTH_TOKEN").Should().BeTrue();
        CustomScriptService.IsSensitiveEnvironmentVariable("SECRET_KEY").Should().BeTrue();
        CustomScriptService.IsSensitiveEnvironmentVariable("LEECHARR__CONFIG").Should().BeTrue();

        CustomScriptService.IsSensitiveEnvironmentVariable("LEECHARR_TORRENT_NAME").Should().BeFalse();
        CustomScriptService.IsSensitiveEnvironmentVariable("LEECHARR_EVENT_TYPE").Should().BeFalse();
        CustomScriptService.IsSensitiveEnvironmentVariable("TR_TORRENT_NAME").Should().BeFalse();
        CustomScriptService.IsSensitiveEnvironmentVariable("PATH").Should().BeFalse();
        CustomScriptService.IsSensitiveEnvironmentVariable("HOME").Should().BeFalse();
        CustomScriptService.IsSensitiveEnvironmentVariable(null).Should().BeFalse();
    }

    [Test]
    public async Task CustomScriptService_ExecuteScriptAsync_RunsExecutableScript()
    {
        var scriptService = new CustomScriptService();

        var torrent = new Torrent
        {
            Id = 42,
            Name = "CustomScriptTestMovie.2024",
            Category = "movies",
            Status = TorrentStatus.Seeding,
            TotalSize = 1048576000,
            Uploaded = 2097152000,
            Downloaded = 1048576000,
            InfoHash = "cccccccccccccccccccccccccccccccccccccccc",
        };

        if (!OperatingSystem.IsWindows())
        {
            // Create a small temporary test script
            var tempScript = Path.Combine(Path.GetTempPath(), $"leecharr_test_script_{Guid.NewGuid():N}.sh");
            await File.WriteAllTextAsync(tempScript, "#!/bin/sh\necho \"Running script for event: $LEECHARR_EVENT_TYPE, torrent: $LEECHARR_TORRENT_NAME\"\nexit 0\n");

            try
            {
                var success = await scriptService.ExecuteScriptAsync(tempScript, torrent, "TorrentDownloadCompleted", "arg1 arg2");
                success.Should().BeTrue();

                var failScript = Path.Combine(Path.GetTempPath(), $"leecharr_fail_{Guid.NewGuid():N}.sh");
                await File.WriteAllTextAsync(failScript, "#!/bin/sh\nexit 1\n");
                try
                {
                    var failed = await scriptService.ExecuteScriptAsync(failScript, torrent, "TorrentDownloadCompleted", string.Empty);
                    failed.Should().BeFalse();
                }
                finally
                {
                    if (File.Exists(failScript))
                    {
                        File.Delete(failScript);
                    }
                }
            }
            finally
            {
                if (File.Exists(tempScript))
                {
                    File.Delete(tempScript);
                }
            }
        }

        // Non-existent script returns false
        var nonExistent = await scriptService.ExecuteScriptAsync("/path/to/missing_script_12345.sh", torrent, "TorrentAdded", null);
        nonExistent.Should().BeFalse();
    }
}
