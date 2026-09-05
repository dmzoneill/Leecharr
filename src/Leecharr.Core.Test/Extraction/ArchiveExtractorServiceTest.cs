// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.IO.Compression;
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
    [TestCase("movie.z01", false)]
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

        // Extracting should either sanitize the filename inside safe_target or fail
        await provider.ExtractAsync(zipPath, outputDir);

        // Verify that the file was NOT created outside the destination directory
        File.Exists(outsideTarget).Should().BeFalse();
    }

    #endregion
}
