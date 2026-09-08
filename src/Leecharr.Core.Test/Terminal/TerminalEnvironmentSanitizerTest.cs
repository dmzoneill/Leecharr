// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using Leecharr.Http.Terminal;
using NUnit.Framework;

namespace Leecharr.Core.Test.Terminal;

[TestFixture]
public class TerminalEnvironmentSanitizerTest
{
    [Test]
    public void Sanitize_ThrowsOnNullStartInfo()
    {
        var act = () => TerminalEnvironmentSanitizer.Sanitize(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void StripSensitiveKeys_ThrowsOnNullStartInfo()
    {
        var act = () => TerminalEnvironmentSanitizer.StripSensitiveKeys(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [TestCase("POSTGRES_PASSWORD", true)]
    [TestCase("postgres_user", true)]
    [TestCase("POSTGRES_MAIN_DB", true)]
    [TestCase("DATABASE_URL", true)]
    [TestCase("LEECHARR_API_KEY", true)]
    [TestCase("my_api_key", true)]
    [TestCase("JWT_SECRET", true)]
    [TestCase("COOKIE_SECRET", true)]
    [TestCase("AppleClientSecret", true)]
    [TestCase("AUTH_TOKEN", true)]
    [TestCase("BEARER_TOKEN", true)]
    [TestCase("USER_CREDENTIALS", true)]
    [TestCase("SSH_PRIVATE_KEY", true)]
    [TestCase("LEECHARR_CONFIG_PATH", true)]
    [TestCase("PATH", false)]
    [TestCase("HOME", false)]
    [TestCase("USER", false)]
    [TestCase("SHELL", false)]
    [TestCase("TERM", false)]
    [TestCase("COLORTERM", false)]
    [TestCase("LANG", false)]
    [TestCase("PWD", false)]
    [TestCase("TMPDIR", false)]
    public void IsSensitiveKey_CorrectlyIdentifiesSensitiveAndSafeKeys(string key, bool expectedSensitive)
    {
        TerminalEnvironmentSanitizer.IsSensitiveKey(key).Should().Be(expectedSensitive);
    }

    [Test]
    public void Sanitize_StripsSensitiveKeys_AndPopulatesStandardShellVariables()
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = "/tmp",
        };

        startInfo.EnvironmentVariables["POSTGRES_PASSWORD"] = "db_pass_123";
        startInfo.EnvironmentVariables["POSTGRES_USER"] = "postgres_user";
        startInfo.EnvironmentVariables["DATABASE_URL"] = "postgres://usr:pwd@localhost:5432/db";
        startInfo.EnvironmentVariables["LEECHARR_API_KEY"] = "api_key_xyz";
        startInfo.EnvironmentVariables["JWT_SECRET"] = "super_jwt_secret";
        startInfo.EnvironmentVariables["AUTH_TOKEN"] = "token_abc";
        startInfo.EnvironmentVariables["CUSTOM_SECRET_KEY"] = "hidden";

        TerminalEnvironmentSanitizer.Sanitize(startInfo);

        // Verify sensitive keys are removed
        startInfo.EnvironmentVariables.ContainsKey("POSTGRES_PASSWORD").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("POSTGRES_USER").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("DATABASE_URL").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("LEECHARR_API_KEY").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("JWT_SECRET").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("AUTH_TOKEN").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("CUSTOM_SECRET_KEY").Should().BeFalse();

        // Verify standard shell variables are populated
        startInfo.EnvironmentVariables.ContainsKey("TERM").Should().BeTrue();
        startInfo.EnvironmentVariables["TERM"].Should().Be("xterm-256color");

        startInfo.EnvironmentVariables.ContainsKey("COLORTERM").Should().BeTrue();
        startInfo.EnvironmentVariables["COLORTERM"].Should().Be("truecolor");

        startInfo.EnvironmentVariables.ContainsKey("LANG").Should().BeTrue();
        startInfo.EnvironmentVariables.ContainsKey("PATH").Should().BeTrue();
        startInfo.EnvironmentVariables.ContainsKey("HOME").Should().BeTrue();
        startInfo.EnvironmentVariables.ContainsKey("USER").Should().BeTrue();
        startInfo.EnvironmentVariables.ContainsKey("SHELL").Should().BeTrue();
        startInfo.EnvironmentVariables.ContainsKey("PWD").Should().BeTrue();
        startInfo.EnvironmentVariables["PWD"].Should().Be("/tmp");
        startInfo.EnvironmentVariables.ContainsKey("PS1").Should().BeTrue();
    }

    [Test]
    public void StripSensitiveKeys_RemovesOnlySensitiveKeysFromPrepopulatedEnvironment()
    {
        var startInfo = new ProcessStartInfo();
        startInfo.EnvironmentVariables["SAFE_CUSTOM_VAR"] = "visible_value";
        startInfo.EnvironmentVariables["ANOTHER_SAFE_SETTING"] = "12345";
        startInfo.EnvironmentVariables["DB_PASSWORD"] = "secret123";
        startInfo.EnvironmentVariables["SERVICE_TOKEN"] = "tok_xyz";
        startInfo.EnvironmentVariables["API_KEY_PUBLIC"] = "key_val";

        TerminalEnvironmentSanitizer.StripSensitiveKeys(startInfo);

        startInfo.EnvironmentVariables.ContainsKey("SAFE_CUSTOM_VAR").Should().BeTrue();
        startInfo.EnvironmentVariables["SAFE_CUSTOM_VAR"].Should().Be("visible_value");
        startInfo.EnvironmentVariables.ContainsKey("ANOTHER_SAFE_SETTING").Should().BeTrue();

        startInfo.EnvironmentVariables.ContainsKey("DB_PASSWORD").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("SERVICE_TOKEN").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("API_KEY_PUBLIC").Should().BeFalse();
    }
}
