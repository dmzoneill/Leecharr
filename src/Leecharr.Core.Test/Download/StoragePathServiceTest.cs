using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;

namespace Leecharr.Core.Test.Download;

[TestFixture]
public class StoragePathServiceTest
{
    private IConfigService _configService = null!;
    private ICategoryService _categoryService = null!;
    private IDiskProvider _diskProvider = null!;
    private StoragePathService _storagePathService = null!;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _categoryService = Substitute.For<ICategoryService>();
        _diskProvider = Substitute.For<IDiskProvider>();

        _storagePathService = new StoragePathService(_configService, _categoryService, _diskProvider);
    }

    [Test]
    public void GetIncompleteDirectory_WhenConfigured_ReturnsConfiguredPath()
    {
        _configService.IncompleteDownloadDir.Returns("/custom/incomplete");
        _diskProvider.FolderExists("/custom/incomplete").Returns(true);

        var path = _storagePathService.GetIncompleteDirectory();

        path.Should().Be("/custom/incomplete");
    }

    [Test]
    public void GetIncompleteDirectory_WhenNotExisting_CreatesFolder()
    {
        _configService.IncompleteDownloadDir.Returns("/custom/incomplete");
        _diskProvider.FolderExists("/custom/incomplete").Returns(false);

        var path = _storagePathService.GetIncompleteDirectory();

        path.Should().Be("/custom/incomplete");
        _diskProvider.Received(1).CreateFolder("/custom/incomplete");
    }

    [Test]
    public void GetCompletedDirectory_WhenCategoryHasCustomPath_ReturnsCategoryPath()
    {
        _categoryService.GetSavePathForCategory("tv").Returns("/storage/tv");
        _diskProvider.FolderExists("/storage/tv").Returns(true);

        var path = _storagePathService.GetCompletedDirectory("tv");

        path.Should().Be("/storage/tv");
    }

    [Test]
    public void GetCompletedDirectory_WhenCategoryPathEmpty_AppendsCategoryToDownloadDir()
    {
        _categoryService.GetSavePathForCategory("movies").Returns(string.Empty);
        _configService.DownloadDir.Returns("/storage/downloads");
        _diskProvider.FolderExists("/storage/downloads/movies").Returns(true);

        var path = _storagePathService.GetCompletedDirectory("movies");

        path.Should().Be(Path.Combine("/storage/downloads", "movies"));
    }

    [Test]
    public void GetWorkingPath_CombinesIncompleteDirAndTorrentName()
    {
        _configService.IncompleteDownloadDir.Returns("/downloads/incomplete");
        _diskProvider.FolderExists("/downloads/incomplete").Returns(true);

        var path = _storagePathService.GetWorkingPath("hash123", "Ubuntu.iso");

        path.Should().Be(Path.Combine("/downloads/incomplete", "Ubuntu.iso"));
    }

    [Test]
    public void GetFinalPath_CombinesCompletedDirAndTorrentName()
    {
        _categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        _diskProvider.FolderExists("/downloads/tv").Returns(true);

        var path = _storagePathService.GetFinalPath("tv", "Show.S01E01.mkv");

        path.Should().Be(Path.Combine("/downloads/tv", "Show.S01E01.mkv"));
    }

    [Test]
    public void MoveToCompleted_WhenSourceMatchesDestination_ReturnsTrueWithoutMoving()
    {
        _categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        _diskProvider.FolderExists("/downloads/tv").Returns(true);

        var success = _storagePathService.MoveToCompleted(
            Path.Combine("/downloads/tv", "File.mkv"),
            "tv",
            "File.mkv",
            out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(Path.Combine("/downloads/tv", "File.mkv"));
        _diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>());
        _diskProvider.DidNotReceive().MoveFolder(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void MoveToCompleted_WhenSourceIsFile_MovesFile()
    {
        var source = "/downloads/incomplete/File.mkv";
        _categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        _diskProvider.FolderExists("/downloads/tv").Returns(true);
        _diskProvider.FileExists(source).Returns(true);
        _diskProvider.FolderExists(source).Returns(false);

        var success = _storagePathService.MoveToCompleted(source, "tv", "File.mkv", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(Path.Combine("/downloads/tv", "File.mkv"));
        _diskProvider.Received(1).MoveFile(source, Path.Combine("/downloads/tv", "File.mkv"));
    }

    [Test]
    public void MoveToCompleted_WhenSourceIsFolder_MovesFolder()
    {
        var source = "/downloads/incomplete/Show.Season.1";
        _categoryService.GetSavePathForCategory("tv").Returns("/downloads/tv");
        _diskProvider.FolderExists("/downloads/tv").Returns(true);
        _diskProvider.FileExists(source).Returns(false);
        _diskProvider.FolderExists(source).Returns(true);

        var success = _storagePathService.MoveToCompleted(source, "tv", "Show.Season.1", out var finalDestination);

        success.Should().BeTrue();
        finalDestination.Should().Be(Path.Combine("/downloads/tv", "Show.Season.1"));
        _diskProvider.Received(1).MoveFolder(source, Path.Combine("/downloads/tv", "Show.Season.1"));
    }
}
