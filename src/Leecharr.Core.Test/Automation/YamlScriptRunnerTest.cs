using System;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class YamlScriptRunnerTest
{
    private YamlScriptRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _runner = new YamlScriptRunner();
    }

    [Test]
    public void ShouldExecuteYamlWorkflowAndApplyActions()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "The.Matrix.1999.2160p",
            TotalSize = 15_000_000_000,
            Category = "Unknown",
        };

        var yaml = "name: 'Categorize and Tag'\n" +
                   "steps:\n" +
                   "  - name: 'Check Large'\n" +
                   "    condition: '${torrent.size} > 1000000000'\n" +
                   "    actions:\n" +
                   "      - addTag: '4K-UHD'\n" +
                   "      - setCategory: 'Movies'\n";

        var script = new AutomationScript
        {
            Name = "YAML Rule",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("4K-UHD"));
        Assert.That(result.NewCategory, Is.EqualTo("Movies"));
    }

    [Test]
    public void ShouldExecuteYamlActionsWithRecheckAndCommand()
    {
        var torrent = new Torrent
        {
            Id = 99,
            Name = "Linux.ISO",
        };

        var yaml = "name: 'Recheck and Command'\n" +
                   "steps:\n" +
                   "  - name: 'Execute pipeline'\n" +
                   "    actions:\n" +
                   "      - command: 'Backup'\n" +
                   "      - recheck: true\n" +
                   "      - reannounce: true\n";

        var script = new AutomationScript
        {
            Name = "Pipeline",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldRecheck, Is.True);
        Assert.That(result.ShouldReannounce, Is.True);
    }

    [Test]
    public void ShouldCreateHardlinkOnDiskAndNotPolluteCommandQueue()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var src = Path.Combine(tempDir, "source.txt");
            var dest = Path.Combine(tempDir, "linked.txt");
            File.WriteAllText(src, "initial-hardlink-data");

            var yaml = "name: 'Create Hardlink'\n" +
                       "steps:\n" +
                       "  - name: 'Hardlink step'\n" +
                       "    actions:\n" +
                       "      - createHardlink:\n" +
                       $"          source: '{src}'\n" +
                       $"          dest: '{dest}'\n";

            var script = new AutomationScript
            {
                Name = "Hardlink Pipeline",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var torrent = new Torrent { Id = 1, Name = "Test" };
            var result = runner.Execute(script, torrent);

            Assert.That(result.Success, Is.True);
            Assert.That(File.Exists(dest), Is.True);
            Assert.That(File.ReadAllText(dest), Is.EqualTo("initial-hardlink-data"));

            // Verify it is a true hard link (shares same data blocks)
            File.WriteAllText(src, "modified-hardlink-data");
            Assert.That(File.ReadAllText(dest), Is.EqualTo("modified-hardlink-data"));

            // Verify internal commandQueue was not polluted with 'ln'
            commandQueue.DidNotReceive().PushRaw("ln", Arg.Any<string>(), Arg.Any<CommandTrigger>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void ShouldSetFilePermissionsOnDiskAndNotPolluteCommandQueue()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "perm-test.txt");
            File.WriteAllText(filePath, "permissions content");

            var yaml = "name: 'Set Permissions'\n" +
                       "steps:\n" +
                       "  - name: 'Chmod step'\n" +
                       "    actions:\n" +
                       "      - setFilePermissions:\n" +
                       $"          path: '{filePath}'\n" +
                       "          permissions: '644'\n";

            var script = new AutomationScript
            {
                Name = "Permissions Pipeline",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var torrent = new Torrent { Id = 2, Name = "Test" };
            var result = runner.Execute(script, torrent);

            Assert.That(result.Success, Is.True);

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(filePath);
                var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
                Assert.That(mode, Is.EqualTo(expected));
            }

            // Verify internal commandQueue was not polluted with 'chmod'
            commandQueue.DidNotReceive().PushRaw("chmod", Arg.Any<string>(), Arg.Any<CommandTrigger>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void ShouldSetDirectoryPermissionsRecursivelyOnDisk()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var subFile = Path.Combine(subDir, "file.txt");
        File.WriteAllText(subFile, "nested content");

        try
        {
            var yaml = "name: 'Set Permissions Recursive'\n" +
                       "steps:\n" +
                       "  - name: 'Chmod recursive step'\n" +
                       "    actions:\n" +
                       "      - setFilePermissions:\n" +
                       $"          path: '{tempDir}'\n" +
                       "          permissions: '755'\n" +
                       "          recursive: true\n";

            var script = new AutomationScript
            {
                Name = "Recursive Permissions Pipeline",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var torrent = new Torrent { Id = 3, Name = "Test" };
            var result = runner.Execute(script, torrent);

            Assert.That(result.Success, Is.True);

            if (!OperatingSystem.IsWindows())
            {
                var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

                Assert.That(new DirectoryInfo(tempDir).UnixFileMode, Is.EqualTo(expected));
                Assert.That(new DirectoryInfo(subDir).UnixFileMode, Is.EqualTo(expected));
                Assert.That(File.GetUnixFileMode(subFile), Is.EqualTo(expected));
            }

            commandQueue.DidNotReceive().PushRaw("chmod", Arg.Any<string>(), Arg.Any<CommandTrigger>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
