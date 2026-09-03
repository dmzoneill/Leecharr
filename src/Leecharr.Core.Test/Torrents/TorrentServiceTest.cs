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

    [Test]
    public async Task RenameFileAsync_WhenEngineSucceeds_UpdatesDatabaseRecord()
    {
        var file = new TorrentFile { Id = 10, TorrentId = 42, Path = "folder/old_video.mkv" };
        this.fileRepository.GetByTorrentId(42).Returns(new List<TorrentFile> { file });
        this.downloadEngine.RenameFileAsync(42, "folder/old_video.mkv", "folder/new_video.mkv").Returns(true);

        var result = await this.service.RenameFileAsync(42, "folder/old_video.mkv", "folder/new_video.mkv");

        result.Should().BeTrue();
        file.Path.Should().Be("folder/new_video.mkv");
        this.fileRepository.Received(1).Update(file);
    }

    [Test]
    public async Task RenameFolderAsync_WhenEngineSucceeds_UpdatesAllSubpathRecords()
    {
        var file1 = new TorrentFile { Id = 10, TorrentId = 42, Path = "Season 1/Episode 1.mkv" };
        var file2 = new TorrentFile { Id = 11, TorrentId = 42, Path = "Season 1/Episode 2.mkv" };
        var file3 = new TorrentFile { Id = 12, TorrentId = 42, Path = "Other/Bonus.mkv" };

        this.fileRepository.GetByTorrentId(42).Returns(new List<TorrentFile> { file1, file2, file3 });
        this.downloadEngine.RenameFolderAsync(42, "Season 1", "S01").Returns(true);

        var result = await this.service.RenameFolderAsync(42, "Season 1", "S01");

        result.Should().BeTrue();
        file1.Path.Should().Be("S01/Episode 1.mkv");
        file2.Path.Should().Be("S01/Episode 2.mkv");
        file3.Path.Should().Be("Other/Bonus.mkv");
        this.fileRepository.Received(1).Update(file1);
        this.fileRepository.Received(1).Update(file2);
        this.fileRepository.DidNotReceive().Update(file3);
    }

    [Test]
    public async Task SetSuperSeedingAsync_UpdatesTorrentAndEngine()
    {
        var torrent = new Torrent { Id = 42, Name = "test", InitialSeeding = false };
        this.torrentRepository.Get(42).Returns(torrent);

        await this.service.SetSuperSeedingAsync(42, true);

        torrent.InitialSeeding.Should().BeTrue();
        this.torrentRepository.Received(1).Update(torrent);
        await this.downloadEngine.Received(1).SetSuperSeedingAsync(42, true);
    }
}
