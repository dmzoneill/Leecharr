// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentFileProgressEnricherTest
{
    [Test]
    public void Enrich_WhenTorrentIsNull_DoesNotThrow()
    {
        var files = new List<TorrentFile> { new() { Id = 1, Size = 100 } };
        Assert.DoesNotThrow(() => TorrentFileProgressEnricher.Enrich(null, files));
    }

    [Test]
    public void Enrich_WhenFilesIsNull_DoesNotThrow()
    {
        var torrent = new Torrent { Id = 1, Progress = 1.0 };
        Assert.DoesNotThrow(() => TorrentFileProgressEnricher.Enrich(torrent, null));
    }

    [TestCase(TorrentStatus.Completed)]
    [TestCase(TorrentStatus.Seeding)]
    public void Enrich_WhenCompletedOrSeeding_MarksAllFiles100PercentAndBytesCompletedEqualsSize(TorrentStatus status)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = status,
            Progress = 0.9, // Even if progress is < 1.0, status is complete/seeding
            TotalSize = 5000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 1, Path = "a.mp4", Size = 3000, Progress = 0.0, BytesCompleted = 0 },
            new() { Id = 2, Path = "b.srt", Size = 2000, Progress = 0.1, BytesCompleted = 200 },
        };

        TorrentFileProgressEnricher.Enrich(torrent, files);

        files[0].Progress.Should().Be(1.0);
        files[0].BytesCompleted.Should().Be(3000);
        files[1].Progress.Should().Be(1.0);
        files[1].BytesCompleted.Should().Be(2000);
    }

    [Test]
    public void Enrich_WhenProgressIs100Percent_MarksAllFiles100Percent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
            TotalSize = 1000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 1, Path = "test.iso", Size = 1000, Progress = 0.5, BytesCompleted = 500 },
        };

        TorrentFileProgressEnricher.Enrich(torrent, files);

        files[0].Progress.Should().Be(1.0);
        files[0].BytesCompleted.Should().Be(1000);
    }

    [Test]
    public void Enrich_WhenZeroByteFile_SetsProgressToOneAndBytesCompletedToZero()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 1, Path = ".empty", Size = 0, Progress = 0.0, BytesCompleted = 0 },
        };

        TorrentFileProgressEnricher.Enrich(torrent, files);

        files[0].Progress.Should().Be(1.0);
        files[0].BytesCompleted.Should().Be(0);
    }

    [Test]
    public void Enrich_WithPieceBitfield_CalculatesExactCompletedPieces()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            PieceLength = 256,
            PieceCount = 8,
            TotalSize = 2048,
        };

        var task = Substitute.For<IDownloadTask>();
        task.PieceBitfield.Returns(new[] { true, true, true, false, false, false, false, false });
        task.PieceLength.Returns(256);

        var files = new List<TorrentFile>
        {
            // File 1 spans pieces 0, 1 (2 pieces, both complete -> 100%)
            new() { Id = 1, Path = "file1.dat", Size = 512, PieceOffset = 0, PieceCount = 2 },
            // File 2 spans pieces 2, 3 (2 pieces, piece 2 complete, piece 3 not -> 256 bytes, 50%)
            new() { Id = 2, Path = "file2.dat", Size = 512, PieceOffset = 2, PieceCount = 2 },
            // File 3 spans pieces 4, 5, 6, 7 (4 pieces, none complete -> 0%)
            new() { Id = 3, Path = "file3.dat", Size = 1024, PieceOffset = 4, PieceCount = 4 },
        };

        TorrentFileProgressEnricher.Enrich(torrent, files, task);

        files[0].Progress.Should().Be(1.0);
        files[0].BytesCompleted.Should().Be(512);

        files[1].Progress.Should().Be(0.5);
        files[1].BytesCompleted.Should().Be(256);

        files[2].Progress.Should().Be(0.0);
        files[2].BytesCompleted.Should().Be(0);
    }

    [Test]
    public void Enrich_WithoutPieceBitfield_ProratesFromTorrentProgress()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Progress = 0.75,
            TotalSize = 4000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 1, Path = "a.bin", Size = 2000 },
            new() { Id = 2, Path = "b.bin", Size = 2000 },
        };

        TorrentFileProgressEnricher.Enrich(torrent, files, null);

        files[0].Progress.Should().Be(0.75);
        files[0].BytesCompleted.Should().Be(1500);

        files[1].Progress.Should().Be(0.75);
        files[1].BytesCompleted.Should().Be(1500);
    }

    [Test]
    public void Enrich_WithoutPieceBitfield_WhenFilePriorityIsZero_SetsZeroProgress()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 4000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 1, Path = "a.bin", Size = 2000, Priority = 3 },
            new() { Id = 2, Path = "b.bin", Size = 2000, Priority = 0 }, // Skipped
        };

        TorrentFileProgressEnricher.Enrich(torrent, files, null);

        files[0].Progress.Should().Be(0.5);
        files[0].BytesCompleted.Should().Be(1000);

        files[1].Progress.Should().Be(0.0);
        files[1].BytesCompleted.Should().Be(0);
    }

    [Test]
    public void Enrich_WithPieceBitfield_CalculatesExactBoundaryOverlapAcrossSharedPieces()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            PieceLength = 256,
            PieceCount = 4,
            TotalSize = 1024,
        };

        var task = Substitute.For<IDownloadTask>();
        // Piece 0: [0..256), Piece 1: [256..512), Piece 2: [512..768), Piece 3: [768..1024)
        // Pieces 0 and 1 are downloaded
        task.PieceBitfield.Returns(new[] { true, true, false, false });
        task.PieceLength.Returns(256);

        var files = new List<TorrentFile>
        {
            // File 1: [0..300) -> spans Piece 0 [0..256) (256 bytes) and Piece 1 [256..300) (44 bytes). Both pieces downloaded -> 300 bytes (100%)
            new() { Id = 1, Path = "file1.dat", Size = 300, ByteOffset = 0 },
            // File 2: [300..600) -> spans Piece 1 [300..512) (212 bytes, downloaded) and Piece 2 [512..600) (88 bytes, not downloaded). Total completed = 212 bytes
            new() { Id = 2, Path = "file2.dat", Size = 300, ByteOffset = 300 },
            // File 3: [600..1024) -> spans Piece 2 [600..768) and Piece 3 [768..1024). Neither downloaded -> 0 bytes (0%)
            new() { Id = 3, Path = "file3.dat", Size = 424, ByteOffset = 600 },
        };

        TorrentFileProgressEnricher.Enrich(torrent, files, task);

        files[0].Progress.Should().Be(1.0);
        files[0].BytesCompleted.Should().Be(300);

        files[1].BytesCompleted.Should().Be(212);
        files[1].Progress.Should().BeApproximately(212.0 / 300.0, 0.0001);

        files[2].Progress.Should().Be(0.0);
        files[2].BytesCompleted.Should().Be(0);
    }
}
