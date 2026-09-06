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
}
