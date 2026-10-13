// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Backup;

namespace Leecharr.Core.Test.Backup;

[TestFixture]
public class PostgresCliArgumentsTest
{
    [Test]
    public void TryBuildPgDumpArguments_WhenDatabaseNameIsFileFlag_RejectsInsteadOfSplitting()
    {
        const string dbName = "-f /config/config.xml";
        var interpolated = $"--clean --if-exists -h \"localhost\" -p 5432 -U \"postgres\" -d \"{dbName}\" -f \"/tmp/out.sql\"";
        interpolated.Should().Contain("-d \"-f /config/config.xml\"");

        var built = PostgresCliArguments.TryBuildPgDumpArguments("localhost", 5432, "postgres", dbName, "/tmp/out.sql", out var arguments);

        built.Should().BeFalse();
        arguments.Should().BeEmpty();
    }

    [Test]
    public void TryBuildPgDumpArguments_WhenDatabaseNameBreaksOutOfQuotes_RejectsInjectedFileFlag()
    {
        const string dbName = "leecharr\" -f /config/config.xml";
        var interpolated = $"--clean --if-exists -h \"localhost\" -p 5432 -U \"postgres\" -d \"{dbName}\" -f \"/tmp/out.sql\"";
        interpolated.Should().Contain("-f /config/config.xml");

        var built = PostgresCliArguments.TryBuildPgDumpArguments("localhost", 5432, "postgres", dbName, "/tmp/out.sql", out var arguments);

        built.Should().BeFalse();
        arguments.Should().NotContain("/config/config.xml");
        arguments.Should().NotContain("-f");
    }

    [TestCase("localhost\" -f /config/config.xml", "postgres", "leecharr")]
    [TestCase("localhost", "postgres\" -c DROP", "leecharr")]
    [TestCase("-h", "postgres", "leecharr")]
    [TestCase("localhost", "-U", "leecharr")]
    [TestCase("localhost", "postgres", "--file=/config/config.xml")]
    public void TryBuildPgDumpArguments_WhenConnectionFieldIsUnsafe_Rejects(string host, string user, string dbName)
    {
        var built = PostgresCliArguments.TryBuildPgDumpArguments(host, 5432, user, dbName, "/tmp/out.sql", out var arguments);

        built.Should().BeFalse();
        arguments.Should().BeEmpty();
    }

    [Test]
    public void TryBuildPgDumpArguments_WhenFieldsAreSafe_KeepsEachFieldAsOneArgument()
    {
        const string outputPath = "/tmp/out.sql";
        var built = PostgresCliArguments.TryBuildPgDumpArguments("db-1.example.com", 5432, "leecharr_user", "leecharr_main", outputPath, out var arguments);

        built.Should().BeTrue();
        arguments.Should().Equal(
            "--clean",
            "--if-exists",
            "-h",
            "db-1.example.com",
            "-p",
            "5432",
            "-U",
            "leecharr_user",
            "-d",
            "leecharr_main",
            "-f",
            outputPath);
        arguments.Should().ContainSingle(arg => arg == "-f");
    }

    [Test]
    public void TryBuildPsqlArguments_WhenDatabaseNameIsFileFlag_RejectsInsteadOfSplitting()
    {
        var built = PostgresCliArguments.TryBuildPsqlArguments("localhost", 5432, "postgres", "-f /config/config.xml", "/tmp/restore.sql", out var arguments);

        built.Should().BeFalse();
        arguments.Should().BeEmpty();
    }

    [Test]
    public void TryBuildPsqlArguments_WhenFieldsAreSafe_PassesFixedRestoreFlagsAndOneScriptPath()
    {
        const string scriptPath = "/tmp/restore.sql";
        var built = PostgresCliArguments.TryBuildPsqlArguments("localhost", 5432, "postgres", "leecharr", scriptPath, out var arguments);

        built.Should().BeTrue();
        arguments.Should().Equal(
            "-h",
            "localhost",
            "-p",
            "5432",
            "-U",
            "postgres",
            "-d",
            "leecharr",
            "-v",
            "ON_ERROR_STOP=1",
            "--single-transaction",
            "-f",
            scriptPath);
        arguments.Should().ContainSingle(arg => arg == "-f");
    }
}
