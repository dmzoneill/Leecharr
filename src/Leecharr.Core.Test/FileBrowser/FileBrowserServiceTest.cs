// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Security;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.FileBrowser;

[TestFixture]
public class FileBrowserServiceTest
{
    private IDiskProvider diskProvider = null!;
    private IConfigService configService = null!;
    private FileBrowserService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.service = new FileBrowserService(this.diskProvider, this.configService);
    }

    [Test]
    public void ListDirectory_WhenGetDirectoriesThrowsUnauthorizedAccessException_ReturnsEmptyEntriesWithoutThrowing()
    {
        var targetPath = "/restricted/folder";
        this.diskProvider.FolderExists(targetPath).Returns(true);
        this.diskProvider.GetDirectories(targetPath).Throws(new UnauthorizedAccessException("Permission denied"));
        this.diskProvider.GetFiles(targetPath, false).Returns(Array.Empty<string>());

        var result = this.service.ListDirectory(targetPath);

        result.Should().NotBeNull();
        result.Exists.Should().BeTrue();
        result.Entries.Should().BeEmpty();
    }

    [Test]
    public void ListDirectory_WhenGetFilesThrowsUnauthorizedAccessException_ReturnsEmptyEntriesWithoutThrowing()
    {
        var targetPath = "/restricted/files";
        this.diskProvider.FolderExists(targetPath).Returns(true);
        this.diskProvider.GetDirectories(targetPath).Returns(Array.Empty<string>());
        this.diskProvider.GetFiles(targetPath, false).Throws(new UnauthorizedAccessException("Permission denied"));

        var result = this.service.ListDirectory(targetPath);

        result.Should().NotBeNull();
        result.Exists.Should().BeTrue();
        result.Entries.Should().BeEmpty();
    }

    [Test]
    public void ListDirectory_WhenGetFilesThrowsSecurityException_ReturnsEmptyEntriesWithoutThrowing()
    {
        var targetPath = "/restricted/security";
        this.diskProvider.FolderExists(targetPath).Returns(true);
        this.diskProvider.GetDirectories(targetPath).Returns(Array.Empty<string>());
        this.diskProvider.GetFiles(targetPath, false).Throws(new SecurityException("Security error"));

        var result = this.service.ListDirectory(targetPath);

        result.Should().NotBeNull();
        result.Exists.Should().BeTrue();
        result.Entries.Should().BeEmpty();
    }

    [Test]
    public void Rename_WhenTargetFileExists_ThrowsInvalidOperationException()
    {
        var source = "/downloads/movie/cd1.flac";
        var dest = "/downloads/movie/cd2.flac";
        this.diskProvider.FileExists(dest).Returns(true);

        var action = () => this.service.Rename(source, "cd2.flac");

        action.Should().Throw<InvalidOperationException>().WithMessage($"Destination '{dest}' already exists.");
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [TestCase(".")]
    [TestCase("..")]
    [TestCase("   ")]
    public void Rename_WhenNewNameIsInvalid_ThrowsArgumentException(string invalidName)
    {
        var source = "/downloads/movie/cd1.flac";

        var action = () => this.service.Rename(source, invalidName);

        action.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Rename_WhenDestinationDoesNotExist_MovesFileWithOverwriteFalse()
    {
        var source = "/downloads/movie/cd1.flac";
        var dest = "/downloads/movie/cd2.flac";
        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(dest).Returns(false);
        this.diskProvider.FolderExists(dest).Returns(false);

        this.service.Rename(source, "cd2.flac");

        this.diskProvider.Received(1).MoveFile(source, dest, false);
    }

    [Test]
    public void Move_WhenMoveFileThrowsIOException_FallsBackToCopyAndDelete()
    {
        var source = "/downloads/source.mkv";
        var destDir = "/storage/movies";
        var targetFile = "/storage/movies/source.mkv";

        this.diskProvider.FolderExists(destDir).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(source).Returns(true);

        this.diskProvider.When(x => x.MoveFile(source, targetFile, false))
            .Do(x => throw new System.IO.IOException("EXDEV: Cross-device link"));

        this.service.Move(source, destDir);

        this.diskProvider.Received(1).CopyFile(source, targetFile, false);
        this.diskProvider.Received(1).DeleteFile(source);
    }

    [Test]
    public void Move_WhenMoveFolderThrowsIOException_FallsBackToRecursiveCopyAndDeleteFolder()
    {
        var source = "/downloads/album";
        var destDir = "/storage/music";
        var targetFolder = "/storage/music/album";
        var sourceFile = "/downloads/album/song.mp3";
        var targetFile = "/storage/music/album/song.mp3";

        this.diskProvider.FolderExists(destDir).Returns(true);
        this.diskProvider.FolderExists(source).Returns(true);
        this.diskProvider.GetFiles(source, false).Returns(new[] { sourceFile });
        this.diskProvider.GetDirectories(source).Returns(Array.Empty<string>());

        this.diskProvider.When(x => x.MoveFolder(source, targetFolder))
            .Do(x => throw new System.IO.IOException("EXDEV: Cross-device link"));

        this.service.Move(source, destDir);

        this.diskProvider.Received(1).CopyFile(sourceFile, targetFile, false);
        this.diskProvider.Received(1).DeleteFolder(source, true);
    }

    [Test]
    public void Move_WhenSourceAndTargetFileAreIdentical_ReturnsWithoutDiskOperations()
    {
        var source = "/downloads/source.mkv";
        var destDir = "/downloads";

        this.service.Move(source, destDir);

        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public void Move_WhenSourceAndTargetFolderAreIdentical_ReturnsWithoutDiskOperations()
    {
        var source = "/downloads/album";
        var destDir = "/downloads";

        this.diskProvider.FolderExists(source).Returns(true);

        this.service.Move(source, destDir);

        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
        this.diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Move_WhenMovingFolderIntoItself_ThrowsInvalidOperationException()
    {
        var source = "/downloads/album";
        var destDir = "/downloads/album";

        this.diskProvider.FolderExists(source).Returns(true);

        var action = () => this.service.Move(source, destDir);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot move a directory into one of its subdirectories.");

        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
        this.diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Move_WhenMovingFolderIntoSubdirectory_ThrowsInvalidOperationException()
    {
        var source = "/downloads/album";
        var destDir = "/downloads/album/disc1";

        this.diskProvider.FolderExists(source).Returns(true);

        var action = () => this.service.Move(source, destDir);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot move a directory into one of its subdirectories.");

        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
        this.diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_WhenSourceAndTargetFileAreIdentical_ReturnsWithoutDiskOperations()
    {
        var source = "/downloads/source.mkv";
        var destDir = "/downloads";

        this.service.Copy(source, destDir);

        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_WhenCopyingFolderIntoSubdirectory_ThrowsInvalidOperationException()
    {
        var source = "/downloads/album";
        var destDir = "/downloads/album/disc1";

        this.diskProvider.FolderExists(source).Returns(true);

        var action = () => this.service.Copy(source, destDir);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot copy a directory into one of its subdirectories.");

        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [TestCase("/downloads/album/", "/downloads")]
    [TestCase("/downloads/album", "/downloads")]
    [TestCase("/downloads/", "/")]
    [TestCase("/downloads", "/")]
    [TestCase("/", "/")]
    public void GetParentPath_WithVariousPaths_ReturnsExpectedParent(string inputPath, string expectedParent)
    {
        var result = this.service.GetParentPath(inputPath);

        result.Should().Be(expectedParent);
    }

    [Test]
    public void ResolvePath_WhenPathHasTrailingSeparator_TrimsSeparatorUnlessRoot()
    {
        this.service.ResolvePath("/downloads/album/").Should().Be("/downloads/album");
        this.service.ResolvePath("/").Should().Be("/");
    }

    [Test]
    public void Rename_WhenPathHasTrailingSeparator_RenamesInParentDirectory()
    {
        var source = "/downloads/album/";
        var expectedSource = "/downloads/album";
        var expectedDest = "/downloads/newalbum";

        this.diskProvider.FolderExists(expectedSource).Returns(true);
        this.diskProvider.FileExists(expectedDest).Returns(false);
        this.diskProvider.FolderExists(expectedDest).Returns(false);

        this.service.Rename(source, "newalbum");

        this.diskProvider.Received(1).MoveFolder(expectedSource, expectedDest);
    }

    [Test]
    public void Copy_WhenSourceDirectoryHasTrailingSeparator_CopiesDirectoryIntoDestination()
    {
        var source = "/downloads/album/";
        var destDir = "/storage/music/";
        var expectedSource = "/downloads/album";
        var expectedDestDir = "/storage/music";
        var expectedTargetDir = "/storage/music/album";
        var fileInSource = "/downloads/album/track1.flac";
        var fileInTarget = "/storage/music/album/track1.flac";

        this.diskProvider.FolderExists(expectedDestDir).Returns(true);
        this.diskProvider.FolderExists(expectedSource).Returns(true);
        this.diskProvider.FolderExists(expectedTargetDir).Returns(false);
        this.diskProvider.GetFiles(expectedSource, false).Returns(new[] { fileInSource });
        this.diskProvider.GetDirectories(expectedSource).Returns(Array.Empty<string>());

        this.service.Copy(source, destDir);

        this.diskProvider.Received(1).CreateFolder(expectedTargetDir);
        this.diskProvider.Received(1).CopyFile(fileInSource, fileInTarget, false);
    }

    [Test]
    public void Move_WhenSourceDirectoryHasTrailingSeparator_MovesDirectoryToDestination()
    {
        var source = "/downloads/album/";
        var destDir = "/storage/music/";
        var expectedSource = "/downloads/album";
        var expectedDestDir = "/storage/music";
        var expectedTargetDir = "/storage/music/album";

        this.diskProvider.FolderExists(expectedDestDir).Returns(true);
        this.diskProvider.FolderExists(expectedSource).Returns(true);

        this.service.Move(source, destDir);

        this.diskProvider.Received(1).MoveFolder(expectedSource, expectedTargetDir);
    }

    [Test]
    public void Move_WhenSourceDirectoryHasTrailingSeparatorAndThrowsIOException_FallsBackToCopyAndDelete()
    {
        var source = "/downloads/album/";
        var destDir = "/storage/music/";
        var expectedSource = "/downloads/album";
        var expectedDestDir = "/storage/music";
        var expectedTargetDir = "/storage/music/album";
        var fileInSource = "/downloads/album/track1.flac";
        var fileInTarget = "/storage/music/album/track1.flac";

        this.diskProvider.FolderExists(expectedDestDir).Returns(true);
        this.diskProvider.FolderExists(expectedSource).Returns(true);
        this.diskProvider.GetFiles(expectedSource, false).Returns(new[] { fileInSource });
        this.diskProvider.GetDirectories(expectedSource).Returns(Array.Empty<string>());

        this.diskProvider.When(x => x.MoveFolder(expectedSource, expectedTargetDir))
            .Do(x => throw new System.IO.IOException("Cross-device link"));

        this.service.Move(source, destDir);

        this.diskProvider.Received(1).CreateFolder(expectedTargetDir);
        this.diskProvider.Received(1).CopyFile(fileInSource, fileInTarget, false);
        this.diskProvider.Received(1).DeleteFolder(expectedSource, true);
    }

    [Test]
    public void Delete_WhenPathIsRootDirectory_ThrowsInvalidOperationException()
    {
        Action act = () => this.service.Delete("/");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [TestCase("/bin")]
    [TestCase("/etc")]
    [TestCase("/usr")]
    [TestCase("/var")]
    [TestCase("/boot")]
    [TestCase("/dev")]
    [TestCase("/proc")]
    [TestCase("/sys")]
    [TestCase("/root")]
    [TestCase("/home")]
    [TestCase("/opt")]
    [TestCase("/srv")]
    public void Delete_WhenPathIsSystemDirectory_ThrowsInvalidOperationException(string sysDir)
    {
        Action act = () => this.service.Delete(sysDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Delete_WhenPathIsAppDataFolder_ThrowsInvalidOperationException()
    {
        var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns("/config/leecharr");
        var svc = new FileBrowserService(this.diskProvider, this.configService, appFolderInfo);

        Action act = () => svc.Delete("/config/leecharr");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Delete_WhenPathIsDownloadDirRoot_ThrowsInvalidOperationException()
    {
        this.configService.DownloadDir.Returns("/downloads");
        var svc = new FileBrowserService(this.diskProvider, this.configService);

        Action act = () => svc.Delete("/downloads");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Delete_WhenPathIsEmptyOrWhitespace_ThrowsArgumentException()
    {
        Action act1 = () => this.service.Delete(string.Empty);
        act1.Should().Throw<ArgumentException>();

        Action act2 = () => this.service.Delete("   ");
        act2.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ResolvePath_WhenPathContainsNullByte_ThrowsArgumentException()
    {
        Action act = () => this.service.ResolvePath("/downloads/\0bad");
        act.Should().Throw<ArgumentException>().WithMessage("*invalid*");
    }

    [TestCase("/etc/shadow")]
    [TestCase("/root/.ssh/id_rsa")]
    [TestCase("/bin/bash")]
    [TestCase("/var/log/syslog")]
    public void IsRootOrSystemDirectory_WhenPathIsDescendantOfSystemDirectory_ReturnsTrue(string path)
    {
        this.service.IsRootOrSystemDirectory(path).Should().BeTrue();
    }

    [TestCase("/etc/shadow")]
    [TestCase("/root/.ssh/id_rsa")]
    public void Delete_WhenPathIsDescendantOfSystemDirectory_ThrowsInvalidOperationException(string path)
    {
        Action act = () => this.service.Delete(path);
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void IsRootOrSystemDirectory_WhenPathIsDescendantOfAppDataFolder_ReturnsTrue()
    {
        var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns("/config/leecharr");
        var svc = new FileBrowserService(this.diskProvider, this.configService, appFolderInfo);

        svc.IsRootOrSystemDirectory("/config/leecharr/leecharr.db").Should().BeTrue();
        svc.IsRootOrSystemDirectory("/config/leecharr/config.xml").Should().BeTrue();

        Action act = () => svc.Delete("/config/leecharr/leecharr.db");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void IsRootOrSystemDirectory_WhenPathIsDescendantOfStartUpFolder_ReturnsTrue()
    {
        var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
        appFolderInfo.StartUpFolder.Returns("/app/bin");
        var svc = new FileBrowserService(this.diskProvider, this.configService, appFolderInfo);

        svc.IsRootOrSystemDirectory("/app/bin/Leecharr.dll").Should().BeTrue();
    }

    [Test]
    public void IsRootOrSystemDirectory_WhenPathIsDescendantOfAppContextBaseDirectory_ReturnsTrue()
    {
        var descendant = System.IO.Path.Combine(AppContext.BaseDirectory, "some_assembly.dll");
        this.service.IsRootOrSystemDirectory(descendant).Should().BeTrue();
    }

    [Test]
    public void IsRootOrSystemDirectory_WhenPathIsInHome_ProtectsUnlessInsideDownloadDir()
    {
        this.configService.DownloadDir.Returns("/home/user/downloads");
        var svc = new FileBrowserService(this.diskProvider, this.configService);

        svc.IsRootOrSystemDirectory("/home/user/.bashrc").Should().BeTrue();
        svc.IsRootOrSystemDirectory("/home/user/downloads/movie.mkv").Should().BeFalse();
    }

    [TestCase("/var/lib/leecharr/downloads")]
    [TestCase("/opt/leecharr/downloads")]
    [TestCase("/usr/local/leecharr/downloads")]
    [TestCase("/srv/downloads")]
    public void IsRootOrSystemDirectory_WhenPathIsInsideDownloadDirUnderServiceRoot_ReturnsFalse(string downloadDir)
    {
        this.configService.DownloadDir.Returns(downloadDir);
        var svc = new FileBrowserService(this.diskProvider, this.configService);
        var file = downloadDir + "/movie.mkv";
        var folder = downloadDir + "/season";
        var sibling = downloadDir + "-other/movie.mkv";

        svc.IsRootOrSystemDirectory(downloadDir).Should().BeTrue();
        svc.IsRootOrSystemDirectory(file).Should().BeFalse();
        svc.IsRootOrSystemDirectory(folder).Should().BeFalse();
        svc.IsRootOrSystemDirectory(sibling).Should().BeTrue();
        svc.IsRootOrSystemDirectory("/var/log/syslog").Should().BeTrue();
        svc.IsRootOrSystemDirectory("/etc/shadow").Should().BeTrue();
    }

    [Test]
    public void IsRootOrSystemDirectory_WhenDownloadDirIsUnderFrozenSystemRoot_StaysProtected()
    {
        this.configService.DownloadDir.Returns("/etc/leecharr/downloads");
        var svc = new FileBrowserService(this.diskProvider, this.configService);

        svc.IsRootOrSystemDirectory("/etc/leecharr/downloads/movie.mkv").Should().BeTrue();
        svc.IsRootOrSystemDirectory("/root/downloads/movie.mkv").Should().BeTrue();
    }

    [Test]
    public void Delete_WhenPathIsInsideDownloadDirUnderVar_DeletesFile()
    {
        this.configService.DownloadDir.Returns("/var/lib/leecharr/downloads");
        var svc = new FileBrowserService(this.diskProvider, this.configService);
        var file = "/var/lib/leecharr/downloads/movie.mkv";
        this.diskProvider.FolderExists(file).Returns(false);

        svc.Delete(file);

        this.diskProvider.Received(1).DeleteFile(file);
    }

    [Test]
    public void CreateDirectory_WhenPathIsInsideDownloadDirUnderOpt_CreatesFolder()
    {
        this.configService.DownloadDir.Returns("/opt/leecharr/downloads");
        var svc = new FileBrowserService(this.diskProvider, this.configService);
        var folder = "/opt/leecharr/downloads/season";

        svc.CreateDirectory(folder);

        this.diskProvider.Received(1).CreateFolder(folder);
    }

    [Test]
    public void Rename_WhenPathIsInsideDownloadDirUnderSrv_RenamesFile()
    {
        this.configService.DownloadDir.Returns("/srv/downloads");
        var svc = new FileBrowserService(this.diskProvider, this.configService);
        var source = "/srv/downloads/movie.mkv";
        var dest = "/srv/downloads/renamed.mkv";
        this.diskProvider.FileExists(dest).Returns(false);
        this.diskProvider.FolderExists(dest).Returns(false);
        this.diskProvider.FolderExists(source).Returns(false);

        svc.Rename(source, "renamed.mkv");

        this.diskProvider.Received(1).MoveFile(source, dest, false);
    }

    [Test]
    public void CreateDirectory_WhenPathIsSystemDirectory_ThrowsInvalidOperationException()
    {
        Action act = () => this.service.CreateDirectory("/etc/subfolder");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Rename_WhenSourceIsSystemPath_ThrowsInvalidOperationException()
    {
        Action act = () => this.service.Rename("/etc/shadow", "shadow_bak");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Copy_WhenTargetFileExists_ThrowsInvalidOperationException()
    {
        var source = "/downloads/source.mkv";
        var destDir = "/storage/movies";
        var targetFile = "/storage/movies/source.mkv";

        this.diskProvider.FolderExists(destDir).Returns(true);
        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(source).Returns(true);
        this.diskProvider.FileExists(targetFile).Returns(true);

        var action = () => this.service.Copy(source, destDir);

        action.Should().Throw<InvalidOperationException>().WithMessage($"Destination '{targetFile}' already exists.");
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_WhenTargetDirectoryExists_ThrowsInvalidOperationException()
    {
        var source = "/downloads/album";
        var destDir = "/storage/music";
        var targetDir = "/storage/music/album";

        this.diskProvider.FolderExists(destDir).Returns(true);
        this.diskProvider.FolderExists(source).Returns(true);
        this.diskProvider.FolderExists(targetDir).Returns(true);

        var action = () => this.service.Copy(source, destDir);

        action.Should().Throw<InvalidOperationException>().WithMessage($"Destination '{targetDir}' already exists.");
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_WhenDestinationIsSystemDirectory_ThrowsInvalidOperationException()
    {
        Action act = () => this.service.Copy("/downloads/movie.mkv", "/etc");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Move_WhenDestinationIsSystemDirectory_ThrowsInvalidOperationException()
    {
        Action act = () => this.service.Move("/downloads/movie.mkv", "/etc");
        act.Should().Throw<InvalidOperationException>().WithMessage("*root or system directory*");
    }

    [Test]
    public void Move_WhenTargetDirectoryExists_ThrowsInvalidOperationExceptionWithoutCopyOrDelete()
    {
        var source = "/downloads/album";
        var destDir = "/storage/music";
        var targetDir = "/storage/music/album";

        this.diskProvider.FolderExists(destDir).Returns(true);
        this.diskProvider.FolderExists(source).Returns(true);
        this.diskProvider.FolderExists(targetDir).Returns(true);

        var action = () => this.service.Move(source, destDir);

        action.Should().Throw<InvalidOperationException>().WithMessage($"Destination '{targetDir}' already exists.");
        this.diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Move_WhenMoveFolderThrowsNonCrossDeviceIOException_DoesNotCopyOrDeleteSource()
    {
        var source = "/downloads/album";
        var destDir = "/storage/music";
        var targetFolder = "/storage/music/album";

        this.diskProvider.FolderExists(destDir).Returns(true);
        this.diskProvider.FolderExists(source).Returns(true);
        this.diskProvider.FolderExists(targetFolder).Returns(false);
        this.diskProvider.FileExists(targetFolder).Returns(false);

        this.diskProvider.When(x => x.MoveFolder(source, targetFolder))
            .Do(x => throw new System.IO.IOException("Cannot create a file when that file already exists."));

        var action = () => this.service.Move(source, destDir);

        action.Should().Throw<System.IO.IOException>();
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_WhenSourceDoesNotExist_ThrowsInvalidOperationExceptionWithoutCreatingDestination()
    {
        var source = "/downloads/missing.mkv";
        var destDir = "/storage/movies";

        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(destDir).Returns(false);

        var action = () => this.service.Copy(source, destDir);

        action.Should().Throw<InvalidOperationException>().WithMessage($"Source '{source}' does not exist.");
        this.diskProvider.DidNotReceive().CreateFolder(Arg.Any<string>());
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Move_WhenSourceDoesNotExist_ThrowsInvalidOperationExceptionWithoutCreatingDestination()
    {
        var source = "/downloads/missing.mkv";
        var destDir = "/storage/movies";

        this.diskProvider.FolderExists(source).Returns(false);
        this.diskProvider.FileExists(source).Returns(false);
        this.diskProvider.FolderExists(destDir).Returns(false);

        var action = () => this.service.Move(source, destDir);

        action.Should().Throw<InvalidOperationException>().WithMessage($"Source '{source}' does not exist.");
        this.diskProvider.DidNotReceive().CreateFolder(Arg.Any<string>());
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_WhenSourceFileSymlinkTargetsProtectedFile_ThrowsWithoutCopying()
    {
        string protectedDir = null;
        string linkDir = null;
        try
        {
            protectedDir = CreateTempDirectory();
            linkDir = CreateTempDirectory();
            var secret = Path.Combine(protectedDir, "secret.txt");
            File.WriteAllText(secret, "do-not-leak");
            var link = Path.Combine(linkDir, "secret.txt");
            File.CreateSymbolicLink(link, secret);

            if (LexicalPathIsProtected(link))
            {
                Assert.Pass("Temp directory is inside a protected path.");
            }

            var diskProvider = Substitute.For<IDiskProvider>();
            var service = CreateService(diskProvider, protectedDir);

            service.IsRootOrSystemDirectory(link).Should().BeTrue();

            var action = () => service.Copy(link, linkDir);

            action.Should().Throw<InvalidOperationException>().WithMessage("*root or system path*");
            diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Pass("Symlink creation is not permitted in this environment.");
        }
        finally
        {
            DeleteTempDirectory(linkDir);
            DeleteTempDirectory(protectedDir);
        }
    }

    [Test]
    public void Copy_WhenDirectoryContainsSymlinkToProtectedFile_ThrowsWithoutCopying()
    {
        string protectedDir = null;
        string sourceDir = null;
        string destDir = null;
        try
        {
            protectedDir = CreateTempDirectory();
            sourceDir = CreateTempDirectory();
            destDir = CreateTempDirectory();
            var secret = Path.Combine(protectedDir, "secret.txt");
            File.WriteAllText(secret, "do-not-leak");
            var link = Path.Combine(sourceDir, "secret.txt");
            File.CreateSymbolicLink(link, secret);

            if (LexicalPathIsProtected(sourceDir) || LexicalPathIsProtected(link))
            {
                Assert.Pass("Temp directory is inside a protected path.");
            }

            var diskProvider = Substitute.For<IDiskProvider>();
            diskProvider.FolderExists(sourceDir).Returns(true);
            diskProvider.FolderExists(destDir).Returns(true);
            diskProvider.GetFiles(sourceDir, false).Returns(new[] { link });
            diskProvider.GetDirectories(sourceDir).Returns(Array.Empty<string>());
            var service = CreateService(diskProvider, protectedDir);

            var action = () => service.Copy(sourceDir, destDir);

            action.Should().Throw<InvalidOperationException>().WithMessage("*root or system path*");
            diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Pass("Symlink creation is not permitted in this environment.");
        }
        finally
        {
            DeleteTempDirectory(sourceDir);
            DeleteTempDirectory(destDir);
            DeleteTempDirectory(protectedDir);
        }
    }

    [Test]
    public void Move_WhenSourceDirectorySymlinkTargetsProtectedDirectory_ThrowsWithoutCopyOrDelete()
    {
        string protectedDir = null;
        string linkDir = null;
        string destDir = null;
        try
        {
            protectedDir = CreateTempDirectory();
            linkDir = CreateTempDirectory();
            destDir = CreateTempDirectory();
            File.WriteAllText(Path.Combine(protectedDir, "secret.txt"), "do-not-leak");
            var directoryLink = Path.Combine(linkDir, "linked");
            Directory.CreateSymbolicLink(directoryLink, protectedDir);

            if (LexicalPathIsProtected(directoryLink))
            {
                Assert.Pass("Temp directory is inside a protected path.");
            }

            var diskProvider = Substitute.For<IDiskProvider>();
            var service = CreateService(diskProvider, protectedDir);

            var action = () => service.Move(directoryLink, destDir);

            action.Should().Throw<InvalidOperationException>().WithMessage("*root or system path*");
            diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
            diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
            diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Pass("Symlink creation is not permitted in this environment.");
        }
        finally
        {
            DeleteTempDirectory(linkDir);
            DeleteTempDirectory(destDir);
            DeleteTempDirectory(protectedDir);
        }
    }

    private static bool LexicalPathIsProtected(string path)
    {
        var service = new FileBrowserService(Substitute.For<IDiskProvider>(), Substitute.For<IConfigService>());
        return service.IsRootOrSystemDirectory(path);
    }

    private static FileBrowserService CreateService(IDiskProvider diskProvider, string protectedDirectory)
    {
        var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(protectedDirectory);
        return new FileBrowserService(diskProvider, Substitute.For<IConfigService>(), appFolderInfo);
    }

    private static string CreateTempDirectory()
    {
        var root = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        var directory = Path.Combine(root, "leecharr-fb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(directory, false);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, false);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        Directory.Delete(directory, true);
    }
}
