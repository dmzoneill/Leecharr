// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Webhooks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrWebhookControllerTest
{
    private ITorrentRepository torrentRepository = null!;
    private ITorrentMediaMetadataRepository mediaMetadataRepository = null!;
    private IArrConnectionRepository arrConnectionRepository = null!;
    private IProwlarrSyncService prowlarrSyncService = null!;
    private IEventAggregator eventAggregator = null!;
    private ArrWebhookController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.mediaMetadataRepository = Substitute.For<ITorrentMediaMetadataRepository>();
        this.arrConnectionRepository = Substitute.For<IArrConnectionRepository>();
        this.prowlarrSyncService = Substitute.For<IProwlarrSyncService>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.controller = new ArrWebhookController(
            this.torrentRepository,
            this.mediaMetadataRepository,
            this.arrConnectionRepository,
            null,
            this.prowlarrSyncService,
            this.eventAggregator);
    }

    [Test]
    public async Task HandleArr_WhenPayloadNull_ReturnsBadRequest()
    {
        var result = await this.controller.HandleArr(null!);
        var badRequest = result.Result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();

        var res = badRequest!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeFalse();
    }

    [Test]
    public async Task HandleSonarr_WhenTestEvent_ReturnsOk()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Test",
            InstanceName = "Sonarr",
        };

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.EventType.Should().Be("Test");
        res.Updated.Should().BeFalse();
    }

    [Test]
    public async Task HandleSonarr_WhenImportEvent_UpdatesTorrentImportStateAndMetadata()
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

        var result = await this.controller.HandleSonarr(payload);
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
    public async Task HandleRadarr_WhenUpgradeEvent_UpdatesTorrentImportState()
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

        var result = await this.controller.HandleRadarr(payload);
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
    public async Task HandleLidarr_WhenRenameEvent_UpdatesTorrentImportState()
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

        var result = await this.controller.HandleLidarr(payload);
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
    public async Task HandleReadarr_WhenImportEvent_UpdatesTorrentImportState()
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

        var result = await this.controller.HandleReadarr(payload);
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
    public async Task HandleSonarr_WhenGrabEvent_DoesNotMarkImported()
    {
        var hash = "9999888877776666555544443333222211110000";
        var torrent = new Torrent
        {
            Id = 5,
            Name = "Show.S01E01.720p",
            InfoHash = hash,
            IsImported = false,
            Category = "tv-sonarr",
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

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeFalse();

        this.torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task HandleSonarr_WhenGrabEventAndCategoryMissing_AssignsCategory()
    {
        var hash = "9999888877776666555544443333222211110001";
        var torrent = new Torrent
        {
            Id = 6,
            Name = "Show.S01E02.720p",
            InfoHash = hash,
            IsImported = false,
            Category = null,
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
                ReleaseTitle = "Show.S01E02.720p",
            },
        };

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 6 &&
            t.Category == "tv-sonarr"));
    }

    [Test]
    public async Task HandleGeneric_WhenTorrentNotFound_ReturnsOkWithUnmatchedStatus()
    {
        this.torrentRepository.All().Returns(new List<Torrent>());

        var payload = new ArrWebhookPayload
        {
            EventType = "Import",
            DownloadClientId = "nonexistent-hash",
        };

        var result = await this.controller.HandleGeneric("Sonarr", payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeFalse();
        res.TorrentId.Should().BeNull();
    }

    [Test]
    public async Task HandleArr_WhenDownloadIdHasUrnBtih_MatchesTorrent()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        var torrent = new Torrent
        {
            Id = 10,
            Name = "UrnBtih.Test.Torrent",
            InfoHash = hash,
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = $"urn:btih:{hash}",
        };

        var result = await this.controller.HandleArr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(10);
        res.InfoHash.Should().Be(hash);
    }

    [Test]
    public async Task HandleArr_WhenDownloadIdHasBase32Hash_MatchesTorrent()
    {
        var base32 = "JBSWY3DPEBLW64TMMQQQJBSWY3DPEBLW";
        var expectedHex = MagnetLinkParser.Base32ToHex(base32).ToLowerInvariant();

        var torrent = new Torrent
        {
            Id = 11,
            Name = "Base32.Test.Torrent",
            InfoHash = expectedHex,
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(expectedHex).Returns(torrent);

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = $"urn:btih:{base32}",
        };

        var result = await this.controller.HandleArr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(11);
        res.InfoHash.Should().Be(expectedHex);
    }

    [Test]
    public async Task HandleArr_WhenReleaseDownloadUrlIsMagnetLink_MatchesTorrent()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        var torrent = new Torrent
        {
            Id = 12,
            Name = "Magnet.Test.Torrent",
            InfoHash = hash,
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "Magnet.Test.Torrent",
                DownloadUrl = $"magnet:?xt=urn:btih:{hash}&dn=Magnet.Test.Torrent",
            },
        };

        var result = await this.controller.HandleArr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(12);
        res.InfoHash.Should().Be(hash);
    }

    [Test]
    public async Task HandleSonarr_WhenTorrentServiceProvidedAndCategoryMissing_CallsSetCategoryAsync()
    {
        var hash = "9999888877776666555544443333222211110002";
        var torrent = new Torrent
        {
            Id = 20,
            Name = "Show.S01E03.720p",
            InfoHash = hash,
            Category = null,
        };

        var service = Substitute.For<ITorrentService>();
        service.GetByInfoHash(hash).Returns(torrent);
        service.Get(20).Returns(torrent);

        var serviceController = new ArrWebhookController(
            this.torrentRepository,
            this.mediaMetadataRepository,
            this.arrConnectionRepository,
            torrentService: service);

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            InstanceName = "Sonarr",
            DownloadClientId = hash,
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "Show.S01E03.720p",
            },
        };

        var result = await serviceController.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        await service.Received(1).SetCategoryAsync(20, "tv-sonarr");
    }

    [Test]
    public async Task HandleArr_WhenCommonSavePathShared_DoesNotMatchFirstDatabaseTorrent()
    {
        var ancientIso = new Torrent
        {
            Id = 1,
            Name = "Ubuntu-22.04-desktop-amd64.iso",
            InfoHash = "1111111111111111111111111111111111111111",
            SavePath = "/downloads",
            IsImported = false,
        };

        var targetShow = new Torrent
        {
            Id = 2,
            Name = "Dark.Matter.S01E01.1080p.WEB-DL",
            InfoHash = "2222222222222222222222222222222222222222",
            SavePath = "/downloads",
            IsImported = false,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { ancientIso, targetShow });

        var payload = new ArrWebhookPayload
        {
            EventType = "Import",
            InstanceName = "Sonarr",
            SourcePath = "/downloads/Dark.Matter.S01E01.1080p.WEB-DL/video.mkv",
            EpisodeFile = new ArrWebhookEpisodeFile
            {
                Path = "/media/tv/Dark Matter/Season 01/Dark Matter - S01E01.mkv",
            },
        };

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(2);
        res.InfoHash.Should().Be("2222222222222222222222222222222222222222");

        this.torrentRepository.DidNotReceive().Update(Arg.Is<Torrent>(t => t.Id == 1));
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 2 && t.IsImported == true));
    }

    [Test]
    public async Task HandleSonarr_WhenSeasonPackImported_MatchesSeasonPackTorrent()
    {
        var seasonPack = new Torrent
        {
            Id = 3,
            Name = "Breaking.Bad.S01.1080p.BluRay",
            InfoHash = "3333333333333333333333333333333333333333",
            SavePath = "/downloads",
            IsImported = false,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { seasonPack });

        var payload = new ArrWebhookPayload
        {
            EventType = "EpisodeImport",
            InstanceName = "Sonarr",
            SourcePath = "/downloads/Breaking.Bad.S01.1080p.BluRay/Breaking.Bad.S01E01.mkv",
            EpisodeFile = new ArrWebhookEpisodeFile
            {
                Path = "/media/tv/Breaking Bad/Season 01/Breaking Bad - S01E01.mkv",
            },
        };

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(3);

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 3 && t.IsImported == true));
    }

    [Test]
    public async Task HandleArr_WhenShortTorrentNameInDatabase_DoesNotFalseMatchLongerReleaseTitle()
    {
        var shortNamed = new Torrent
        {
            Id = 1,
            Name = "Up",
            InfoHash = "4444444444444444444444444444444444444444",
            SavePath = "/movies",
            IsImported = false,
        };

        var commonTagNamed = new Torrent
        {
            Id = 2,
            Name = "1080p",
            InfoHash = "5555555555555555555555555555555555555555",
            SavePath = "/movies",
            IsImported = false,
        };

        var targetMovie = new Torrent
        {
            Id = 3,
            Name = "Up.In.The.Air.2009.1080p",
            InfoHash = "6666666666666666666666666666666666666666",
            SavePath = "/movies",
            IsImported = false,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { shortNamed, commonTagNamed, targetMovie });

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "Up.In.The.Air.2009.1080p",
            },
        };

        var result = await this.controller.HandleRadarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(3);
    }

    [Test]
    public async Task HandleSonarr_WhenEpisodeFileDeleteEvent_ResetsImportStateAndImportPath()
    {
        var torrent = new Torrent
        {
            Id = 4,
            Name = "Severance.S02E01.1080p",
            InfoHash = "7777777777777777777777777777777777777777",
            SavePath = "/downloads",
            IsImported = true,
            ImportPath = "/media/tv/Severance/Season 02/Severance - S02E01.mkv",
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "EpisodeFileDelete",
            InstanceName = "Sonarr",
            EpisodeFile = new ArrWebhookEpisodeFile
            {
                Path = "/media/tv/Severance/Season 02/Severance - S02E01.mkv",
            },
        };

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();
        res.TorrentId.Should().Be(4);

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 4 &&
            t.IsImported == false &&
            t.ImportPath == null));

        this.mediaMetadataRepository.DidNotReceive().Insert(Arg.Any<TorrentMediaMetadata>());
    }

    [Test]
    public async Task HandleRadarr_WhenMovieFileDeleteEvent_ResetsImportStateAndImportPath()
    {
        var torrent = new Torrent
        {
            Id = 5,
            Name = "Inception.2010.1080p",
            InfoHash = "8888888888888888888888888888888888888888",
            SavePath = "/movies",
            IsImported = true,
            ImportPath = "/media/movies/Inception (2010)/Inception (2010).mkv",
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var payload = new ArrWebhookPayload
        {
            EventType = "MovieFileDelete",
            InstanceName = "Radarr",
            MovieFile = new ArrWebhookMovieFile
            {
                Path = "/media/movies/Inception (2010)/Inception (2010).mkv",
            },
        };

        var result = await this.controller.HandleRadarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();
        res.TorrentId.Should().Be(5);

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 5 &&
            t.IsImported == false &&
            t.ImportPath == null));
    }

    [Test]
    public async Task HandleArr_WhenInfoHashInPayloadData_MatchesTorrent()
    {
        var hash = "9999999999999999999999999999999999999999";
        var torrent = new Torrent
        {
            Id = 6,
            Name = "Manual.Grab.Torrent",
            InfoHash = hash,
            IsImported = false,
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            Data = new Dictionary<string, object>
            {
                { "infoHash", hash },
            },
        };

        var result = await this.controller.HandleArr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.TorrentId.Should().Be(6);
        res.InfoHash.Should().Be(hash);
    }

    [Test]
    public async Task HandleProwlarr_TriggersSyncAllAsync_AndReturnsOk()
    {
        this.prowlarrSyncService.SyncAllAsync().Returns(Task.FromResult(5));
        var payload = new ArrWebhookPayload
        {
            EventType = "IndexerSync",
            InstanceName = "Prowlarr",
        };

        var result = await this.controller.HandleProwlarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();
        res.Message.Should().Contain("5 indexers synced");

        await this.prowlarrSyncService.Received(1).SyncAllAsync();
        this.torrentRepository.DidNotReceiveWithAnyArgs().All();
    }

    [Test]
    public async Task HandleGeneric_WithProwlarr_TriggersSyncAllAsync()
    {
        this.prowlarrSyncService.SyncAllAsync().Returns(Task.FromResult(2));
        var payload = new ArrWebhookPayload
        {
            EventType = "CustomEvent",
            InstanceName = "Prowlarr",
        };

        var result = await this.controller.HandleGeneric("prowlarr", payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        await this.prowlarrSyncService.Received(1).SyncAllAsync();
    }

    [TestCase("IndexerSync")]
    [TestCase("IndexerUpdated")]
    [TestCase("IndexerDeleted")]
    [TestCase("IndexerAdded")]
    [TestCase("Sync")]
    [TestCase("SyncAll")]
    [TestCase("IndexerStatusChanged")]
    public async Task HandleArr_WithProwlarrIndexerEvents_TriggersSyncAllAsync(string eventType)
    {
        this.prowlarrSyncService.SyncAllAsync().Returns(Task.FromResult(1));
        var payload = new ArrWebhookPayload
        {
            EventType = eventType,
        };

        var result = await this.controller.HandleArr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Updated.Should().BeTrue();

        await this.prowlarrSyncService.Received(1).SyncAllAsync();
    }

    [Test]
    public async Task HandleProwlarr_WhenTestEvent_ReturnsTestResultWithoutTriggeringSync()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Test",
            InstanceName = "Prowlarr",
        };

        var result = await this.controller.HandleProwlarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
        res.Message.Should().Be("Webhook test received successfully.");

        await this.prowlarrSyncService.DidNotReceive().SyncAllAsync();
    }

    [Test]
    public async Task HandleProwlarr_WhenProwlarrSyncServiceNull_ReturnsOkWithoutCrashing()
    {
        var ctrl = new ArrWebhookController(
            this.torrentRepository,
            this.mediaMetadataRepository,
            this.arrConnectionRepository,
            null,
            null);

        var payload = new ArrWebhookPayload
        {
            EventType = "IndexerSync",
            InstanceName = "Prowlarr",
        };

        var result = await ctrl.HandleProwlarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.Success.Should().BeTrue();
    }

    [Test]
    public async Task HandleSonarr_WhenExistingMetadataPresent_UpdatesExistingMetadataAndPublishesEvent()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Severance.S02E01.1080p.WEB-DL.mkv",
            InfoHash = hash,
            SavePath = "/downloads/Severance.S02E01.1080p.WEB-DL.mkv",
        };

        var existing = new TorrentMediaMetadata
        {
            TorrentId = 1,
            ArrType = "Sonarr",
            Title = "Old Initial Title",
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });
        this.mediaMetadataRepository.GetByTorrentId(1).Returns(existing);

        var payload = new ArrWebhookPayload
        {
            EventType = "EpisodeImport",
            InstanceName = "Sonarr",
            DownloadClientId = hash,
            Series = new ArrWebhookSeries
            {
                Id = 42,
                Title = "Severance",
                Year = 2022,
                TvdbId = 371980,
                ImdbId = "tt11280740",
            },
        };

        var result = await this.controller.HandleSonarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.mediaMetadataRepository.Received(1).Update(Arg.Is<TorrentMediaMetadata>(m =>
            m.TorrentId == 1 &&
            m.ArrType == "Sonarr" &&
            m.ArrMediaId == 42 &&
            m.Title == "Severance" &&
            m.Year == 2022 &&
            m.TvdbId == "371980" &&
            m.ImdbId == "tt11280740"));
        this.mediaMetadataRepository.DidNotReceive().Insert(Arg.Any<TorrentMediaMetadata>());

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e =>
            e.TorrentId == 1 &&
            e.Metadata.Title == "Severance"));
    }

    [Test]
    public async Task HandleLidarr_WhenAlbumPresent_PrefersAlbumTitleOverArtistName()
    {
        var hash = "1111222233334444555566667777888899990000";
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Daft Punk - Discovery",
            InfoHash = hash,
            SavePath = "/music/Daft Punk - Discovery",
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });
        this.mediaMetadataRepository.GetByTorrentId(2).Returns((TorrentMediaMetadata)null);

        var payload = new ArrWebhookPayload
        {
            EventType = "TrackImport",
            InstanceName = "Lidarr",
            DownloadClientId = hash,
            Artist = new ArrWebhookArtist
            {
                Id = 10,
                Name = "Daft Punk",
                MbId = "mbid-123",
            },
            Album = new ArrWebhookAlbum
            {
                Id = 20,
                Title = "Discovery",
                ReleaseDate = "2001-03-12",
            },
        };

        var result = await this.controller.HandleLidarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.mediaMetadataRepository.Received(1).Insert(Arg.Is<TorrentMediaMetadata>(m =>
            m.TorrentId == 2 &&
            m.ArrType == "Lidarr" &&
            m.ArrMediaId == 10 &&
            m.Title == "Discovery" &&
            m.AlbumTitle == "Discovery" &&
            m.ArtistName == "Daft Punk" &&
            m.MusicBrainzId == "mbid-123" &&
            m.Year == 2001));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e =>
            e.TorrentId == 2 &&
            e.Metadata.Title == "Discovery"));
    }

    [Test]
    public async Task HandleReadarr_WhenBookPresent_PrefersBookTitleOverAuthorName()
    {
        var hash = "2222333344445555666677778888999900001111";
        var torrent = new Torrent
        {
            Id = 3,
            Name = "Andy Weir - Project Hail Mary",
            InfoHash = hash,
            SavePath = "/books/Andy Weir - Project Hail Mary",
        };

        this.torrentRepository.GetByInfoHash(hash).Returns(torrent);
        this.torrentRepository.All().Returns(new List<Torrent> { torrent });
        this.mediaMetadataRepository.GetByTorrentId(3).Returns((TorrentMediaMetadata)null);

        var payload = new ArrWebhookPayload
        {
            EventType = "BookImport",
            InstanceName = "Readarr",
            DownloadClientId = hash,
            Author = new ArrWebhookAuthor
            {
                Id = 11,
                Name = "Andy Weir",
            },
            Book = new ArrWebhookBook
            {
                Id = 21,
                Title = "Project Hail Mary",
                ReleaseDate = "2021-05-04",
            },
        };

        var result = await this.controller.HandleReadarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.mediaMetadataRepository.Received(1).Insert(Arg.Is<TorrentMediaMetadata>(m =>
            m.TorrentId == 3 &&
            m.ArrType == "Readarr" &&
            m.ArrMediaId == 11 &&
            m.Title == "Project Hail Mary" &&
            m.ArtistName == "Andy Weir" &&
            m.Year == 2021));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e =>
            e.TorrentId == 3 &&
            e.Metadata.Title == "Project Hail Mary"));
    }

    [Test]
    public async Task FindMatchingTorrent_WhenMatchingByTrackFileOrBookFilePath_MatchesCorrectTorrent()
    {
        var torrentMusic = new Torrent
        {
            Id = 10,
            Name = "Daft Punk - Discovery",
            InfoHash = "aaaabbbbccccddddeeeeffff0000111122223333",
            SavePath = "/music/Daft Punk - Discovery",
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrentMusic });

        var payload = new ArrWebhookPayload
        {
            EventType = "TrackImport",
            InstanceName = "Lidarr",
            TrackFile = new ArrWebhookTrackFile
            {
                Path = "/music/Daft Punk - Discovery/01 One More Time.flac",
            },
            Album = new ArrWebhookAlbum
            {
                Id = 55,
                Title = "Discovery",
            },
        };

        var result = await this.controller.HandleLidarr(payload);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrWebhookResult;
        res.Should().NotBeNull();
        res!.TorrentId.Should().Be(10);
    }
}
