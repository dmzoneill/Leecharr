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

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class ArchiveExtractorProvidersTest
{
    private IDiskProvider diskProvider = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private string testTempDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.testTempDir = Path.Combine(Path.GetTempPath(), "ArchiveProvidersTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testTempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(this.testTempDir))
            {
                Directory.Delete(this.testTempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    #region Provider Capabilities & Properties

    [Test]
    public void UnrarExtractorProvider_ExposesCorrectMetadataAndCapabilities()
    {
        var provider = new UnrarExtractorProvider(this.diskProvider);

        provider.ProviderId.Should().Be("Unrar");
        provider.DisplayName.Should().Be("RARLAB UnRAR (Official Native)");
        provider.Version.Should().Contain("RARLAB");
        provider.Description.Should().NotBeNullOrWhiteSpace();

        var caps = provider.Capabilities;
        caps.SupportsRar5.Should().BeTrue();
        caps.Supports7z.Should().BeFalse();
        caps.SupportsZip.Should().BeFalse();
        caps.SupportsTarGz.Should().BeFalse();
        caps.SupportsMultiPart.Should().BeTrue();
        caps.SupportsPasswordProtected.Should().BeTrue();
        caps.SupportsSolidArchives.Should().BeTrue();
        caps.SupportsRecoveryVolumes.Should().BeTrue();
    }

    [Test]
    public void SevenZipExtractorProvider_ExposesCorrectMetadataAndCapabilities()
    {
        var provider = new SevenZipExtractorProvider(this.diskProvider);

        provider.ProviderId.Should().Be("SevenZip");
        provider.DisplayName.Should().Be("7-Zip / p7zip (CLI / Native)");
        provider.Version.Should().Contain("7-Zip");
        provider.Description.Should().NotBeNullOrWhiteSpace();

        var caps = provider.Capabilities;
        caps.SupportsRar5.Should().BeTrue();
        caps.Supports7z.Should().BeTrue();
        caps.SupportsZip.Should().BeTrue();
        caps.SupportsTarGz.Should().BeTrue();
        caps.SupportsMultiPart.Should().BeTrue();
        caps.SupportsPasswordProtected.Should().BeTrue();
        caps.SupportsSolidArchives.Should().BeTrue();
        caps.SupportsRecoveryVolumes.Should().BeTrue();
    }

    [Test]
    public void SharpCompressExtractorProvider_ExposesCorrectMetadataAndCapabilities()
    {
        var provider = new SharpCompressExtractorProvider(this.diskProvider, bufferSize: 64 * 1024);

        provider.ProviderId.Should().Be("SharpCompress");
        provider.DisplayName.Should().Be("SharpCompress (Pure C# .NET)");
        provider.IsAvailable.Should().BeTrue();
        provider.BufferSize.Should().Be(64 * 1024);

        var caps = provider.Capabilities;
        caps.SupportsRar5.Should().BeTrue();
        caps.Supports7z.Should().BeTrue();
        caps.SupportsZip.Should().BeTrue();
        caps.SupportsTarGz.Should().BeTrue();
        caps.SupportsMultiPart.Should().BeTrue();
        caps.SupportsPasswordProtected.Should().BeTrue();
        caps.SupportsSolidArchives.Should().BeTrue();
        caps.SupportsRecoveryVolumes.Should().BeFalse(); // SharpCompress does not support recovery volumes
    }

    #endregion

    #region Health Checks

    [Test]
    public async Task SharpCompressExtractorProvider_ProbeHealthAsync_AlwaysReturnsHealthy()
    {
        var provider = new SharpCompressExtractorProvider(this.diskProvider);

        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("operational");
        health.DependencyChecks.Should().ContainSingle();
    }

    [Test]
    public async Task UnrarExtractorProvider_ProbeHealthAsync_WhenBinaryNotFound_ReturnsUnhealthyWithWarning()
    {
        var prev = Environment.GetEnvironmentVariable("UNRAR_PATH");
        try
        {
            Environment.SetEnvironmentVariable("UNRAR_PATH", "/non/existent/path/to/unrar_xyz");
            var provider = new UnrarExtractorProvider(this.diskProvider);

            var health = await provider.ProbeHealthAsync();

            health.IsHealthy.Should().BeFalse();
            health.StatusMessage.Should().Contain("not found");
            health.Warnings.Should().NotBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNRAR_PATH", prev);
        }
    }

    [Test]
    public async Task SevenZipExtractorProvider_ProbeHealthAsync_ReturnsConsistentHealthCheck()
    {
        var provider = new SevenZipExtractorProvider(this.diskProvider);
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        if (provider.IsAvailable)
        {
            health.IsHealthy.Should().BeTrue();
            health.StatusMessage.Should().Contain("found");
        }
        else
        {
            health.IsHealthy.Should().BeFalse();
            health.StatusMessage.Should().Contain("not found");
            health.Warnings.Should().NotBeEmpty();
        }
    }

    #endregion

    #region Archive Detection (CanExtract)

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("movie.mkv", false)]
    [TestCase("album.mp3", false)]
    [TestCase("archive.rar", true)]
    [TestCase("ARCHIVE.RAR", true)]
    [TestCase("archive.cbr", true)]
    [TestCase("archive.001", true)]
    [TestCase("release.part01.rar", true)]
    [TestCase("release.part1.rar", true)]
    [TestCase("release.part02.rar", false)]
    [TestCase("release.part2.rar", false)]
    [TestCase("release.r01", false)]
    public void UnrarExtractorProvider_CanExtract_ValidatesPrimaryArchiveAndRejectsSecondaryVolumes(string path, bool expected)
    {
        var provider = new UnrarExtractorProvider(this.diskProvider);
        provider.CanExtract(path).Should().Be(expected);
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("data.txt", false)]
    [TestCase("archive.7z", true)]
    [TestCase("archive.zip", true)]
    [TestCase("archive.tar", true)]
    [TestCase("archive.gz", true)]
    [TestCase("archive.tgz", true)]
    [TestCase("archive.bz2", true)]
    [TestCase("archive.xz", true)]
    [TestCase("archive.iso", true)]
    [TestCase("archive.cab", true)]
    [TestCase("release.7z.001", true)]
    [TestCase("release.zip.001", true)]
    [TestCase("release.001", true)]
    [TestCase("release.002", false)]
    [TestCase("release.7z.002", false)]
    public void SevenZipExtractorProvider_CanExtract_ValidatesSupportedExtensions(string path, bool expected)
    {
        var provider = new SevenZipExtractorProvider(this.diskProvider);
        provider.CanExtract(path).Should().Be(expected);
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("video.avi", false)]
    [TestCase("bundle.zip", true)]
    [TestCase("bundle.tar", true)]
    [TestCase("bundle.gz", true)]
    [TestCase("bundle.bz2", true)]
    [TestCase("bundle.xz", true)]
    [TestCase("bundle.7z", true)]
    [TestCase("bundle.rar", true)]
    [TestCase("bundle.001", true)]
    [TestCase("bundle.002", false)]
    public void SharpCompressExtractorProvider_CanExtract_ValidatesSupportedExtensions(string path, bool expected)
    {
        var provider = new SharpCompressExtractorProvider(this.diskProvider);
        provider.CanExtract(path).Should().Be(expected);
    }

    #endregion

    #region Timeout Calculation

    [Test]
    public void CalculateTimeout_HonorsConfiguredTimeoutMinutes()
    {
        this.configService.ArchiveExtractionTimeoutMinutes.Returns(45);
        this.diskProvider.GetFileSize("/downloads/archive.rar").Returns(1024L * 1024L * 1024L);

        var provider = new UnrarExtractorProvider(this.diskProvider, this.configService);

        provider.BaseTimeout.Should().Be(TimeSpan.FromMinutes(45));
        var timeout = provider.CalculateTimeout("/downloads/archive.rar");
        timeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(45));
    }

    [Test]
    public void SevenZipExtractorProvider_CalculateTimeout_ScalesWithArchiveSize()
    {
        this.diskProvider.FileExists("/downloads/huge_archive.7z").Returns(true);
        this.diskProvider.GetFileSize("/downloads/huge_archive.7z").Returns(10L * 1024L * 1024L * 1024L); // 10 GB

        var provider = new SevenZipExtractorProvider(
            this.diskProvider,
            baseTimeout: TimeSpan.FromMinutes(10),
            minutesPerGb: 2);

        var timeout = provider.CalculateTimeout("/downloads/huge_archive.7z");

        // Base 10 min + 10 GB * 2 min/GB = 30 min
        timeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
    }

    #endregion

    #region Extraction Stream & Password Handling

    [Test]
    public async Task ExtractAsync_WhenArchiveFileDoesNotExist_ReturnsFalse()
    {
        this.diskProvider.FileExists("/missing/file.rar").Returns(false);

        var unrarProvider = new UnrarExtractorProvider(this.diskProvider);
        (await unrarProvider.ExtractAsync("/missing/file.rar", "/out")).Should().BeFalse();

        var sevenZipProvider = new SevenZipExtractorProvider(this.diskProvider);
        (await sevenZipProvider.ExtractAsync("/missing/file.rar", "/out")).Should().BeFalse();

        var sharpCompressProvider = new SharpCompressExtractorProvider(this.diskProvider);
        (await sharpCompressProvider.ExtractAsync("/missing/file.rar", "/out")).Should().BeFalse();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ExtractAsync_WhenArchivePathInvalid_ReturnsFalse(string invalidPath)
    {
        var sharpCompressProvider = new SharpCompressExtractorProvider(this.diskProvider);
        (await sharpCompressProvider.ExtractAsync(invalidPath, "/out")).Should().BeFalse();
    }

    [Test]
    public async Task SharpCompressExtractorProvider_ExtractAsync_ExtractsNestedZipArchiveSuccessfully()
    {
        var zipPath = Path.Combine(this.testTempDir, "nested.zip");
        var outputDir = Path.Combine(this.testTempDir, "extracted");
        Directory.CreateDirectory(outputDir);

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("folder1/subfolder/file1.txt");
            using (var stream1 = entry1.Open())
            using (var writer1 = new StreamWriter(stream1))
            {
                await writer1.WriteAsync("File 1 contents");
            }

            var entry2 = archive.CreateEntry("root_file.txt");
            using (var stream2 = entry2.Open())
            using (var writer2 = new StreamWriter(stream2))
            {
                await writer2.WriteAsync("Root contents");
            }
        }

        var realDiskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(realDiskProvider);

        var result = await provider.ExtractAsync(zipPath, outputDir);

        result.Should().BeTrue();

        var extractedFile1 = Path.Combine(outputDir, "folder1", "subfolder", "file1.txt");
        var extractedFile2 = Path.Combine(outputDir, "root_file.txt");

        File.Exists(extractedFile1).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedFile1)).Should().Be("File 1 contents");

        File.Exists(extractedFile2).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedFile2)).Should().Be("Root contents");
    }

    [Test]
    public async Task SharpCompressExtractorProvider_ExtractAsync_WhenDestinationIsNull_DefaultsToArchiveDirectory()
    {
        var archiveDir = Path.Combine(this.testTempDir, "archive_parent");
        Directory.CreateDirectory(archiveDir);
        var zipPath = Path.Combine(archiveDir, "archive.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("extracted_in_place.txt");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync("In place content");
        }

        var realDiskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(realDiskProvider);

        var result = await provider.ExtractAsync(zipPath, null!);

        result.Should().BeTrue();
        var expectedFile = Path.Combine(archiveDir, "extracted_in_place.txt");
        File.Exists(expectedFile).Should().BeTrue();
    }

    [Test]
    public async Task SharpCompressExtractorProvider_ExtractAsync_WhenArchiveHasEmptyEntries_ExtractsWithoutCrashing()
    {
        var zipPath = Path.Combine(this.testTempDir, "empty_entry.zip");
        var outputDir = Path.Combine(this.testTempDir, "empty_out");
        Directory.CreateDirectory(outputDir);

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("zero_bytes.txt");
            using var stream = entry.Open(); // Write 0 bytes
        }

        var realDiskProvider = new DiskProvider();
        var provider = new SharpCompressExtractorProvider(realDiskProvider);

        var result = await provider.ExtractAsync(zipPath, outputDir);

        result.Should().BeTrue();
        var extractedFile = Path.Combine(outputDir, "zero_bytes.txt");
        File.Exists(extractedFile).Should().BeTrue();
        new FileInfo(extractedFile).Length.Should().Be(0);
    }

    [Test]
    public void BuildProcessStartInfo_ValidatesRedirectsAndArguments()
    {
        var unrarInfo = UnrarExtractorProvider.BuildProcessStartInfo(
            binary: "unrar",
            archivePath: "/data/archive.rar",
            destinationPath: "/data/out/",
            candidatePassword: "secret");

        unrarInfo.RedirectStandardInput.Should().BeTrue();
        unrarInfo.RedirectStandardOutput.Should().BeTrue();
        unrarInfo.RedirectStandardError.Should().BeTrue();
        unrarInfo.ArgumentList.Should().Contain("-psecret");

        var sevenZipInfo = SevenZipExtractorProvider.BuildProcessStartInfo(
            binary: "7z",
            archivePath: "/data/archive.7z",
            destinationPath: "/data/out/",
            candidatePassword: "secret");

        sevenZipInfo.RedirectStandardInput.Should().BeTrue();
        sevenZipInfo.RedirectStandardOutput.Should().BeTrue();
        sevenZipInfo.RedirectStandardError.Should().BeTrue();
        sevenZipInfo.ArgumentList.Should().Contain("-psecret");
    }

    #endregion
}
