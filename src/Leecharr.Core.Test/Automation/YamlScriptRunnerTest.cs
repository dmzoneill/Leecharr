using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
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

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("4K-UHD");
        result.NewCategory.Should().Be("Movies");
    }

    [Test]
    public void ShouldExecuteYamlActionsWithRecheckAndCommand()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

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

        var result = runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldRecheck.Should().BeTrue();
        result.ShouldReannounce.Should().BeTrue();
        commandQueue.Received(1).PushRaw("Backup", "{}", CommandTrigger.Manual);
    }

    [Test]
    public void ShouldCreateHardlinkOnDiskAndNotPolluteCommandQueue()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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

            result.Success.Should().BeTrue();
            File.Exists(dest).Should().BeTrue();
            File.ReadAllText(dest).Should().Be("initial-hardlink-data");

            File.WriteAllText(src, "modified-hardlink-data");
            File.ReadAllText(dest).Should().Be("modified-hardlink-data");

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

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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

            result.Success.Should().BeTrue();

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(filePath);
                var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
                mode.Should().Be(expected);
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

    [Test]
    public void ShouldSetDirectoryPermissionsRecursivelyOnDisk()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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

            result.Success.Should().BeTrue();

            if (!OperatingSystem.IsWindows())
            {
                var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

                new DirectoryInfo(tempDir).UnixFileMode.Should().Be(expected);
                new DirectoryInfo(subDir).UnixFileMode.Should().Be(expected);
                File.GetUnixFileMode(subFile).Should().Be(expected);
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

    [Test]
    public void ShouldSetSymbolicFilePermissionsOnDisk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "sym-perm.txt");
            File.WriteAllText(filePath, "symbolic perms test");

            var yaml = "name: 'Symbolic Permissions'\n" +
                        "steps:\n" +
                        "  - name: 'Chmod symbolic'\n" +
                        "    actions:\n" +
                        "      - setFilePermissions:\n" +
                        $"          path: '{filePath}'\n" +
                        "          permissions: 'rwxr-xr--'\n";

            var script = new AutomationScript
            {
                Name = "Symbolic Perm Script",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var result = _runner.Execute(script, new Torrent { Id = 4, Name = "PermTorrent" });

            result.Success.Should().BeTrue();

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(filePath);
                var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                UnixFileMode.OtherRead;
                mode.Should().Be(expected);
            }
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
    public void ShouldCreateSymlinkOnDisk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var src = Path.Combine(tempDir, "original.txt");
            var dest = Path.Combine(tempDir, "symlinked.txt");
            File.WriteAllText(src, "symlink source data");

            var yaml = "name: 'Create Symlink'\n" +
                        "steps:\n" +
                        "  - name: 'Symlink action'\n" +
                        "    actions:\n" +
                        "      - createSymlink:\n" +
                        $"          source: '{src}'\n" +
                        $"          dest: '{dest}'\n";

            var script = new AutomationScript
            {
                Name = "Symlink Script",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var result = _runner.Execute(script, new Torrent { Id = 5, Name = "SymlinkTorrent" });

            result.Success.Should().BeTrue();
            File.Exists(dest).Should().BeTrue();
            File.ReadAllText(dest).Should().Be("symlink source data");
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
    public void ShouldCleanExtensionsOnDisk_DeletingMatchingAndPreservingNonMatching()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var nfoFile = Path.Combine(tempDir, "movie.nfo");
            var txtFile = Path.Combine(tempDir, "notes.txt");
            var mkvFile = Path.Combine(tempDir, "movie.mkv");

            File.WriteAllText(nfoFile, "nfo content");
            File.WriteAllText(txtFile, "txt content");
            File.WriteAllText(mkvFile, "video content data");

            var yaml = "name: 'Clean Extensions'\n" +
                        "steps:\n" +
                        "  - name: 'Delete junk'\n" +
                        "    actions:\n" +
                        "      - cleanExtensions:\n" +
                        $"          directory: '{tempDir}'\n" +
                        "          extensions: 'nfo, txt'\n";

            var script = new AutomationScript
            {
                Name = "Clean Ext Script",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var result = _runner.Execute(script, new Torrent { Id = 6, Name = "CleanTorrent" });

            result.Success.Should().BeTrue();
            File.Exists(nfoFile).Should().BeFalse();
            File.Exists(txtFile).Should().BeFalse();
            File.Exists(mkvFile).Should().BeTrue();
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
    public void ShouldCleanExtensionsOnDisk_RespectingMaxSizeLimit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var smallFile = Path.Combine(tempDir, "small.sample");
            var largeFile = Path.Combine(tempDir, "large.sample");

            File.WriteAllText(smallFile, "short");
            File.WriteAllText(largeFile, new string('A', 500));

            var yaml = "name: 'Clean By Size'\n" +
                        "steps:\n" +
                        "  - name: 'Delete small samples'\n" +
                        "    actions:\n" +
                        "      - cleanExtensions:\n" +
                        $"          directory: '{tempDir}'\n" +
                        "          extensions: 'xyz'\n" +
                        "          maxSizeLimit: '50'\n";

            var script = new AutomationScript
            {
                Name = "Clean Size Script",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var result = _runner.Execute(script, new Torrent { Id = 7, Name = "SizeTorrent" });

            result.Success.Should().BeTrue();
            File.Exists(smallFile).Should().BeFalse();
            File.Exists(largeFile).Should().BeTrue();
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
    public void ShouldCalculateChecksum_AndStoreInVariableContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "check.txt");
            var content = "checksum content to hash";
            File.WriteAllText(filePath, content);

            string expectedHash;
            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                expectedHash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            }

            var yaml = "name: 'Calculate Checksum'\n" +
                        "steps:\n" +
                        "  - name: 'Hash file'\n" +
                        "    actions:\n" +
                        "      - calculateChecksum:\n" +
                        $"          path: '{filePath}'\n" +
                        "          targetVariable: 'fileHash'\n" +
                        "      - setCategory: '${fileHash}'\n";

            var script = new AutomationScript
            {
                Name = "Checksum Script",
                Code = yaml,
                Language = AutomationLanguage.Yaml,
            };

            var result = _runner.Execute(script, new Torrent { Id = 8, Name = "HashTorrent" });

            result.Success.Should().BeTrue();
            result.NewCategory.Should().Be(expectedHash);
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
    public void ShouldHandleMissingFilesGracefully_InFileActions()
    {
        var nonExistentPath = "/non/existent/path/for/sure/" + Guid.NewGuid().ToString("N");

        var yaml = "name: 'Graceful missing files'\n" +
            "steps:\n" +
            "  - name: 'Missing file steps'\n" +
            "    actions:\n" +
            "      - createHardlink:\n" +
                    $"          source: '{nonExistentPath}/src'\n" +
                    $"          dest: '{nonExistentPath}/dest'\n" +
            "      - createSymlink:\n" +
                    $"          source: '{nonExistentPath}/src'\n" +
                    $"          dest: '{nonExistentPath}/dest'\n" +
            "      - cleanExtensions:\n" +
                    $"          directory: '{nonExistentPath}'\n" +
            "          extensions: 'nfo'\n" +
            "      - calculateChecksum:\n" +
                    $"          path: '{nonExistentPath}/file'\n" +
            "          targetVariable: 'missingHash'\n";

        var script = new AutomationScript
        {
            Name = "Missing File Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, new Torrent { Id = 9, Name = "MissingTorrent" });

        result.Success.Should().BeTrue();
    }

    [Test]
    public void ShouldApplyTorrentMutations_TagsCategoryLimitsAndPriorities()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Avatar.2009",
            Category = "Movies",
        };

        var yaml = "name: 'Mutations'\n" +
            "steps:\n" +
            "  - name: 'Apply mutations'\n" +
            "    actions:\n" +
            "      - addTag: 'HDR'\n" +
            "      - addTag: 'Surround'\n" +
            "      - removeTag: 'OldTag'\n" +
            "      - setCategory: 'Movies-4K'\n" +
            "      - setUploadLimit: 1024\n" +
            "      - setDownloadLimit: 4096\n" +
            "      - setRatioLimit: 2.5\n" +
            "      - setSeedingTimeLimit: 180\n" +
            "      - setPriority: 'high'\n" +
            "      - setSequentialDownload: true\n" +
            "      - setSuperSeeding: true\n" +
            "      - setShareLimitAction: 'Stop'\n";

        var script = new AutomationScript
        {
            Name = "Mutation Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain(new[] { "HDR", "Surround" });
        result.TagsToRemove.Should().Contain("OldTag");
        result.NewCategory.Should().Be("Movies-4K");
        result.NewUploadLimitKbps.Should().Be(1024);
        result.NewDownloadLimitKbps.Should().Be(4096);
        result.NewRatioLimit.Should().Be(2.5);
        result.NewSeedingTimeLimitMinutes.Should().Be(180);
        result.NewPriority.Should().Be(2);
        result.NewSequentialDownload.Should().BeTrue();
        result.NewSuperSeeding.Should().BeTrue();
        result.ShareLimitAction.Should().Be("Stop");
    }

    [Test]
    public void ShouldApplyPriorityAliases_AndSequentialAliases()
    {
        var torrent = new Torrent { Id = 11, Name = "PriorityTorrent" };

        var yamlLow = "name: 'Prio Low'\nsteps:\n  - actions:\n      - setPriority: 'low'\n      - setSequential: false\n";
        var resultLow = _runner.Execute(new AutomationScript { Code = yamlLow, Language = AutomationLanguage.Yaml }, torrent);
        resultLow.NewPriority.Should().Be(0);
        resultLow.NewSequentialDownload.Should().BeFalse();

        var yamlOff = "name: 'Prio Off'\nsteps:\n  - actions:\n      - setPriority: 'donotdownload'\n";
        var resultOff = _runner.Execute(new AutomationScript { Code = yamlOff, Language = AutomationLanguage.Yaml }, torrent);
        resultOff.NewPriority.Should().Be(-1);

        var yamlNumeric = "name: 'Prio Numeric'\nsteps:\n  - actions:\n      - setPriority: 5\n";
        var resultNumeric = _runner.Execute(new AutomationScript { Code = yamlNumeric, Language = AutomationLanguage.Yaml }, torrent);
        resultNumeric.NewPriority.Should().Be(5);

        var yamlNormal = "name: 'Prio Normal'\nsteps:\n  - actions:\n      - setPriority: 'normal'\n";
        var resultNormal = _runner.Execute(new AutomationScript { Code = yamlNormal, Language = AutomationLanguage.Yaml }, torrent);
        resultNormal.NewPriority.Should().Be(1);
    }

    [Test]
    public void ShouldApplyPauseResumeRecheckReannounceAndRemove()
    {
        var torrent = new Torrent { Id = 12, Name = "StateTorrent" };

        var yamlPause = "name: 'Pause'\nsteps:\n  - actions:\n      - pause: true\n";
        var resultPause = _runner.Execute(new AutomationScript { Code = yamlPause, Language = AutomationLanguage.Yaml }, torrent);
        resultPause.ShouldPause.Should().BeTrue();
        resultPause.ShouldResume.Should().BeFalse();

        var yamlResume = "name: 'Resume'\nsteps:\n  - actions:\n      - resume: true\n";
        var resultResume = _runner.Execute(new AutomationScript { Code = yamlResume, Language = AutomationLanguage.Yaml }, torrent);
        resultResume.ShouldResume.Should().BeTrue();
        resultResume.ShouldPause.Should().BeFalse();

        var yamlReannounce = "name: 'Reannounce'\nsteps:\n  - actions:\n      - reannounceAll: true\n";
        var resultReannounce = _runner.Execute(new AutomationScript { Code = yamlReannounce, Language = AutomationLanguage.Yaml }, torrent);
        resultReannounce.ShouldReannounceAll.Should().BeTrue();

        var yamlRemove = "name: 'Remove'\nsteps:\n  - actions:\n      - remove: true\n        deleteData: true\n";
        var resultRemove = _runner.Execute(new AutomationScript { Code = yamlRemove, Language = AutomationLanguage.Yaml }, torrent);
        resultRemove.ShouldRemove.Should().BeTrue();
        resultRemove.DeleteDataOnRemove.Should().BeTrue();

        var yamlRemoveNoData = "name: 'Remove Keep Data'\nsteps:\n  - actions:\n      - remove: true\n";
        var resultRemoveNoData = _runner.Execute(new AutomationScript { Code = yamlRemoveNoData, Language = AutomationLanguage.Yaml }, torrent);
        resultRemoveNoData.ShouldRemove.Should().BeTrue();
        resultRemoveNoData.DeleteDataOnRemove.Should().BeFalse();
    }

    [Test]
    public void ShouldApplyTrackersBanningAndBoosting()
    {
        var torrent = new Torrent { Id = 13, Name = "TrackerTorrent" };

        var yaml = "name: 'Tracker Actions'\n" +
            "steps:\n" +
            "  - name: 'Modify trackers'\n" +
            "    actions:\n" +
            "      - addTracker: 'https://tracker.new.org/announce'\n" +
            "      - removeTracker: 'http://tracker.dead.com/announce'\n" +
            "      - replaceTracker:\n" +
            "          oldTracker: 'http://old.com'\n" +
            "          newTracker: 'https://new.com'\n" +
            "      - boostTracker: true\n" +
            "      - banPeer: '203.0.113.5'\n";

        var script = new AutomationScript
        {
            Name = "Tracker Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.TrackersToAdd.Should().Contain("https://tracker.new.org/announce");
        result.TrackersToRemove.Should().Contain("http://tracker.dead.com/announce");
        result.TrackersToReplace.Should().Contain(t => t.Item1 == "http://old.com" && t.Item2 == "https://new.com");
        result.ShouldBoostTracker.Should().BeTrue();
        result.PeersToBan.Should().Contain("203.0.113.5");
    }

    [Test]
    public void ShouldApplyFilePrioritiesExportAndMoveFiles()
    {
        var torrent = new Torrent
        {
            Id = 14,
            Name = "Movie.2024",
            SavePath = "/downloads/completed/Movie.2024",
        };

        var yaml = "name: 'File Actions'\n" +
            "steps:\n" +
            "  - name: 'Set file rules'\n" +
            "    actions:\n" +
            "      - setFilePriority:\n" +
            "          pattern: '*.sample.mkv'\n" +
            "          priority: 'donotdownload'\n" +
            "      - exportTorrent: '/backup/torrents/${torrent.name}.torrent'\n" +
            "      - moveFiles: '/media/library/${torrent.name}'\n";

        var script = new AutomationScript
        {
            Name = "Files Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.FilePriorities.Should().Contain(t => t.Item1 == "*.sample.mkv" && t.Item2 == "donotdownload");
        result.TorrentExportDestination.Should().Be("/backup/torrents/Movie.2024.torrent");
        result.NewSavePath.Should().Be("/media/library/Movie.2024");
    }

    [Test]
    public void ShouldApplyCleanUnwantedFiles_WithListAndDelimitedString()
    {
        var torrent = new Torrent { Id = 15, Name = "CleanTorrent" };

        var yaml = "name: 'Clean files'\n" +
            "steps:\n" +
            "  - name: 'Clean actions'\n" +
            "    actions:\n" +
            "      - cleanUnwantedFiles:\n" +
            "          - '*.nfo'\n" +
            "          - '*.txt'\n" +
            "      - cleanUnwantedFiles: '*.sfv, *.diz'\n";

        var script = new AutomationScript
        {
            Name = "Clean Files Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.CleanFilePatterns.Should().Contain(new[] { "*.nfo", "*.txt", "*.sfv", "*.diz" });
    }

    [Test]
    public void ShouldApplyExtractArchive_WithOptionsAndDefaults()
    {
        var torrent = new Torrent
        {
            Id = 16,
            Name = "ArchiveTorrent",
            SavePath = "/downloads/ArchiveTorrent",
        };

        var yaml = "name: 'Extract Archive'\n" +
            "steps:\n" +
            "  - name: 'Extract with options'\n" +
            "    actions:\n" +
            "      - extractArchive:\n" +
            "          destination: '/extracted/${torrent.name}'\n" +
            "          deleteArchive: true\n";

        var script = new AutomationScript
        {
            Name = "Extract Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldExtractArchive.Should().BeTrue();
        result.ExtractDestination.Should().Be("/extracted/ArchiveTorrent");
        result.DeleteArchiveOnExtract.Should().BeTrue();

        var yamlDefault = "name: 'Extract Default'\nsteps:\n  - actions:\n      - extractArchive: true\n";
        var resultDefault = _runner.Execute(new AutomationScript { Code = yamlDefault, Language = AutomationLanguage.Yaml }, torrent);
        resultDefault.ShouldExtractArchive.Should().BeTrue();
        resultDefault.ExtractDestination.Should().BeNull();
        resultDefault.DeleteArchiveOnExtract.Should().BeFalse();
    }

    [Test]
    public void ShouldQueueNotificationsArrSyncAndCustomScripts()
    {
        var torrent = new Torrent { Id = 17, Name = "ExternalActionTorrent" };

        var yaml = "name: 'External Actions'\n" +
            "steps:\n" +
            "  - name: 'Notify and Sync'\n" +
            "    actions:\n" +
            "      - sendNotification:\n" +
            "          title: 'Download Finished'\n" +
            "          message: 'Saved ${torrent.name}'\n" +
            "          provider: 'Discord'\n" +
            "      - sendNotification: 'Simple alert for ${torrent.name}'\n" +
            "      - notifyArr:\n" +
            "          appType: 'Radarr'\n" +
            "          instanceId: 1\n" +
            "      - syncArr: 'Sonarr'\n" +
            "      - runScript:\n" +
            "          path: '/scripts/postprocess.sh'\n" +
            "          timeout: 45\n" +
            "          args:\n" +
            "            - '--id'\n" +
            "            - '${torrent.id}'\n" +
            "      - runScript: '/scripts/simple.sh'\n";

        var script = new AutomationScript
        {
            Name = "External Actions Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.NotificationsToSend.Should().HaveCount(2);
        result.NotificationsToSend[0].Title.Should().Be("Download Finished");
        result.NotificationsToSend[0].Message.Should().Be("Saved ExternalActionTorrent");
        result.NotificationsToSend[0].Provider.Should().Be("Discord");
        result.NotificationsToSend[1].Title.Should().Be("Automation Alert");
        result.NotificationsToSend[1].Message.Should().Be("Simple alert for ExternalActionTorrent");

        result.ArrSyncsToSend.Should().HaveCount(2);
        result.ArrSyncsToSend[0].AppType.Should().Be("Radarr");
        result.ArrSyncsToSend[0].InstanceId.Should().Be(1);
        result.ArrSyncsToSend[1].AppType.Should().Be("Sonarr");

        result.ScriptsToRun.Should().HaveCount(2);
        result.ScriptsToRun[0].Path.Should().Be("/scripts/postprocess.sh");
        result.ScriptsToRun[0].TimeoutSeconds.Should().Be(45);
        result.ScriptsToRun[0].Arguments.Should().Contain(new[] { "--id", "${torrent.id}" });
        result.ScriptsToRun[1].Path.Should().Be("/scripts/simple.sh");
    }

    [Test]
    public void ShouldHandleInvokePipelineAndLogging()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var yaml = "name: 'Pipeline & Log'\n" +
            "steps:\n" +
            "  - name: 'Invoke & log'\n" +
            "    actions:\n" +
            "      - invokePipeline: 'CleanupWorkflow'\n" +
            "      - log:\n" +
            "          message: 'Detailed warning message'\n" +
            "          level: 'warn'\n" +
            "      - log: 'Simple info message'\n";

        var script = new AutomationScript
        {
            Name = "Pipeline Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = runner.Execute(script, new Torrent { Id = 18, Name = "LogTorrent" });

        result.Success.Should().BeTrue();
        commandQueue.Received(1).PushRaw("InvokePipeline", Arg.Is<string>(s => s.Contains("CleanupWorkflow")), CommandTrigger.Manual);
        result.OutputLog.Should().Contain("[ACTION] log (warn): Detailed warning message");
        result.OutputLog.Should().Contain("[ACTION] log: Simple info message");
    }

    [Test]
    public void ShouldEvaluateMathAndStopPipeline()
    {
        var torrent = new Torrent { Id = 19, Name = "MathTorrent" };

        var yaml = "name: 'Math & Stop'\n" +
            "steps:\n" +
            "  - name: 'Compute'\n" +
            "    actions:\n" +
            "      - setVariable:\n" +
            "          key: 'baseFactor'\n" +
            "          value: '10'\n" +
            "      - evalMath:\n" +
            "          expression: '${variables.baseFactor} * 4 + 2'\n" +
            "          targetVariable: 'calcVal'\n" +
            "      - setCategory: 'Category_${calcVal}'\n" +
            "  - name: 'Halt Step'\n" +
            "    actions:\n" +
            "      - stopPipeline: 'Completed early'\n" +
            "  - name: 'Unreachable Step'\n" +
            "    actions:\n" +
            "      - addTag: 'ShouldNeverBeAdded'\n";

        var script = new AutomationScript
        {
            Name = "Math Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.NewCategory.Should().Be("Category_42");
        result.ShouldStopPipeline.Should().BeTrue();
        result.StopReason.Should().Be("Completed early");
        result.TagsToAdd.Should().NotContain("ShouldNeverBeAdded");
    }

    [Test]
    public void ShouldEvaluateConditions_WithAllComparisonOperators()
    {
        var torrent = new Torrent
        {
            Id = 20,
            Name = "ConditionTorrent",
            TotalSize = 5_000_000_000,
            Ratio = 2.5,
            Category = "Movies",
            IsPrivate = false,
        };

        var yaml = "name: 'Conditions'\n" +
            "steps:\n" +
            "  - name: 'GT Check'\n" +
            "    condition: '${torrent.size} > 1000000'\n" +
            "    actions:\n" +
            "      - addTag: 'TagGT'\n" +
            "  - name: 'LT Skip Check'\n" +
            "    condition: '${torrent.size} < 1000'\n" +
            "    actions:\n" +
            "      - addTag: 'TagLT'\n" +
            "  - name: 'GTE Check'\n" +
            "    condition: '${torrent.ratio} >= 2.5'\n" +
            "    actions:\n" +
            "      - addTag: 'TagGTE'\n" +
            "  - name: 'LTE Check'\n" +
            "    condition: '${torrent.ratio} <= 3.0'\n" +
            "    actions:\n" +
            "      - addTag: 'TagLTE'\n" +
            "  - name: 'Equal Check'\n" +
            "    condition: '${torrent.category} == Movies'\n" +
            "    actions:\n" +
            "      - addTag: 'TagEqual'\n" +
            "  - name: 'Not Equal Check'\n" +
            "    condition: '${torrent.category} != Series'\n" +
            "    actions:\n" +
            "      - addTag: 'TagNotEqual'\n" +
            "  - name: 'Negation Check'\n" +
            "    condition: '!${torrent.isPrivate}'\n" +
            "    actions:\n" +
            "      - addTag: 'TagPublic'\n" +
            "  - name: 'Boolean Equivalence Check'\n" +
            "    condition: 'yes == true'\n" +
            "    actions:\n" +
            "      - addTag: 'TagYes'\n" +
            "  - name: 'Numeric Equivalence Check'\n" +
            "    condition: '5.0 == 5'\n" +
            "    actions:\n" +
            "      - addTag: 'TagNumericEq'\n";

        var script = new AutomationScript
        {
            Name = "Condition Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("TagGT");
        result.TagsToAdd.Should().NotContain("TagLT");
        result.TagsToAdd.Should().Contain("TagGTE");
        result.TagsToAdd.Should().Contain("TagLTE");
        result.TagsToAdd.Should().Contain("TagEqual");
        result.TagsToAdd.Should().Contain("TagNotEqual");
        result.TagsToAdd.Should().Contain("TagPublic");
        result.TagsToAdd.Should().Contain("TagYes");
        result.TagsToAdd.Should().Contain("TagNumericEq");
    }

    [Test]
    public void ShouldSubstituteAllTorrentVariables_AndInputsAndSecrets()
    {
        var torrent = new Torrent
        {
            Id = 21,
            Name = "TorrentWithAllVars",
            InfoHash = "aabbccddeeff00112233",
            TotalSize = 10_000_000,
            Ratio = 1.85,
            Category = "Music",
            TrackerUrl = "https://tracker.music.org",
            Status = TorrentStatus.Seeding,
            Progress = 1.0f,
            DownloadSpeed = 0,
            UploadSpeed = 500_000,
            Eta = 0,
            Seeders = 45,
            Leechers = 2,
            SavePath = "/music/completed",
            Uploaded = 18_500_000,
            Downloaded = 10_000_000,
            CumulativeSeedingTimeSeconds = 7200,
            Priority = 1,
            Label = "FLAC",
            Comment = "Best rip",
            TargetRatio = 2.0,
            TargetSeedTimeMinutes = 120,
            IsPrivate = true,
        };

        var yaml = "name: 'Variable Interpolation'\n" +
            "steps:\n" +
            "  - name: 'Check Variables'\n" +
            "    actions:\n" +
            "      - addTag: 'Hash-${torrent.infoHash}'\n" +
            "      - addTag: 'Cat-${torrent.category}'\n" +
            "      - addTag: 'Seeders-${torrent.seeders}'\n" +
            "      - addTag: 'Label-${torrent.label}'\n" +
            "      - addTag: 'Input-${inputs.customKey}'\n" +
            "      - addTag: 'Secret-${secrets.secretToken}'\n" +
            "      - addTag: 'SystemVpn-${system.vpnActive}'\n";

        var script = new AutomationScript
        {
            Name = "Variable Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
            InputsJson = "{\"customKey\": \"CustomVal\", \"secretToken\": \"TopSecret\"}",
        };

        var customInputs = new Dictionary<string, object>
        {
            { "extraInput", "ExtraValue" },
        };

        var result = _runner.Execute(script, torrent, customInputs: customInputs);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("Hash-aabbccddeeff00112233");
        result.TagsToAdd.Should().Contain("Cat-Music");
        result.TagsToAdd.Should().Contain("Seeders-45");
        result.TagsToAdd.Should().Contain("Label-FLAC");
        result.TagsToAdd.Should().Contain("Input-CustomVal");
        result.TagsToAdd.Should().Contain("Secret-TopSecret");
        result.TagsToAdd.Should().Contain("SystemVpn-True");
    }

    [Test]
    public void ShouldEscapeJsonStringsInsideQuotes_DuringVariableSubstitution()
    {
        var torrent = new Torrent
        {
            Id = 22,
            Name = "Movie \"Special\" Edition\nWithNewline\\Path",
            SavePath = "/downloads/Movie \"Special\"",
        };

        var yaml = "name: 'Escape Quotes'\n" +
            "steps:\n" +
            "  - name: 'Escaped step'\n" +
            "    actions:\n" +
            "      - setCategory: \"Title: ${torrent.name}\"\n";

        var script = new AutomationScript
        {
            Name = "Escape Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.NewCategory.Should().Contain("Special");
    }

    [Test]
    public async Task ShouldExecuteHttpStepAndRegisterResponse_WithLoopbackListener()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var req = context.Request;
            req.HttpMethod.Should().Be("POST");
            req.Headers["X-Custom-Header"].Should().Be("LeecharrEngine");

            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var reqBody = await reader.ReadToEndAsync();
            reqBody.Should().Contain("HttpTorrent");

            var response = context.Response;
            var responseJson = "{\"status\": 200, \"ack\": \"received\"}";
            var buffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentType = "application/json";
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
        });

        var yaml = "name: 'HTTP Step'\n" +
            "steps:\n" +
            "  - name: 'Post Webhook'\n" +
            "    http:\n" +
            "      method: 'POST'\n" +
                    $"      url: 'http://127.0.0.1:{port}/api/notify'\n" +
            "      headers:\n" +
            "        X-Custom-Header: 'LeecharrEngine'\n" +
            "      body: 'payload=${torrent.name}'\n" +
            "    register: 'httpResult'\n" +
            "  - name: 'Apply From Response'\n" +
            "    actions:\n" +
            "      - addTag: 'Status-${httpResult.status}'\n";

        var script = new AutomationScript
        {
            Name = "HTTP Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var torrent = new Torrent { Id = 23, Name = "HttpTorrent" };
        var result = _runner.Execute(script, torrent);
        await serverTask;

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("Status-200");
    }

    [Test]
    public void ShouldReturnSuccessFalse_WhenYamlHasInvalidSyntax()
    {
        var yaml = "name: [broken yaml: \n  steps: \n     - - invalid syntax:::";

        var script = new AutomationScript
        {
            Name = "Broken Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, new Torrent { Id = 24, Name = "BrokenTorrent" });

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("YAML Execution error:");
        result.OutputLog.Should().Contain("[ERROR]");
    }

    [Test]
    public void ShouldReturnSuccessTrue_WhenYamlIsEmptyOrHasNoSteps()
    {
        var emptyScript = new AutomationScript
        {
            Name = "Empty Script",
            Code = string.Empty,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(emptyScript, new Torrent { Id = 25, Name = "EmptyTorrent" });

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Empty YAML workflow executed successfully");
    }

    [Test]
    public void ShouldExecuteSuccessfully_WhenTorrentIsNull_ForSystemActions()
    {
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var runner = new YamlScriptRunner(commandQueue);

        var yaml = "name: 'System Only'\n" +
            "steps:\n" +
            "  - name: 'System command step'\n" +
            "    actions:\n" +
            "      - command: 'SystemHealthCheck'\n" +
            "      - sendNotification: 'System check complete'\n";

        var script = new AutomationScript
        {
            Name = "System Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = runner.Execute(script, torrent: null);

        result.Success.Should().BeTrue();
        commandQueue.Received(1).PushRaw("SystemHealthCheck", "{}", CommandTrigger.Manual);
        result.NotificationsToSend.Should().ContainSingle(n => n.Message == "System check complete");
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
