// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extraction;

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class ArchiveTimeoutCalculatorTest
{
    private IDiskProvider diskProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = Substitute.For<IDiskProvider>();
    }

    [Test]
    public void CalculateDynamicTimeout_WhenFileDoesNotExist_ReturnsDefaultBaseTimeout()
    {
        this.diskProvider.FileExists("/non/existent.zip").Returns(false);

        var timeout = ArchiveTimeoutCalculator.CalculateDynamicTimeout("/non/existent.zip", this.diskProvider);

        timeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void CalculateDynamicTimeout_WhenSingleFile_AddsOneMinutePerGigabyte()
    {
        var archivePath = "/downloads/large_movie.zip";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        // 50 GB = 50 * 1024 * 1024 * 1024 bytes
        this.diskProvider.GetFileSize(archivePath).Returns(50L * 1024 * 1024 * 1024);

        var timeout = ArchiveTimeoutCalculator.CalculateDynamicTimeout(archivePath, this.diskProvider);

        // 30 base + 50 additional = 80 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(80));
    }

    [Test]
    public void CalculateDynamicTimeout_For100GbArchive_Returns130Minutes()
    {
        var archivePath = "/downloads/remux_4k.7z";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        // 100 GB
        this.diskProvider.GetFileSize(archivePath).Returns(100L * 1024 * 1024 * 1024);

        var timeout = ArchiveTimeoutCalculator.CalculateDynamicTimeout(archivePath, this.diskProvider);

        // 30 base + 100 additional = 130 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(130));
    }

    [Test]
    public void CalculateDynamicTimeout_ForMultiPartArchive_SumsAllPartFiles()
    {
        var primaryPart = "/downloads/release.part01.rar";
        this.diskProvider.FileExists(primaryPart).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(true);
        this.diskProvider.GetFiles("/downloads", false).Returns(new[]
        {
            "/downloads/release.part01.rar",
            "/downloads/release.part02.rar",
            "/downloads/release.part03.rar",
            "/downloads/release.part04.rar",
            "/downloads/release.part05.rar",
        });

        // 5 parts of 10 GB each = 50 GB total
        this.diskProvider.GetFileSize(Arg.Any<string>()).Returns(10L * 1024 * 1024 * 1024);

        var timeout = ArchiveTimeoutCalculator.CalculateDynamicTimeout(primaryPart, this.diskProvider);

        // 30 base + 50 additional = 80 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(80));
    }

    [Test]
    public void CalculateDynamicTimeout_ForNumericSplitArchive_SumsAllPartFiles()
    {
        var primaryPart = "/downloads/release.r00";
        this.diskProvider.FileExists(primaryPart).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(true);
        this.diskProvider.GetFiles("/downloads", false).Returns(new[]
        {
            "/downloads/release.r00",
            "/downloads/release.r01",
            "/downloads/release.r02",
        });

        // 3 parts of 20 GB each = 60 GB total
        this.diskProvider.GetFileSize(Arg.Any<string>()).Returns(20L * 1024 * 1024 * 1024);

        var timeout = ArchiveTimeoutCalculator.CalculateDynamicTimeout(primaryPart, this.diskProvider);

        // 30 base + 60 additional = 90 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Test]
    public void CalculateDynamicTimeout_WithCustomBaseTimeoutAndMinutesPerGb()
    {
        var archivePath = "/downloads/archive.zip";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        // 20 GB
        this.diskProvider.GetFileSize(archivePath).Returns(20L * 1024 * 1024 * 1024);

        var timeout = ArchiveTimeoutCalculator.CalculateDynamicTimeout(
            archivePath,
            this.diskProvider,
            baseTimeout: TimeSpan.FromMinutes(45),
            minutesPerGb: 2);

        // 45 base + (20 * 2 = 40) = 85 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(85));
    }

    [Test]
    public void SevenZipExtractorProvider_CalculateTimeout_UsesArchiveTimeoutCalculator()
    {
        var archivePath = "/downloads/test.7z";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        this.diskProvider.GetFileSize(archivePath).Returns(40L * 1024 * 1024 * 1024);

        var provider = new SevenZipExtractorProvider(this.diskProvider);
        var timeout = provider.CalculateTimeout(archivePath);

        timeout.Should().Be(TimeSpan.FromMinutes(70));
    }

    [Test]
    public void SevenZipExtractorProvider_WithConfiguredTimeout_UsesConfiguredBaseTimeout()
    {
        var configService = Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.ArchiveExtractionTimeoutMinutes.Returns(60);

        var archivePath = "/downloads/test.7z";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        this.diskProvider.GetFileSize(archivePath).Returns(10L * 1024 * 1024 * 1024); // 10 GB

        var provider = new SevenZipExtractorProvider(this.diskProvider, configService: configService);
        var timeout = provider.CalculateTimeout(archivePath);

        // 60 base + 10 GB = 70 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(70));
    }

    [Test]
    public void UnrarExtractorProvider_CalculateTimeout_UsesArchiveTimeoutCalculator()
    {
        var archivePath = "/downloads/test.rar";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        this.diskProvider.GetFileSize(archivePath).Returns(25L * 1024 * 1024 * 1024);

        var provider = new UnrarExtractorProvider(this.diskProvider);
        var timeout = provider.CalculateTimeout(archivePath);

        timeout.Should().Be(TimeSpan.FromMinutes(55));
    }

    [Test]
    public void UnrarExtractorProvider_WithConfiguredTimeout_UsesConfiguredBaseTimeout()
    {
        var configService = Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.ArchiveExtractionTimeoutMinutes.Returns(45);

        var archivePath = "/downloads/test.rar";
        this.diskProvider.FileExists(archivePath).Returns(true);
        this.diskProvider.FolderExists("/downloads").Returns(false);
        this.diskProvider.GetFileSize(archivePath).Returns(15L * 1024 * 1024 * 1024); // 15 GB

        var provider = new UnrarExtractorProvider(this.diskProvider, configService: configService);
        var timeout = provider.CalculateTimeout(archivePath);

        // 45 base + 15 GB = 60 minutes
        timeout.Should().Be(TimeSpan.FromMinutes(60));
    }
}
