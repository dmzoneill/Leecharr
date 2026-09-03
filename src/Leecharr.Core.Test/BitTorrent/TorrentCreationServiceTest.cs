// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Creation;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class TorrentCreationServiceTest
{
    private string testDir;

    [SetUp]
    public void SetUp()
    {
        this.testDir = Path.Combine(Path.GetTempPath(), "leecharr_creation_test_" + Guid.NewGuid());
        Directory.CreateDirectory(this.testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.testDir))
        {
            try
            {
                Directory.Delete(this.testDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task CreateTorrentAsync_WithSingleFile_CreatesValidTorrent()
    {
        var sourceFile = Path.Combine(this.testDir, "video.mp4");
        var dummyData = new byte[65536];
        new Random(42).NextBytes(dummyData);
        await File.WriteAllBytesAsync(sourceFile, dummyData);

        var service = new TorrentCreationService();
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            Name = "custom_video",
            Comment = "Leecharr Test Torrent",
            CreatedBy = "Leecharr CI",
            IsPrivate = true,
            PieceLength = 16384,
            Trackers = new List<string> { "http://tracker.example.com/announce" },
            WebSeeds = new List<string> { "http://seed.example.com/video.mp4" },
            OutputPath = Path.Combine(this.testDir, "output.torrent"),
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.InfoHash.Should().NotBeNullOrWhiteSpace();
        result.TotalSize.Should().Be(65536);
        result.PieceLength.Should().Be(16384);
        result.PieceCount.Should().Be(4);
        File.Exists(result.OutputPath).Should().BeTrue();

        // Verify the created torrent parses properly with MonoTorrent
        var loaded = MonoTorrent.Torrent.Load(result.TorrentFileBytes);
        loaded.Name.Should().Be("custom_video");
        loaded.Comment.Should().Be("Leecharr Test Torrent");
        loaded.CreatedBy.Should().Be("Leecharr CI");
        loaded.IsPrivate.Should().BeTrue();
        loaded.Size.Should().Be(65536);
        loaded.AnnounceUrls.Should().Contain(l => l.Contains("http://tracker.example.com/announce"));
        loaded.HttpSeeds.Should().Contain(new Uri("http://seed.example.com/video.mp4"));
    }

    [Test]
    public async Task CreateTorrentAsync_WithDirectory_CreatesMultiFileTorrent()
    {
        var subDir = Path.Combine(this.testDir, "album");
        Directory.CreateDirectory(subDir);
        await File.WriteAllBytesAsync(Path.Combine(subDir, "track1.flac"), new byte[32768]);
        await File.WriteAllBytesAsync(Path.Combine(subDir, "track2.flac"), new byte[32768]);

        var service = new TorrentCreationService();
        var request = new TorrentCreationRequest
        {
            Path = subDir,
            Trackers = new List<string> { "udp://tracker.openbittorrent.com:80/announce" },
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.TotalSize.Should().Be(65536);

        var loaded = MonoTorrent.Torrent.Load(result.TorrentFileBytes);
        loaded.Files.Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTorrentAsync_WithNonExistentPath_ReturnsFailure()
    {
        var service = new TorrentCreationService();
        var request = new TorrentCreationRequest
        {
            Path = "/non/existent/path/for/torrent.bin",
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not exist");
    }
}
