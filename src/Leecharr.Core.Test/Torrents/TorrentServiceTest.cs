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
    private ITorrentRepository _torrentRepository = null!;
    private ITorrentFileRepository _fileRepository = null!;
    private ICategoryService _categoryService = null!;
    private IMediaEnrichmentService _mediaEnrichmentService = null!;
    private IConfigService _configService = null!;
    private IDownloadEngine _downloadEngine = null!;
    private IEventAggregator _eventAggregator = null!;
    private ITrackerEntryRepository _trackerEntryRepository = null!;
    private TorrentService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _fileRepository = Substitute.For<ITorrentFileRepository>();
        _categoryService = Substitute.For<ICategoryService>();
        _mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        _configService = Substitute.For<IConfigService>();
        _downloadEngine = Substitute.For<IDownloadEngine>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();

        _categoryService.GetSavePathForCategory(Arg.Any<string>()).Returns("/downloads");
        _configService.DefaultCategory.Returns("default");
        _configService.AutoEnrichEnabled.Returns(false);

        _service = new TorrentService(
            _torrentRepository,
            _fileRepository,
            _categoryService,
            _mediaEnrichmentService,
            _configService,
            _downloadEngine,
            _eventAggregator,
            _trackerEntryRepository);
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
                new() { Path = "file3.txt", Size = 1800 }  // bytes 1700-3499: piece 1 to piece 3 (count 3)
            }
        };

        _torrentRepository.GetByInfoHash(parsed.InfoHash).Returns((Torrent)null!);
        _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 42;
            return t;
        });

        var insertedFiles = new List<TorrentFile>();
        _fileRepository.Insert(Arg.Do<TorrentFile>(f => insertedFiles.Add(f)));

        var result = await _service.AddFromParsedTorrentAsync(parsed, "movies", "/downloads/movies", false, Array.Empty<byte>());

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
