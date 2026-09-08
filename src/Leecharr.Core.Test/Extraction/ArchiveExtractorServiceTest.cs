// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
public class ArchiveExtractorServiceTest
{
    private IDiskProvider diskProvider = null!;
    private ArchiveExtractorService service = null!;
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "leecharr_archive_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);

        this.diskProvider = Substitute.For<IDiskProvider>();
        this.service = new ArchiveExtractorService(this.diskProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Archive Format Detection

    [TestCase("sample.zip", true)]
    [TestCase("sample.rar", true)]
    [TestCase("sample.7z", true)]
    [TestCase("sample.tar", true)]
    [TestCase("sample.gz", true)]
    [TestCase("sample.tgz", true)]
    [TestCase("sample.bz2", true)]
    [TestCase("sample.tbz2", true)]
    [TestCase("sample.xz", true)]
    [TestCase("sample.txz", true)]
    [TestCase("sample.lz", true)]
    [TestCase("sample.z", true)]
    [TestCase("sample.001", true)]
    [TestCase("sample.7z.001", true)]
    [TestCase("sample.rar.001", true)]
    [TestCase("sample.zip.001", true)]
    [TestCase("sample.mkv", false)]
    [TestCase("sample.mp4", false)]
    [TestCase("sample.avi", false)]
    [TestCase("sample.flac", false)]
    [TestCase("sample.mp3", false)]
    [TestCase("sample.txt", false)]
    [TestCase("sample.nfo", false)]
    [TestCase("sample.exe", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsArchiveFile_DetectsSupportedAndUnsupportedExtensions(string fileName, bool expected)
    {
        this.service.IsArchiveFile(fileName).Should().Be(expected);
    }

    #endregion

    #region Multi-Volume Archive Detection

    [TestCase("movie.part01.rar", false)]
    [TestCase("movie.part1.rar", false)]
    [TestCase("movie.rar", false)]
    [TestCase("movie.r00", true)]
    [TestCase("movie.001", false)]
    [TestCase("movie.7z.001", false)]
    [TestCase("movie.rar.001", false)]
    [TestCase("movie.zip.001", false)]
    [TestCase("movie.z01", true)]
    [TestCase("movie.z99", true)]
    [TestCase("movie.part01.7z", false)]
    [TestCase("movie.part01.zip", false)]
    [TestCase("movie.part02.rar", true)]
    [TestCase("movie.part03.rar", true)]
    [TestCase("movie.part10.rar", true)]
    [TestCase("movie.r01", true)]
    [TestCase("movie.r02", true)]
    [TestCase("movie.002", true)]
    [TestCase("movie.003", true)]
    [TestCase("movie.z02", true)]
    [TestCase("movie.part02.7z", true)]
    [TestCase("movie.part02.zip", true)]
    public void IsSecondaryVolume_DifferentiatesPrimaryFromSecondaryParts(string filePath, bool isSecondary)
    {
        ArchiveExtractorEventHandler.IsSecondaryVolume(filePath).Should().Be(isSecondary);
    }

    #endregion

    #region Configuration Check (Auto-Extraction Disabled By Default)

    [Test]
    public void AutoExtraction_DisabledByDefault_InConfiguration()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentFileService = Substitute.For<ITorrentFileService>();
        var eventAggregator = Substitute.For<IEventAggregator>();

        configService.AutoExtractArchives.Returns(false);
        configService.GetValueBoolean("AutoExtract", false).Returns(false);
        configService.GetValueBoolean("AutoExtractEnabled", false).Returns(false);

        var handler = new ArchiveExtractorEventHandler(
            this.service,
            torrentFileService,
            this.diskProvider,
            eventAggregator,
            configService);

        var torrent = new Torrent { Id = 1, Name = "Release.With.Archive", SavePath = "/downloads" };
        handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        torrentFileService.DidNotReceive().GetFiles(Arg.Any<int>());
    }

    #endregion

    #region Extraction and ZipSlip Traversal Prevention

    [Test]
    public async Task ExtractArchiveAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        this.diskProvider.FileExists("/non/existent/file.zip").Returns(false);

        var result = await this.service.ExtractArchiveAsync("/non/existent/file.zip");
        result.Should().BeFalse();
    }

    [Test]
    public async Task SharpCompressExtractor_ExtractsValidZipArchive()
    {
        var zipPath = Path.Combine(this.tempDirectory, "valid.zip");
        var outputDir = Path.Combine(this.tempDirectory, "output");

        // Create a real zip archive
        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("inner_folder/content.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("Extracted content successfully");
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);

        var success = await provider.ExtractAsync(zipPath, outputDir);
        success.Should().BeTrue();

        var extractedFile = Path.Combine(outputDir, "inner_folder", "content.txt");
        File.Exists(extractedFile).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedFile)).Should().Be("Extracted content successfully");
    }

    [Test]
    public async Task SharpCompressExtractor_ExtractsArchive_WithPasswordParameter()
    {
        var zipPath = Path.Combine(this.tempDirectory, "valid_with_pass.zip");
        var outputDir = Path.Combine(this.tempDirectory, "pass_output");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("inner/secret.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("Encrypted content successfully decrypted");
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);

        var success = await provider.ExtractAsync(zipPath, outputDir, "SecretPassword123");
        success.Should().BeTrue();

        var extractedFile = Path.Combine(outputDir, "inner", "secret.txt");
        File.Exists(extractedFile).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedFile)).Should().Be("Encrypted content successfully decrypted");
    }

    [Test]
    public async Task SharpCompressExtractor_ExtractsArchive_WithCandidatePasswordList()
    {
        var zipPath = Path.Combine(this.tempDirectory, "valid_candidates.zip");
        var outputDir = Path.Combine(this.tempDirectory, "candidates_output");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("data.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("Decrypted via candidate list");
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);
        var candidateList = new[] { "wrong_password_1", "TargetPassword456", "wrong_password_2" };

        var success = await provider.ExtractAsync(zipPath, outputDir, null, candidateList);
        success.Should().BeTrue();

        var extractedFile = Path.Combine(outputDir, "data.txt");
        File.Exists(extractedFile).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedFile)).Should().Be("Decrypted via candidate list");
    }

    [Test]
    public async Task SharpCompressExtractor_ProtectsAgainstDirectoryTraversal_ZipSlip()
    {
        var zipPath = Path.Combine(this.tempDirectory, "malicious_zipslip.zip");
        var outputDir = Path.Combine(this.tempDirectory, "safe_target");
        Directory.CreateDirectory(outputDir);

        var outsideTarget = Path.Combine(this.tempDirectory, "outside_target.txt");

        // Construct zip containing a path traversal entry attempting to write outside target
        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var maliciousEntry = archive.CreateEntry("../../outside_target.txt");
            using var entryStream = maliciousEntry.Open();
            using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("Malicious payload outside destination folder");
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);

        // Extracting should skip the malicious entry and prevent write outside target
        await provider.ExtractAsync(zipPath, outputDir);

        // Verify that the file was NOT created outside the destination directory
        File.Exists(outsideTarget).Should().BeFalse();
    }

    [Test]
    public async Task SharpCompressExtractor_ProtectsAgainstDirectoryTraversal_ZipSlip_AbsolutePath()
    {
        var zipPath = Path.Combine(this.tempDirectory, "malicious_absolute.zip");
        var outputDir = Path.Combine(this.tempDirectory, "safe_target_abs");
        Directory.CreateDirectory(outputDir);

        var absoluteTarget = Path.Combine(Path.GetTempPath(), "leecharr_evil_absolute_" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var maliciousEntry = archive.CreateEntry(absoluteTarget);
                using var entryStream = maliciousEntry.Open();
                using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync("Malicious payload via absolute path");
            }

            var diskProvider = new DiskProvider();
            var provider = new SharpCompressExtractorProvider(diskProvider);

            await provider.ExtractAsync(zipPath, outputDir);

            File.Exists(absoluteTarget).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(absoluteTarget))
            {
                File.Delete(absoluteTarget);
            }
        }
    }

    [Test]
    public async Task SharpCompressExtractor_ProtectsAgainstDirectoryTraversal_ZipSlip_WindowsStyleBackslashes()
    {
        var zipPath = Path.Combine(this.tempDirectory, "malicious_win_traversal.zip");
        var outputDir = Path.Combine(this.tempDirectory, "safe_target_win");
        Directory.CreateDirectory(outputDir);

        var outsideTarget = Path.Combine(this.tempDirectory, "win_outside.txt");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var maliciousEntry = archive.CreateEntry(@"..\..\win_outside.txt");
            using var entryStream = maliciousEntry.Open();
            using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("Malicious payload via backslash traversal");
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);

        await provider.ExtractAsync(zipPath, outputDir);

        File.Exists(outsideTarget).Should().BeFalse();
    }

    [Test]
    public async Task SharpCompressExtractor_ProtectsAgainstReservedDeviceNames()
    {
        var zipPath = Path.Combine(this.tempDirectory, "reserved_dev.zip");
        var outputDir = Path.Combine(this.tempDirectory, "safe_target_dev");
        Directory.CreateDirectory(outputDir);

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var devEntry = archive.CreateEntry("CON.txt");
            using var entryStream = devEntry.Open();
            using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("Windows reserved device name payload");
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);

        await provider.ExtractAsync(zipPath, outputDir);

        File.Exists(Path.Combine(outputDir, "CON.txt")).Should().BeFalse();
    }

    [Test]
    public async Task SharpCompressExtractor_ExtractsValidEntries_WhileSkippingMaliciousEntries()
    {
        var zipPath = Path.Combine(this.tempDirectory, "mixed_entries.zip");
        var outputDir = Path.Combine(this.tempDirectory, "mixed_output");
        Directory.CreateDirectory(outputDir);

        var outsideTarget = Path.Combine(this.tempDirectory, "mixed_evil.txt");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            // Valid entry
            var validEntry = archive.CreateEntry("valid_folder/safe_file.txt");
            using (var stream = validEntry.Open())
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync("Safe legitimate content");
            }

            // Malicious entry
            var evilEntry = archive.CreateEntry("../../mixed_evil.txt");
            using (var stream = evilEntry.Open())
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync("Malicious traversal payload");
            }
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider);

        var result = await provider.ExtractAsync(zipPath, outputDir);
        result.Should().BeTrue();

        var extractedSafeFile = Path.Combine(outputDir, "valid_folder", "safe_file.txt");
        File.Exists(extractedSafeFile).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedSafeFile)).Should().Be("Safe legitimate content");

        File.Exists(outsideTarget).Should().BeFalse();
    }

    [Test]
    public void SharpCompressExtractor_BufferSize_DefaultsTo128Kb_AndAcceptsCustomSize()
    {
        var diskProvider = new DiskProvider();
        var defaultProvider = new SharpCompressExtractorProvider(diskProvider);
        defaultProvider.BufferSize.Should().Be(128 * 1024);

        var customProvider = new SharpCompressExtractorProvider(diskProvider, bufferSize: 512 * 1024);
        customProvider.BufferSize.Should().Be(512 * 1024);
    }

    [Test]
    public async Task SharpCompressExtractor_ExtractsValidZipArchive_WithCustomHighThroughputBuffer()
    {
        var zipPath = Path.Combine(this.tempDirectory, "high_throughput.zip");
        var outputDir = Path.Combine(this.tempDirectory, "high_throughput_output");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("large_chunk_data.bin");
            using var entryStream = entry.Open();
            var randomData = new byte[256 * 1024]; // 256 KB
            new Random(42).NextBytes(randomData);
            await entryStream.WriteAsync(randomData);
        }

        var diskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(diskProvider, bufferSize: 256 * 1024);

        var success = await provider.ExtractAsync(zipPath, outputDir);
        success.Should().BeTrue();

        var extractedFile = Path.Combine(outputDir, "large_chunk_data.bin");
        File.Exists(extractedFile).Should().BeTrue();
        new FileInfo(extractedFile).Length.Should().Be(256 * 1024);
    }

    [Test]
    public void ArchiveExtractorService_ConcurrencyLimit_DefaultsToTwo_AndAcceptsCustomValue()
    {
        var defaultService = new ArchiveExtractorService(this.diskProvider);
        defaultService.ConcurrencyLimit.Should().Be(2);
        defaultService.ConcurrencySemaphore.CurrentCount.Should().Be(2);

        var customService = new ArchiveExtractorService(this.diskProvider, maxConcurrentExtractions: 4);
        customService.ConcurrencyLimit.Should().Be(4);
        customService.ConcurrencySemaphore.CurrentCount.Should().Be(4);
    }

    [Test]
    public async Task ArchiveExtractorService_ExtractArchiveAsync_ThrottlesConcurrencyViaSemaphore()
    {
        var provider = Substitute.For<IArchiveExtractorProvider>();
        var activeExtractions = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();

        provider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<System.Threading.CancellationToken>())
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

                await Task.Delay(50);

                lock (lockObj)
                {
                    activeExtractions--;
                }

                return true;
            });

        var boundedService = new ArchiveExtractorService(provider, maxConcurrentExtractions: 1);

        var task1 = boundedService.ExtractArchiveAsync("/path/1.zip");
        var task2 = boundedService.ExtractArchiveAsync("/path/2.zip");
        var task3 = boundedService.ExtractArchiveAsync("/path/3.zip");

        var results = await Task.WhenAll(task1, task2, task3);
        results.Should().AllBeEquivalentTo(true);
        maxObservedConcurrency.Should().Be(1);
    }

    [Test]
    public async Task ArchiveExtractorService_ExtractArchiveAsync_WhenDiskSpaceInsufficient_SkipsExtractionAndReturnsFalse()
    {
        var provider = Substitute.For<IArchiveExtractorProvider>();
        var diskMock = Substitute.For<IDiskProvider>();

        diskMock.FileExists("/downloads/movie.zip").Returns(true);
        diskMock.GetFileSize("/downloads/movie.zip").Returns(100_000_000L);
        diskMock.GetAvailableSpace("/downloads/extracted").Returns(140_000_000L); // Needs 150 MB (1.5x)

        var serviceWithDisk = new ArchiveExtractorService(provider, diskMock);

        var result = await serviceWithDisk.ExtractArchiveAsync("/downloads/movie.zip", "/downloads/extracted");

        result.Should().BeFalse();
        await provider.DidNotReceive().ExtractAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ArchiveExtractorService_ExtractArchiveAsync_WhenDiskSpaceSufficient_ProceedsWithExtraction()
    {
        var provider = Substitute.For<IArchiveExtractorProvider>();
        var diskMock = Substitute.For<IDiskProvider>();

        diskMock.FileExists("/downloads/movie.zip").Returns(true);
        diskMock.GetFileSize("/downloads/movie.zip").Returns(100_000_000L);
        diskMock.GetAvailableSpace("/downloads/extracted").Returns(200_000_000L); // 200 MB > 150 MB

        provider.ExtractAsync(
            "/downloads/movie.zip",
            "/downloads/extracted",
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var serviceWithDisk = new ArchiveExtractorService(provider, diskMock);

        var result = await serviceWithDisk.ExtractArchiveAsync("/downloads/movie.zip", "/downloads/extracted");

        result.Should().BeTrue();
        await provider.Received(1).ExtractAsync(
            "/downloads/movie.zip",
            "/downloads/extracted",
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
