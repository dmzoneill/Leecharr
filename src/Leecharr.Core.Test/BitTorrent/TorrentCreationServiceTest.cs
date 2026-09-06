// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Creation;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;

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

        var service = new TorrentCreationService(new[] { this.testDir });
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

        var service = new TorrentCreationService(new[] { this.testDir });
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
        var service = new TorrentCreationService(new[] { "/non/existent/path" });
        var request = new TorrentCreationRequest
        {
            Path = "/non/existent/path/for/torrent.bin",
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [TestCase("/etc/cron.d/torrent_cron")]
    [TestCase("/etc/shadow")]
    [TestCase("/bin/sh")]
    [TestCase("/root/.ssh/authorized_keys")]
    [TestCase("/usr/bin/payload")]
    [TestCase("/boot/grub/grub.cfg")]
    public async Task CreateTorrentAsync_WhenOutputPathIsSensitiveSystemPath_ReturnsFailure(string sensitivePath)
    {
        var sourceFile = Path.Combine(this.testDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(new[] { this.testDir, sensitivePath });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = sensitivePath,
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("restricted system directory");
    }

    [TestCase("../../etc/cron.d/torrent_cron")]
    [TestCase("sub/../../etc/cron.d/torrent_cron")]
    [TestCase("../evil.torrent")]
    [TestCase("..")]
    [TestCase(".")]
    public async Task CreateTorrentAsync_WhenOutputPathContainsTraversal_ReturnsFailure(string traversalPath)
    {
        var sourceFile = Path.Combine(this.testDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(new[] { this.testDir });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = traversalPath,
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("directory traversal");
    }

    [Test]
    public async Task CreateTorrentAsync_WhenOutputPathContainsNullByte_ReturnsFailure()
    {
        var sourceFile = Path.Combine(this.testDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(new[] { this.testDir });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(this.testDir, "evil\0.torrent"),
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null bytes");
    }

    [TestCase("/etc/shadow")]
    [TestCase("/etc/passwd")]
    [TestCase("/root/.bashrc")]
    [TestCase("/bin/sh")]
    public async Task CreateTorrentAsync_WhenPathIsSensitiveSystemPath_ReturnsFailure(string sensitivePath)
    {
        var service = new TorrentCreationService(new[] { sensitivePath });
        var request = new TorrentCreationRequest
        {
            Path = sensitivePath,
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("restricted system directory");
    }

    [TestCase("../../etc/shadow")]
    [TestCase("../somefile")]
    [TestCase("..")]
    public async Task CreateTorrentAsync_WhenPathContainsTraversal_ReturnsFailure(string traversalPath)
    {
        var service = new TorrentCreationService(new[] { this.testDir });
        var request = new TorrentCreationRequest
        {
            Path = traversalPath,
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("directory traversal");
    }

    [Test]
    public async Task CreateTorrentAsync_WhenPathContainsNullByte_ReturnsFailure()
    {
        var service = new TorrentCreationService(new[] { this.testDir });
        var request = new TorrentCreationRequest
        {
            Path = "video\0.mp4",
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null bytes");
    }

    [Test]
    public async Task CreateTorrentAsync_WhenAllowedDirectoriesConfigured_RejectsPathsOutside()
    {
        var allowedDir = Path.Combine(this.testDir, "allowed");
        var outsideDir = Path.Combine(this.testDir, "outside");
        Directory.CreateDirectory(allowedDir);
        Directory.CreateDirectory(outsideDir);

        var sourceFile = Path.Combine(outsideDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(new[] { allowedDir });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(allowedDir, "out.torrent"),
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("allowed storage directories");
    }

    [Test]
    public async Task CreateTorrentAsync_WhenAllowedDirectoriesConfigured_AcceptsPathsInside()
    {
        var allowedDir = Path.Combine(this.testDir, "allowed");
        Directory.CreateDirectory(allowedDir);

        var sourceFile = Path.Combine(allowedDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(new[] { allowedDir });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(allowedDir, "out.torrent"),
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(result.OutputPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateTorrentAsync_WhenNoAllowedDirectoriesConfigured_FailsClosed()
    {
        var sourceFile = Path.Combine(this.testDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService();
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(this.testDir, "out.torrent"),
        };

        var result = await service.CreateTorrentAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("allowed storage directories");
    }

    [Test]
    public async Task CreateTorrentAsync_WhenCategoryAddedDynamically_RecognizesNewCategoryPathWithoutRestart()
    {
        var categoryService = Substitute.For<ICategoryService>();
        var configService = Substitute.For<IConfigService>();

        var categories = new List<Category>();
        categoryService.GetAll().Returns(_ => categories);

        var dynamicCatDir = Path.Combine(this.testDir, "dynamic_movies");
        Directory.CreateDirectory(dynamicCatDir);
        var sourceFile = Path.Combine(dynamicCatDir, "movie.mkv");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(categoryService, configService);
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(dynamicCatDir, "movie.torrent"),
        };

        // Initially no categories exist -> fails closed
        var result1 = await service.CreateTorrentAsync(request);
        result1.Should().NotBeNull();
        result1.Success.Should().BeFalse();
        result1.ErrorMessage.Should().Contain("allowed storage directories");

        // Dynamically add category at runtime
        categories.Add(new Category { Id = 1, Name = "Movies", SavePath = dynamicCatDir });

        // Same singleton service instance without restart now allows the path
        var result2 = await service.CreateTorrentAsync(request);
        result2.Should().NotBeNull();
        result2.Success.Should().BeTrue();
        File.Exists(result2.OutputPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateTorrentAsync_WhenDownloadDirUpdatedInConfig_RecognizesNewDownloadDirWithoutRestart()
    {
        var configService = Substitute.For<IConfigService>();
        var initialDir = Path.Combine(this.testDir, "initial");
        var updatedDir = Path.Combine(this.testDir, "updated");
        Directory.CreateDirectory(initialDir);
        Directory.CreateDirectory(updatedDir);

        var currentDownloadDir = initialDir;
        configService.DownloadDir.Returns(_ => currentDownloadDir);

        var sourceFile = Path.Combine(updatedDir, "sample.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(null, configService);
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(updatedDir, "sample.torrent"),
        };

        // Before update -> fails because updatedDir is not in allowed directories
        var result1 = await service.CreateTorrentAsync(request);
        result1.Should().NotBeNull();
        result1.Success.Should().BeFalse();
        result1.ErrorMessage.Should().Contain("allowed storage directories");

        // Update DownloadDir dynamically in settings
        currentDownloadDir = updatedDir;

        // Same singleton service instance immediately succeeds
        var result2 = await service.CreateTorrentAsync(request);
        result2.Should().NotBeNull();
        result2.Success.Should().BeTrue();
        File.Exists(result2.OutputPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateTorrentAsync_WhenStoragePathServiceConfigured_AllowsStoragePaths()
    {
        var storagePathService = Substitute.For<IStoragePathService>();
        var incDir = Path.Combine(this.testDir, "incomplete");
        Directory.CreateDirectory(incDir);
        storagePathService.GetIncompleteDirectory().Returns(incDir);

        var sourceFile = Path.Combine(incDir, "downloading.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[1024]);

        var service = new TorrentCreationService(null, null, storagePathService);
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            OutputPath = Path.Combine(incDir, "downloading.torrent"),
        };

        var result = await service.CreateTorrentAsync(request);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(result.OutputPath).Should().BeTrue();
    }

    [TestCase(1000)]
    [TestCase(30000)]
    [TestCase(8192)]
    [TestCase(134217728)]
    public async Task CreateTorrentAsync_WhenPieceLengthInvalid_ReturnsFailure(int pieceLength)
    {
        var sourceFile = Path.Combine(this.testDir, "video.mp4");
        await File.WriteAllBytesAsync(sourceFile, new byte[65536]);

        var service = new TorrentCreationService(new[] { this.testDir });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            PieceLength = pieceLength,
        };

        var result = await service.CreateTorrentAsync(request);
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Piece length must be a power of 2 between 16 KB");
    }

    [Test]
    public async Task CreateTorrentAsync_WithMultipleTrackers_CreatesSeparateTiersPerBEP12()
    {
        var sourceFile = Path.Combine(this.testDir, "data.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[32768]);

        var service = new TorrentCreationService(new[] { this.testDir });
        var request = new TorrentCreationRequest
        {
            Path = sourceFile,
            Trackers = new List<string>
            {
                "http://tracker1.com/announce",
                "http://tracker2.com/announce",
            },
        };

        var result = await service.CreateTorrentAsync(request);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var loaded = MonoTorrent.Torrent.Load(result.TorrentFileBytes);
        loaded.AnnounceUrls.Count.Should().Be(2);
        loaded.AnnounceUrls[0].Should().Contain("http://tracker1.com/announce");
        loaded.AnnounceUrls[1].Should().Contain("http://tracker2.com/announce");
    }
}
