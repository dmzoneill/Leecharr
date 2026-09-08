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
    [TestCase("movie.r00", true)]
    [TestCase("movie.001", false)]
    [TestCase("movie.7z.001", false)]
    [TestCase("movie.rar.001", false)]
    [TestCase("movie.zip.001", false)]
    [TestCase("movie.z01", true)]
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
    [TestCase("movie.z99", true)]
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
        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
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

        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return !path.Contains(".leecharr_extracted");
        });
        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        // Exactly one extraction call for the primary volume part01
        await this.extractorService.Received(1).ExtractArchiveAsync(
            Arg.Is<string>(p => p.EndsWith("movie.part01.rar")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionCompletedEvent>(e =>
            e.Torrent.Id == 10 &&
            e.ArchivePath.EndsWith("movie.part01.rar")));

        // Verifies receipt marker was written
        this.diskProvider.Received().WriteAllText(
            Arg.Is<string>(p => p.Contains(".leecharr_extracted_movie.part01.rar")),
            Arg.Any<string>());
    }

    [Test]
    public void Handle_WhenExtractionFails_PublishesArchiveExtractionFailedEvent()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 20, Name = "Corrupt.Archive", SavePath = "/downloads/Corrupt.Archive" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 20, Path = "corrupt.zip", Size = 50000 },
        };

        this.torrentFileService.GetFiles(20).Returns(files);
        this.extractorService.IsArchiveFile("corrupt.zip").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return !path.Contains(".leecharr_extracted");
        });
        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionFailedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionFailedEvent>(e =>
            e.Torrent.Id == 20 &&
            e.ArchivePath.EndsWith("corrupt.zip")));
    }

    [Test]
    public void Handle_WhenDiskSpaceInsufficient_AbortsExtractionAndPublishesFailedEvent()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 25, Name = "Large.Archive", SavePath = "/downloads/Large.Archive" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 25, Path = "large.zip", Size = 100_000_000 },
        };

        this.torrentFileService.GetFiles(25).Returns(files);
        this.extractorService.IsArchiveFile("large.zip").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return !path.Contains(".leecharr_extracted");
        });
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(140_000_000L);

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionFailedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionFailedEvent>(e =>
            e.Torrent.Id == 25 &&
            e.ErrorMessage.Contains("Insufficient free disk space")));
    }

    [Test]
    public async Task Handle_WhenDiskSpaceSufficient_ProceedsWithExtraction()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 26, Name = "Sufficient.Space.Archive", SavePath = "/downloads/Sufficient.Space.Archive" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 26, Path = "archive.zip", Size = 100_000_000 },
        };

        this.torrentFileService.GetFiles(26).Returns(files);
        this.extractorService.IsArchiveFile("archive.zip").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return !path.Contains(".leecharr_extracted");
        });
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(200_000_000L); // 200 MB > 150 MB required
        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        await this.extractorService.Received(1).ExtractArchiveAsync(
            Arg.Is<string>(p => p.EndsWith("archive.zip")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionCompletedEvent>(e => e.Torrent.Id == 26));
    }

    [Test]
    public async Task Handle_WhenAvailableDiskSpaceIsNull_ProceedsWithExtraction()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 27, Name = "Unknown.Space.Archive", SavePath = "/downloads/Unknown.Space.Archive" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 27, Path = "archive.zip", Size = 100_000_000 },
        };

        this.torrentFileService.GetFiles(27).Returns(files);
        this.extractorService.IsArchiveFile("archive.zip").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return !path.Contains(".leecharr_extracted");
        });
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns((long?)null); // Space cannot be determined
        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        await this.extractorService.Received(1).ExtractArchiveAsync(
            Arg.Is<string>(p => p.EndsWith("archive.zip")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void Handle_WhenMultiPartArchiveTotalSizeExceedsDiskSpace_AbortsExtraction()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 28, Name = "MultiPart.Large", SavePath = "/downloads/MultiPart.Large" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 28, Path = "large.part01.rar", Size = 50_000_000 },
            new() { Id = 2, TorrentId = 28, Path = "large.part02.rar", Size = 50_000_000 },
            new() { Id = 3, TorrentId = 28, Path = "large.part03.rar", Size = 50_000_000 },
        };

        this.torrentFileService.GetFiles(28).Returns(files);
        this.extractorService.IsArchiveFile("large.part01.rar").Returns(true);
        this.extractorService.IsArchiveFile("large.part02.rar").Returns(true);
        this.extractorService.IsArchiveFile("large.part03.rar").Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call => !call.Arg<string>().Contains(".leecharr_extracted"));
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(200_000_000L); // 3 * 50 MB = 150 MB * 1.5 = 225 MB required > 200 MB available

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionFailedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionFailedEvent>(e =>
            e.Torrent.Id == 28 &&
            e.ErrorMessage.Contains("Insufficient free disk space")));
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
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return !path.Contains(".leecharr_extracted");
        });
        this.extractorService.ExtractArchiveAsync(singleFilePath, parentDir, Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ => signal.Set());

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var received = signal.Wait(TimeSpan.FromSeconds(3));
        received.Should().BeTrue();

        await this.extractorService.Received(1).ExtractArchiveAsync(singleFilePath, parentDir, Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
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

        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
    }

    [Test]
    public void Handle_WhenArchiveAlreadyExtractedByReceiptFile_SkipsReExtraction()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 50, Name = "Already.Extracted", SavePath = "/downloads/Already.Extracted" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 50, Path = "movie.rar", Size = 50000000 },
        };

        this.torrentFileService.GetFiles(50).Returns(files);
        this.extractorService.IsArchiveFile("movie.rar").Returns(true);

        // Simulate that archive file exists AND .leecharr_extracted_movie.rar receipt marker exists
        this.diskProvider.FileExists(Arg.Is<string>(p => p.EndsWith("movie.rar"))).Returns(true);
        this.diskProvider.FileExists(Arg.Is<string>(p => p.EndsWith(".leecharr_extracted_movie.rar"))).Returns(true);

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        Thread.Sleep(200);

        // Verify extraction was skipped idempotently
        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
    }

    [Test]
    public void Handle_WhenArchiveAlreadyInGlobalReceiptFile_SkipsReExtraction()
    {
        this.configService.AutoExtractArchives.Returns(true);

        var torrent = new Torrent { Id = 51, Name = "Global.Receipt.Torrent", SavePath = "/downloads/Global.Receipt.Torrent" };
        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 51, Path = "video.zip", Size = 50000000 },
        };

        this.torrentFileService.GetFiles(51).Returns(files);
        this.extractorService.IsArchiveFile("video.zip").Returns(true);

        // Specific receipt does not exist, but global .leecharr_extracted exists and contains "video.zip"
        this.diskProvider.FileExists(Arg.Is<string>(p => p.EndsWith("video.zip"))).Returns(true);
        this.diskProvider.FileExists(Arg.Is<string>(p => p.EndsWith(".leecharr_extracted_video.zip"))).Returns(false);
        this.diskProvider.FileExists(Arg.Is<string>(p => p.EndsWith(".leecharr_extracted"))).Returns(true);
        this.diskProvider.ReadAllText(Arg.Is<string>(p => p.EndsWith(".leecharr_extracted"))).Returns("other.rar\nvideo.zip\n");

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        Thread.Sleep(200);

        this.extractorService.DidNotReceive().ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>());
    }

    [Test]
    public void Constructor_SetsConcurrencyLimit_FromConfigServiceOrParameter()
    {
        this.configService.MaxConcurrentExtractions.Returns(4);
        var customHandler = new ArchiveExtractorEventHandler(
            this.extractorService,
            this.torrentFileService,
            this.diskProvider,
            this.eventAggregator,
            this.configService);

        customHandler.ConcurrencyLimit.Should().Be(4);
        customHandler.ConcurrencySemaphore.CurrentCount.Should().Be(4);

        var explicitHandler = new ArchiveExtractorEventHandler(
            this.extractorService,
            this.torrentFileService,
            this.diskProvider,
            this.eventAggregator,
            this.configService,
            maxConcurrentExtractions: 1);

        explicitHandler.ConcurrencyLimit.Should().Be(1);
        explicitHandler.ConcurrencySemaphore.CurrentCount.Should().Be(1);
    }

    [Test]
    public void Handle_ConcurrencyQueue_ThrottlesConcurrentExtractions()
    {
        this.configService.AutoExtractArchives.Returns(true);
        var customHandler = new ArchiveExtractorEventHandler(
            this.extractorService,
            this.torrentFileService,
            this.diskProvider,
            this.eventAggregator,
            this.configService,
            maxConcurrentExtractions: 1);

        var activeExtractions = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();

        this.extractorService.IsArchiveFile(Arg.Any<string>()).Returns(true);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(call => !call.Arg<string>().Contains(".leecharr_extracted"));

        this.extractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                lock (lockObj)
                {
                    activeExtractions++;
                    if (activeExtractions > maxObservedConcurrency)
                    {
                        maxObservedConcurrency = activeExtractions;
                    }
                }

                await Task.Delay(100);

                lock (lockObj)
                {
                    activeExtractions--;
                }

                return true;
            });

        var torrent1 = new Torrent { Id = 101, Name = "Torrent.1", SavePath = "/downloads/Torrent.1" };
        var torrent2 = new Torrent { Id = 102, Name = "Torrent.2", SavePath = "/downloads/Torrent.2" };

        this.torrentFileService.GetFiles(101).Returns(new List<TorrentFile> { new() { Id = 1, TorrentId = 101, Path = "file1.zip", Size = 1000 } });
        this.torrentFileService.GetFiles(102).Returns(new List<TorrentFile> { new() { Id = 2, TorrentId = 102, Path = "file2.zip", Size = 1000 } });

        var completedCount = 0;
        var signal = new ManualResetEventSlim(false);
        this.eventAggregator.When(e => e.PublishEvent(Arg.Any<ArchiveExtractionCompletedEvent>())).Do(_ =>
        {
            if (Interlocked.Increment(ref completedCount) == 2)
            {
                signal.Set();
            }
        });

        customHandler.Handle(new TorrentDownloadCompletedEvent(torrent1));
        customHandler.Handle(new TorrentDownloadCompletedEvent(torrent2));

        var finished = signal.Wait(TimeSpan.FromSeconds(5));
        finished.Should().BeTrue();
        maxObservedConcurrency.Should().Be(1);
    }
}
