// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.Disk;

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
    public void Rename_WhenDestinationFileExists_ThrowsInvalidOperationException()
    {
        var current = Path.GetFullPath("/downloads/song1.flac");
        var dest = Path.GetFullPath("/downloads/song2.flac");

        this.diskProvider.FileExists(current).Returns(true);
        this.diskProvider.FileExists(dest).Returns(true);

        var act = () => this.service.Rename(current, "song2.flac");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already exists*");

        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [TestCase(".")]
    [TestCase("..")]
    [TestCase(" ")]
    [TestCase("")]
    [TestCase(null)]
    public void Rename_WhenNewNameIsInvalid_ThrowsArgumentException(string? invalidName)
    {
        var current = Path.GetFullPath("/downloads/song1.flac");

        var act = () => this.service.Rename(current, invalidName!);

        act.Should().Throw<ArgumentException>();
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Rename_WhenFileSuccessfullyRenamed_CallsMoveFileWithOverwriteFalse()
    {
        var current = Path.GetFullPath("/downloads/song1.flac");
        var dest = Path.GetFullPath("/downloads/song2.flac");

        this.diskProvider.FileExists(current).Returns(true);
        this.diskProvider.FileExists(dest).Returns(false);
        this.diskProvider.FolderExists(dest).Returns(false);

        this.service.Rename(current, "song2.flac");

        this.diskProvider.Received(1).MoveFile(current, dest, false);
    }

    [Test]
    public void Rename_WhenFolderSuccessfullyRenamed_CallsMoveFolder()
    {
        var current = Path.GetFullPath("/downloads/folder1");
        var dest = Path.GetFullPath("/downloads/folder2");

        this.diskProvider.FolderExists(current).Returns(true);
        this.diskProvider.FolderExists(dest).Returns(false);
        this.diskProvider.FileExists(dest).Returns(false);

        this.service.Rename(current, "folder2");

        this.diskProvider.Received(1).MoveFolder(current, dest);
    }
}
