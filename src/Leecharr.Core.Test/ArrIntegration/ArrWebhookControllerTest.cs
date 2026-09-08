// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Webhooks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrWebhookControllerTest
{
    private ITorrentRepository torrentRepository = null!;
    private ITorrentMediaMetadataRepository mediaMetadataRepository = null!;
    private IArrConnectionRepository arrConnectionRepository = null!;
    private ArrWebhookController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.mediaMetadataRepository = Substitute.For<ITorrentMediaMetadataRepository>();
        this.arrConnectionRepository = Substitute.For<IArrConnectionRepository>();
        this.controller = new ArrWebhookController(
            this.torrentRepository,
            this.mediaMetadataRepository,
            this.arrConnectionRepository);
    }

    [Test]
    public void HandleArr_WhenPayloadNull_ReturnsBadRequest()
    {
        var result = this.controller.HandleArr(null!);
        var badRequest = result.Result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();

        var res = badRequest!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeFalse();
    }

    [Test]
    public void HandleSonarr_WhenTestEvent_ReturnsOk()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Test",
            InstanceName = "Sonarr",
        };

        var result = this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.EventType.Should().Be("Test");
        res.Updated.Should().BeFalse();
    }

    [Test]
    public void HandleSonarr_WhenImportEvent_UpdatesTorrentImportStateAndMetadata()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Severance.S02E01.1080p.WEB-DL.mkv",
            InfoHash = hash,
            SavePath = "/downloads/Severance.S02E01.1080p.WEB-DL.mkv",
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "Import",
            InstanceName = "Sonarr-Main",
            DownloadClientId = hash,
            EpisodeFile = new ArrWebhookEpisodeFile
            {
                Id = 101,
                Path = "/media/tv/Severance/Season 02/Severance - S02E01.mkv",
                Quality = "WEBDL-1080p",
            },
            Series = new ArrWebhookSeries
            {
                Id = 42,
                Title = "Severance",
                Year = 2022,
                TvdbId = 371980,
            },
        };

        var result = this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();
        res.TorrentId.Should().Be(1);
        res.InfoHash.Should().Be(hash);

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 1 &&
            t.IsImported == true &&
            t.ImportedAt.HasValue &&
            t.ImportPath == "/media/tv/Severance/Season 02/Severance - S02E01.mkv" &&
            t.ImportedByArr == "Sonarr"));

        this.mediaMetadataRepository.Received(1).Insert(Arg.Is<TorrentMediaMetadata>(m =>
            m.TorrentId == 1 &&
            m.ArrType == "Sonarr" &&
            m.ArrMediaId == 42 &&
            m.Title == "Severance" &&
            m.Year == 2022 &&
            m.TvdbId == "371980"));
    }

    [Test]
    public void HandleRadarr_WhenUpgradeEvent_UpdatesTorrentImportState()
    {
        var hash = "fedcba9876543210fedcba9876543210fedcba98";
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Dune.Part.Two.2024.2160p.UHD.Remux.mkv",
            InfoHash = hash,
            SavePath = "/downloads/Dune.Part.Two.2024.2160p.UHD.Remux.mkv",
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "Upgrade",
            InstanceName = "Radarr-4K",
            DownloadId = hash,
            MovieFile = new ArrWebhookMovieFile
            {
                Id = 202,
                Path = "/media/movies/Dune Part Two (2024)/Dune Part Two (2024) [2160p].mkv",
                Quality = "Remux-2160p",
            },
            Movie = new ArrWebhookMovie
            {
                Id = 99,
                Title = "Dune: Part Two",
                Year = 2024,
                TmdbId = 693134,
                ImdbId = "tt15239678",
            },
        };

        var result = this.controller.HandleRadarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 2 &&
            t.IsImported == true &&
            t.ImportPath == "/media/movies/Dune Part Two (2024)/Dune Part Two (2024) [2160p].mkv" &&
            t.ImportedByArr == "Radarr"));
    }

    [Test]
    public void HandleLidarr_WhenRenameEvent_UpdatesTorrentImportState()
    {
        var hash = "abcdef0123456789abcdef0123456789abcdef01";
        var torrent = new Torrent
        {
            Id = 3,
            Name = "Pink Floyd - The Dark Side of the Moon (1973) [FLAC]",
            InfoHash = hash,
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "Rename",
            InstanceName = "Lidarr",
            DownloadClientId = hash,
            RenamedFiles = new List<ArrWebhookRenamedFile>
            {
                new()
                {
                    PreviousPath = "/media/music/Pink Floyd/Dark Side/01.flac",
                    Path = "/media/music/Pink Floyd/1973 - The Dark Side of the Moon/01 - Speak to Me.flac",
                },
            },
            Artist = new ArrWebhookArtist
            {
                Id = 5,
                Name = "Pink Floyd",
            },
        };

        var result = this.controller.HandleLidarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 3 &&
            t.IsImported == true &&
            t.ImportPath == "/media/music/Pink Floyd/1973 - The Dark Side of the Moon/01 - Speak to Me.flac" &&
            t.ImportedByArr == "Lidarr"));
    }

    [Test]
    public void HandleReadarr_WhenImportEvent_UpdatesTorrentImportState()
    {
        var torrent = new Torrent
        {
            Id = 4,
            Name = "Brandon Sanderson - The Way of Kings",
            InfoHash = "1111222233334444555566667777888899990000",
            IsImported = false,
        };

        this.torrentRepository.Get(4).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "Import",
            DownloadClientId = "4",
            BookFiles = new List<ArrWebhookBookFile>
            {
                new() { Id = 1, Path = "/media/books/Brandon Sanderson/The Way of Kings.epub" },
            },
            Author = new ArrWebhookAuthor
            {
                Id = 12,
                Name = "Brandon Sanderson",
            },
        };

        var result = this.controller.HandleReadarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 4 &&
            t.IsImported == true &&
            t.ImportPath == "/media/books/Brandon Sanderson/The Way of Kings.epub" &&
            t.ImportedByArr == "Readarr"));
    }

    [Test]
    public void HandleSonarr_WhenGrabEvent_DoesNotMarkImported()
    {
        var hash = "9999888877776666555544443333222211110000";
        var torrent = new Torrent
        {
            Id = 5,
            Name = "Show.S01E01.720p",
            InfoHash = hash,
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            InstanceName = "Sonarr",
            DownloadClientId = hash,
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "Show.S01E01.720p",
            },
        };

        var result = this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeFalse();

        this.torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void HandleGeneric_WhenTorrentNotFound_ReturnsOkWithUnmatchedStatus()
    {
        this.torrentRepository.All().Returns(new List<Torrent>());

        var payload = new ArrWebhookPayload
        {
            EventType = "Import",
            DownloadClientId = "nonexistent-hash",
        };

        var result = this.controller.HandleGeneric("Sonarr", payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeFalse();
        res.TorrentId.Should().BeNull();
    }
}
