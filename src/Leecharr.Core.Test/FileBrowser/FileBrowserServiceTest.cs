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
}
