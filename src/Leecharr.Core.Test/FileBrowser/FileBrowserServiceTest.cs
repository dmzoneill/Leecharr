// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
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

        this.diskProvider.When(x => x.MoveFile(source, targetFile, true))
            .Do(x => throw new System.IO.IOException("EXDEV: Cross-device link"));

        this.service.Move(source, destDir);

        this.diskProvider.Received(1).CopyFile(source, targetFile, true);
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

        this.diskProvider.Received(1).CopyFile(sourceFile, targetFile, true);
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
        this.diskProvider.Received(1).CopyFile(fileInSource, fileInTarget, true);
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
        this.diskProvider.Received(1).CopyFile(fileInSource, fileInTarget, true);
        this.diskProvider.Received(1).DeleteFolder(expectedSource, true);
    }
}
