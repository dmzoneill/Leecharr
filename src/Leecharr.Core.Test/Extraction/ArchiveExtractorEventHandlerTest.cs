// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
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
    private IArchiveExtractorService extractorService = null!;
    private ITorrentFileService torrentFileService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;
    private IConfigService configService = null!;
    private ArchiveExtractorEventHandler handler = null!;

    [SetUp]
    public void SetUp()
    {
        this.extractorService = Substitute.For<IArchiveExtractorService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.configService = Substitute.For<IConfigService>();

        this.handler = new ArchiveExtractorEventHandler(
            this.extractorService,
            this.torrentFileService,
            this.diskProvider,
            this.eventAggregator,
            this.configService);
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
        this.configService.AutoExtractArchives.Returns(false);
        this.configService.GetValueBoolean("AutoExtract", false).Returns(false);
        this.configService.GetValueBoolean("AutoExtractEnabled", false).Returns(false);

        var torrent = new Torrent { Id = 1, Name = "Test.Movie", SavePath = "/downloads/Test.Movie" };
        var message = new TorrentDownloadCompletedEvent(torrent);

        this.handler.Handle(message);

        this.torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Handle_WhenMessageOrTorrentIsNull_DoesNotThrowOrExtract()
    {
        this.handler.Handle(null!);
        this.handler.Handle(new TorrentDownloadCompletedEvent(null!));

        this.torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    [Test]
    public async Task Handle_WhenAutoExtractEnabled_ExtractsOnlyPrimaryVolumeAndPublishesEvent()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 10, Name = "Movie.MultiPart", SavePath = "/downloads/Movie.MultiPart" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 10, Path = "movie.part01.rar", Size = 50000000 },
            new() { Id = 2, TorrentId = 10, Path = "movie.part02.rar", Size = 50000000 },
            new() { Id = 3, TorrentId = 10, Path = "movie.part03.rar", Size = 50000000 },
            new() { Id = 4, TorrentId = 10, Path = "sample.nfo", Size = 1000 },
        };

        this.torrentFileService.GetFiles(10).Returns(files);
        this.extractorService.IsArchiveFile("movie.part01.rar").Returns(true);
        this.extractorService.IsArchiveFile("movie.part02.rar").Returns(true);
        this.extractorService.IsArchiveFile("movie.part03.rar").Returns(true);
        this.extractorService.IsArchiveFile("sample.nfo").Returns(false);

        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);
        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        // Exactly one extraction call for the primary volume part01
        await this.extractorService.Received(1).ExtractArchiveAsync(
            Arg.Is<string>(p => p.EndsWith("movie.part01.rar")),
            Arg.Any<string>());

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionCompletedEvent>(e =>
            e.Torrent.Id == 10 &&
            e.ArchivePath.EndsWith("movie.part01.rar")));
    }

    [Test]
    public void Handle_WhenExtractionFails_DoesNotPublishEvent()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 20, Name = "Corrupt.Archive", SavePath = "/downloads/Corrupt.Archive" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 20, Path = "corrupt.zip", Size = 50000 },
        };

        this.torrentFileService.GetFiles(20).Returns(files);
        this.extractorService.IsArchiveFile("corrupt.zip").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);
        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(false));

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        // Wait a short duration for the async task to execute
        Thread.Sleep(200);

        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
    }

    [Test]
    public async Task Handle_WhenSingleFileTorrent_ExtractsToParentDirectoryAndPublishesEvent()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var singleFilePath = Path.Combine(Path.GetTempPath(), "downloads", "single-movie.rar");
        var parentDir = Path.GetDirectoryName(Path.GetFullPath(singleFilePath))!;

        var torrent = new Torrent { Id = 30, Name = "SingleFile.Movie", SavePath = singleFilePath };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 30, Path = "single-movie.rar", Size = 50000000 },
        };

        this.torrentFileService.GetFiles(30).Returns(files);
        this.extractorService.IsArchiveFile("single-movie.rar").Returns(true);
        this.diskProvider.FileExists(singleFilePath).Returns(true);
        this.extractorService.ExtractArchiveAsync(singleFilePath, parentDir).Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        await this.extractorService.Received(1).ExtractArchiveAsync(singleFilePath, parentDir);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionCompletedEvent>(e =>
            e.Torrent.Id == 30 &&
            e.ArchivePath == singleFilePath &&
            e.DestinationDirectory == parentDir));
    }

    [Test]
    public void Handle_WhenPathTraversalOutsideRootDir_RefusesExtraction()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var savePath = Path.Combine(Path.GetTempPath(), "downloads", "my-folder");
        var torrent = new Torrent { Id = 40, Name = "Traversal.Movie", SavePath = savePath };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 40, Path = "../../../etc/evil.rar", Size = 50000 },
        };

        this.torrentFileService.GetFiles(40).Returns(files);
        this.extractorService.IsArchiveFile("../../../etc/evil.rar").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        Thread.Sleep(200);

        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
    }
}
