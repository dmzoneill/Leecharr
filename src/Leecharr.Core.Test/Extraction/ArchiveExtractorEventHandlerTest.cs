using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class ArchiveExtractorEventHandlerTest
{
    private IArchiveExtractorService _extractorService = null!;
    private ITorrentFileService _torrentFileService = null!;
    private IDiskProvider _diskProvider = null!;
    private IEventAggregator _eventAggregator = null!;
    private IConfigService _configService = null!;
    private ArchiveExtractorEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _extractorService = Substitute.For<IArchiveExtractorService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _diskProvider = Substitute.For<IDiskProvider>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _configService = Substitute.For<IConfigService>();

        _handler = new ArchiveExtractorEventHandler(
            _extractorService,
            _torrentFileService,
            _diskProvider,
            _eventAggregator,
            _configService);
    }

    [TestCase("movie.rar", false)]
    [TestCase("movie.part01.rar", false)]
    [TestCase("movie.part1.rar", false)]
    [TestCase("movie.r00", false)]
    [TestCase("movie.001", false)]
    [TestCase("movie.z01", false)]
    [TestCase("movie.zip", false)]
    [TestCase("movie.7z", false)]
    [TestCase("movie.part02.rar", true)]
    [TestCase("movie.part2.rar", true)]
    [TestCase("movie.part10.rar", true)]
    [TestCase("movie.r01", true)]
    [TestCase("movie.r02", true)]
    [TestCase("movie.002", true)]
    [TestCase("movie.003", true)]
    [TestCase("movie.z02", true)]
    [TestCase("movie.7z.002", true)]
    [TestCase("movie.zip.003", true)]
    public void IsSecondaryVolume_IdentifiesPrimaryAndSecondaryArchiveVolumes(string filePath, bool isSecondary)
    {
        ArchiveExtractorEventHandler.IsSecondaryVolume(filePath).Should().Be(isSecondary);
    }

    [Test]
    public void Handle_WhenAutoExtractDisabled_DoesNotExtract()
    {
        _configService.AutoExtractArchives.Returns(false);
        _configService.GetValueBoolean("AutoExtract", false).Returns(false);
        _configService.GetValueBoolean("AutoExtractEnabled", false).Returns(false);

        var torrent = new Torrent { Id = 1, Name = "Test.Movie", SavePath = "/downloads/Test.Movie" };
        var message = new TorrentDownloadCompletedEvent(torrent);

        _handler.Handle(message);

        _torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
        _extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Handle_WhenMessageOrTorrentIsNull_DoesNotThrowOrExtract()
    {
        _handler.Handle(null!);
        _handler.Handle(new TorrentDownloadCompletedEvent(null!));

        _torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    [Test]
    public async Task Handle_WhenAutoExtractEnabled_ExtractsOnlyPrimaryVolumeAndPublishesEvent()
    {
        _configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 10, Name = "Movie.MultiPart", SavePath = "/downloads/Movie.MultiPart" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 10, Path = "movie.part01.rar", Size = 50000000 },
            new() { Id = 2, TorrentId = 10, Path = "movie.part02.rar", Size = 50000000 },
            new() { Id = 3, TorrentId = 10, Path = "movie.part03.rar", Size = 50000000 },
            new() { Id = 4, TorrentId = 10, Path = "sample.nfo", Size = 1000 }
        };

        _torrentFileService.GetFiles(10).Returns(files);
        _extractorService.IsArchiveFile("movie.part01.rar").Returns(true);
        _extractorService.IsArchiveFile("movie.part02.rar").Returns(true);
        _extractorService.IsArchiveFile("movie.part03.rar").Returns(true);
        _extractorService.IsArchiveFile("sample.nfo").Returns(false);

        _diskProvider.FileExists(Arg.Any<string>()).Returns(true);
        _extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        _eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        _handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        // Exactly one extraction call for the primary volume part01
        await _extractorService.Received(1).ExtractArchiveAsync(
            Arg.Is<string>(p => p.EndsWith("movie.part01.rar")),
            Arg.Any<string>());

        _eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionCompletedEvent>(e =>
            e.Torrent.Id == 10 &&
            e.ArchivePath.EndsWith("movie.part01.rar")));
    }

    [Test]
    public void Handle_WhenExtractionFails_DoesNotPublishEvent()
    {
        _configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 20, Name = "Corrupt.Archive", SavePath = "/downloads/Corrupt.Archive" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 20, Path = "corrupt.zip", Size = 50000 }
        };

        _torrentFileService.GetFiles(20).Returns(files);
        _extractorService.IsArchiveFile("corrupt.zip").Returns(true);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(true);
        _extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(false));

        _handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        // Wait a short duration for the async task to execute
        Thread.Sleep(200);

        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
    }
}
