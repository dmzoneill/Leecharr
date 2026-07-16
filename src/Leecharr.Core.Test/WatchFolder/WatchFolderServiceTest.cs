using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace Leecharr.Core.Test.WatchFolder;

[TestFixture]
public class WatchFolderServiceTest
{
    private IConfigService _configService = null!;
    private ITorrentService _torrentService = null!;
    private ITorrentFileParser _torrentFileParser = null!;
    private ICategoryService _categoryService = null!;
    private IDiskProvider _diskProvider = null!;
    private WatchFolderService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _categoryService = Substitute.For<ICategoryService>();
        _diskProvider = Substitute.For<IDiskProvider>();

        _configService.DefaultCategory.Returns("default");
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns("/watch");
        _configService.WatchFolderAutoStartTorrents.Returns(true);
        _configService.WatchFolderDeleteAddedTorrents.Returns(true);

        _service = new WatchFolderService(
            _configService,
            _torrentService,
            _torrentFileParser,
            _categoryService,
            _diskProvider);
    }

    [TestCase("Severance.S02E01.1080p.WEB-DL.x265", "tv")]
    [TestCase("Breaking.Bad.Season.1.Complete", "tv")]
    [TestCase("[SubsPlease] Frieren - 28 (1080p) [12345678].mkv", "anime")]
    [TestCase("Dune.Part.Two.2024.2160p.UHD.BluRay.x265-FLUX", "movies")]
    [TestCase("Oppenheimer.2023.1080p.Remux", "movies")]
    [TestCase("Pink.Floyd-The.Dark.Side.Of.The.Moon.1973.FLAC.Lossless", "music")]
    [TestCase("Random.Document.v1.0.pdf", "default")]
    public void MatchCategoryFromReleaseName_ClassifiesCorrectly(string releaseName, string expectedCategory)
    {
        var category = _service.MatchCategoryFromReleaseName(releaseName);
        category.Should().Be(expectedCategory);
    }

    [Test]
    public async Task ScanWatchFolderAsync_WhenDisabled_DoesNotScan()
    {
        _configService.WatchFolderEnabled.Returns(false);

        await _service.ScanWatchFolderAsync();

        _diskProvider.DidNotReceive().GetFiles(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public async Task ScanWatchFolderAsync_WhenFolderDoesNotExist_DoesNotScan()
    {
        _diskProvider.FolderExists("/watch").Returns(false);

        await _service.ScanWatchFolderAsync();

        _diskProvider.DidNotReceive().GetFiles(Arg.Any<string>(), Arg.Any<bool>());
    }
}
