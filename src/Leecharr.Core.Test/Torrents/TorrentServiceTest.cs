// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentServiceTest
{
    private ITorrentRepository torrentRepository = null!;
    private ITorrentFileRepository fileRepository = null!;
    private ICategoryService categoryService = null!;
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private IEventAggregator eventAggregator = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private TorrentService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.fileRepository = Substitute.For<ITorrentFileRepository>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.configService = Substitute.For<IConfigService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();

        this.categoryService.GetSavePathForCategory(Arg.Any<string>()).Returns("/downloads");
        this.configService.DefaultCategory.Returns("default");
        this.configService.AutoEnrichEnabled.Returns(false);

        this.service = new TorrentService(
            this.torrentRepository,
            this.fileRepository,
            this.categoryService,
            this.mediaEnrichmentService,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            this.trackerEntryRepository);
    }

    [Test]
    public async Task AddFromParsedTorrentAsync_CalculatesContinuousBytePieceOffsets()
    {
        var parsed = new ParsedTorrent
        {
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Name = "MultiFileTorrent",
            PieceLength = 1000, // 1000 bytes per piece
            TotalSize = 3500,
            Files = new List<ParsedTorrentFile>
            {
                new() { Path = "file1.txt", Size = 500 },  // bytes 0-499: piece 0 (count 1)
                new() { Path = "file2.txt", Size = 1200 }, // bytes 500-1699: piece 0 to piece 1 (count 2)
                new() { Path = "file3.txt", Size = 1800 } // bytes 1700-3499: piece 1 to piece 3 (count 3)
            },
        };

        this.torrentRepository.GetByInfoHash(parsed.InfoHash).Returns((Torrent)null!);
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 42;
            return t;
        });

        var insertedFiles = new List<TorrentFile>();
        this.fileRepository.Insert(Arg.Do<TorrentFile>(f => insertedFiles.Add(f)));

        var result = await this.service.AddFromParsedTorrentAsync(parsed, "movies", "/downloads/movies", false, Array.Empty<byte>());

        result.Should().NotBeNull();
        result.Id.Should().Be(42);

        insertedFiles.Should().HaveCount(3);

        // File 1: size 500 -> startPiece 0, count 1
        insertedFiles[0].PieceOffset.Should().Be(0);
        insertedFiles[0].PieceCount.Should().Be(1);

        // File 2: size 1200 (bytes 500-1699) -> startPiece 0, endPiece 1, count 2
        insertedFiles[1].PieceOffset.Should().Be(0);
        insertedFiles[1].PieceCount.Should().Be(2);

        // File 3: size 1800 (bytes 1700-3499) -> startPiece 1, endPiece 3, count 3
        insertedFiles[2].PieceOffset.Should().Be(1);
        insertedFiles[2].PieceCount.Should().Be(3);
    }
}
