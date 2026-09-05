// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Leecharr.Api.V1.Media;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaControllerTest
{
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private MediaController controller = null!;
    private string tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.controller = new MediaController(this.mediaEnrichmentService);
        this.tempDir = Path.Combine(Path.GetTempPath(), "leecharr_media_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    [Test]
    public void GetByTorrentId_WhenFound_ReturnsOk()
    {
        var meta = new TorrentMediaMetadata
        {
            TorrentId = 42,
            Title = "Breaking Bad",
            ArrType = "Sonarr",
        };
        this.mediaEnrichmentService.GetMetadata(42).Returns(meta);

        var result = this.controller.GetByTorrentId(42);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var resource = (MediaMetadataResource)okResult.Value!;
        resource.TorrentId.Should().Be(42);
        resource.Title.Should().Be("Breaking Bad");
    }

    [Test]
    public void GetByTorrentId_WhenNotFound_ReturnsNotFound()
    {
        this.mediaEnrichmentService.GetMetadata(42).Returns((TorrentMediaMetadata)null!);

        var result = this.controller.GetByTorrentId(42);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void GetArtwork_WhenTypeIsInvalid_ReturnsNotFound()
    {
        var result = this.controller.GetArtwork(42, "banner");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void GetArtwork_WhenMetadataNotFound_ReturnsNotFound()
    {
        this.mediaEnrichmentService.GetMetadata(42).Returns((TorrentMediaMetadata)null!);

        var result = this.controller.GetArtwork(42, "poster");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void GetArtwork_WhenFileDoesNotExist_ReturnsNotFound()
    {
        var meta = new TorrentMediaMetadata
        {
            TorrentId = 42,
            PosterLocalPath = Path.Combine(this.tempDir, "nonexistent.jpg"),
        };
        this.mediaEnrichmentService.GetMetadata(42).Returns(meta);

        var result = this.controller.GetArtwork(42, "poster");

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestCase("poster.png", "image/png", "poster")]
    [TestCase("poster.webp", "image/webp", "poster")]
    [TestCase("poster.gif", "image/gif", "poster")]
    [TestCase("poster.svg", "image/svg+xml", "poster")]
    [TestCase("poster.jpg", "image/jpeg", "poster")]
    [TestCase("poster.jpeg", "image/jpeg", "poster")]
    [TestCase("backdrop.png", "image/png", "backdrop")]
    [TestCase("backdrop.bin", "application/octet-stream", "backdrop")]
    public void GetArtwork_WhenFileExists_ReturnsPhysicalFileWithCorrectContentType(string fileName, string expectedContentType, string type)
    {
        var filePath = Path.Combine(this.tempDir, fileName);
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 42,
            PosterLocalPath = type == "poster" ? filePath : null!,
            BackdropLocalPath = type == "backdrop" ? filePath : null!,
        };
        this.mediaEnrichmentService.GetMetadata(42).Returns(meta);

        var result = this.controller.GetArtwork(42, type);

        result.Should().BeOfType<PhysicalFileResult>();
        var fileResult = (PhysicalFileResult)result;
        fileResult.ContentType.Should().Be(expectedContentType);
        fileResult.FileName.Should().Be(Path.GetFullPath(filePath));
    }
}
