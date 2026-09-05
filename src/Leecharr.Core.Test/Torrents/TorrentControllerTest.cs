// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IBroadcastSignalRMessage signalRBroadcaster = null!;
    private IDownloadEngine downloadEngine = null!;
    private IGeoIpService geoIpService = null!;
    private TorrentController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.geoIpService = Substitute.For<IGeoIpService>();

        this.controller = new TorrentController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.mediaEnrichmentService,
            this.trackerEntryRepository,
            this.signalRBroadcaster,
            geoIpService: this.geoIpService,
            downloadEngine: this.downloadEngine);
    }

    [Test]
    public void GetAll_BatchLoadsMediaMetadata_DoesNotQueryGetMetadataPerTorrent()
    {
        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1", InfoHash = "hash1" };
        var torrent2 = new Torrent { Id = 2, Name = "Torrent 2", InfoHash = "hash2" };

        this.torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });
        this.mediaEnrichmentService.GetAllMetadata().Returns(new Dictionary<int, TorrentMediaMetadata>
        {
            { 1, new TorrentMediaMetadata { TorrentId = 1, Title = "Movie 1" } },
            { 2, new TorrentMediaMetadata { TorrentId = 2, Title = "Movie 2" } },
        });

        var result = this.controller.GetAll();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<TorrentResource>>().Subject;
        list.Should().HaveCount(2);

        // Verify GetAllMetadata was called once
        this.mediaEnrichmentService.Received(1).GetAllMetadata();

        // Verify individual GetMetadata was never called (avoiding N+1 queries)
        this.mediaEnrichmentService.DidNotReceive().GetMetadata(Arg.Any<int>());
    }

    [Test]
    public async Task Update_PersistsForceStartTargetRatioSeedTimeShareLimitActionCategoryAndLabel()
    {
        var existing = new Torrent
        {
            Id = 10,
            Name = "Initial Torrent",
            ForceStart = false,
            TargetRatio = 0,
            TargetSeedTimeMinutes = 0,
            ShareLimitAction = "Pause",
            Category = "initial-cat",
            Label = "initial-label",
        };

        this.torrentService.Get(10).Returns(existing);
        this.torrentService.UpdateAsync(existing).Returns(Task.FromResult(existing));

        var resource = new TorrentResource
        {
            Id = 10,
            Name = "Initial Torrent",
            ForceStart = true,
            TargetRatio = 3.5,
            TargetSeedTimeMinutes = 120,
            ShareLimitAction = "SuperSeeding",
            Category = "movies",
            Label = "4k",
        };

        var response = await this.controller.Update(10, resource);

        existing.ForceStart.Should().BeTrue();
        existing.TargetRatio.Should().Be(3.5);
        existing.TargetSeedTimeMinutes.Should().Be(120);
        existing.ShareLimitAction.Should().Be("SuperSeeding");
        existing.Category.Should().Be("movies");
        existing.Label.Should().Be("4k");

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resultResource = okResult.Value.Should().BeOfType<TorrentResource>().Subject;
        resultResource.ForceStart.Should().Be(true);
        resultResource.TargetRatio.Should().Be(3.5);
        resultResource.TargetSeedTimeMinutes.Should().Be(120);
        resultResource.ShareLimitAction.Should().Be("SuperSeeding");
        resultResource.Category.Should().Be("movies");
        resultResource.Label.Should().Be("4k");
    }

    [Test]
    public async Task Update_CanClearCategoryAndLabel_WhenEmptyStringsProvided()
    {
        var existing = new Torrent
        {
            Id = 11,
            Name = "Categorized Torrent",
            Category = "movies",
            Label = "action",
        };

        this.torrentService.Get(11).Returns(existing);
        this.torrentService.UpdateAsync(existing).Returns(Task.FromResult(existing));

        var resource = new TorrentResource
        {
            Id = 11,
            Category = string.Empty,
            Label = string.Empty,
        };

        var response = await this.controller.Update(11, resource);

        existing.Category.Should().BeEmpty();
        existing.Label.Should().BeEmpty();

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resultResource = okResult.Value.Should().BeOfType<TorrentResource>().Subject;
        resultResource.Category.Should().BeEmpty();
        resultResource.Label.Should().BeEmpty();
    }

    [Test]
    public async Task Update_DoesNotOverwriteCategoryAndLabel_WhenNullProvided()
    {
        var existing = new Torrent
        {
            Id = 12,
            Name = "Preserved Torrent",
            Category = "series",
            Label = "drama",
            TargetRatio = 1.5,
        };

        this.torrentService.Get(12).Returns(existing);
        this.torrentService.UpdateAsync(existing).Returns(Task.FromResult(existing));

        var resource = new TorrentResource
        {
            Id = 12,
            Category = null,
            Label = null,
            TargetRatio = 4.0,
        };

        var response = await this.controller.Update(12, resource);

        existing.Category.Should().Be("series");
        existing.Label.Should().Be("drama");
        existing.TargetRatio.Should().Be(4.0);

        var okResult = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resultResource = okResult.Value.Should().BeOfType<TorrentResource>().Subject;
        resultResource.Category.Should().Be("series");
        resultResource.Label.Should().Be("drama");
        resultResource.TargetRatio.Should().Be(4.0);
    }

    [Test]
    public async Task AddTracker_ValidRequest_RegistersWithDownloadEngineAndReturnsQueued()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Valid Torrent",
            IsPrivate = false,
        };
        this.torrentService.Get(42).Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(42).Returns(new List<TrackerEntry>());
        this.trackerEntryRepository.Insert(Arg.Any<TrackerEntry>())
            .Returns(callInfo =>
            {
                var entry = callInfo.Arg<TrackerEntry>();
                entry.Id = 99;
                return entry;
            });

        var request = new AddTrackerRequest { Url = "udp://tracker.openbittorrent.com:80/announce" };
        var actionResult = await this.controller.AddTracker(42, request);

        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resource = okResult.Value.Should().BeOfType<TrackerResource>().Subject;

        resource.Id.Should().Be(99);
        resource.Url.Should().Be("udp://tracker.openbittorrent.com:80/announce");
        resource.Status.Should().Be("Queued");
        resource.TotalAnnounces.Should().Be(0);
        resource.SuccessfulAnnounces.Should().Be(0);
        resource.LastAnnounce.Should().BeNull();
        resource.Message.Should().Be("Queued for announce");

        await this.downloadEngine.Received(1)
            .AddTrackersAsync(42, Arg.Is<IEnumerable<string>>(urls => urls.Contains("udp://tracker.openbittorrent.com:80/announce")));

        this.trackerEntryRepository.Received(1)
            .Insert(Arg.Is<TrackerEntry>(t =>
                t.TorrentId == 42 &&
                t.Url == "udp://tracker.openbittorrent.com:80/announce" &&
                t.Status == 0 &&
                t.TotalAnnounces == 0 &&
                t.SuccessfulAnnounces == 0 &&
                t.LastAnnounce == null &&
                t.AnnounceInterval == 1800));
    }

    [Test]
    public async Task AddTracker_PrivateTorrent_ReturnsBadRequestDueToBep27()
    {
        var torrent = new Torrent
        {
            Id = 5,
            Name = "Private Torrent",
            IsPrivate = true,
        };
        this.torrentService.Get(5).Returns(torrent);

        var request = new AddTrackerRequest { Url = "udp://tracker.openbittorrent.com:80/announce" };
        var actionResult = await this.controller.AddTracker(5, request);

        var badRequestResult = actionResult.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().Be("Cannot add public trackers to private torrents");

        this.trackerEntryRepository.DidNotReceive().Insert(Arg.Any<TrackerEntry>());
        await this.downloadEngine.DidNotReceive().AddTrackersAsync(Arg.Any<int>(), Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task AddTracker_DuplicateTracker_ReturnsConflict()
    {
        var torrent = new Torrent
        {
            Id = 7,
            Name = "Torrent With Existing Tracker",
            IsPrivate = false,
        };
        this.torrentService.Get(7).Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(7).Returns(new List<TrackerEntry>
        {
            new TrackerEntry
            {
                Id = 1,
                TorrentId = 7,
                Url = "udp://tracker.openbittorrent.com:80/announce",
            },
        });

        var request = new AddTrackerRequest { Url = " UDP://TRACKER.openbittorrent.com:80/announce " };
        var actionResult = await this.controller.AddTracker(7, request);

        var conflictResult = actionResult.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflictResult.Value.Should().Be("Tracker already exists for this torrent");

        this.trackerEntryRepository.DidNotReceive().Insert(Arg.Any<TrackerEntry>());
        await this.downloadEngine.DidNotReceive().AddTrackersAsync(Arg.Any<int>(), Arg.Any<IEnumerable<string>>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task AddTracker_NullOrWhitespaceUrl_ReturnsBadRequest(string url)
    {
        var request = new AddTrackerRequest { Url = url };
        var actionResult = await this.controller.AddTracker(1, request);

        var badRequestResult = actionResult.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().Be("Tracker URL is required");

        this.trackerEntryRepository.DidNotReceive().Insert(Arg.Any<TrackerEntry>());
        await this.downloadEngine.DidNotReceive().AddTrackersAsync(Arg.Any<int>(), Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task AddTracker_MissingTorrent_ReturnsNotFound()
    {
        this.torrentService.Get(999).Returns((Torrent)null!);

        var request = new AddTrackerRequest { Url = "udp://tracker.openbittorrent.com:80/announce" };
        var actionResult = await this.controller.AddTracker(999, request);

        actionResult.Result.Should().BeOfType<NotFoundResult>();

        this.trackerEntryRepository.DidNotReceive().Insert(Arg.Any<TrackerEntry>());
        await this.downloadEngine.DidNotReceive().AddTrackersAsync(Arg.Any<int>(), Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public void GetFiles_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.torrentService.Get(404).Returns((Torrent)null);

        var result = this.controller.GetFiles(404);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestCase(TorrentStatus.Completed)]
    [TestCase(TorrentStatus.Seeding)]
    public void GetFiles_WhenTorrentCompletedOrSeeding_ReturnsFilesWithFullProgressAndCompletedBytes(TorrentStatus status)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Completed Torrent",
            Status = status,
            Progress = 1.0,
            TotalSize = 3000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 101, TorrentId = 1, Path = "video.mkv", Size = 2500, PieceOffset = 0, PieceCount = 5, Progress = 0.0, BytesCompleted = 0 },
            new() { Id = 102, TorrentId = 1, Path = "sample.mkv", Size = 500, PieceOffset = 5, PieceCount = 1, Progress = 0.0, BytesCompleted = 0 },
        };

        this.torrentService.Get(1).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(files);

        var result = this.controller.GetFiles(1);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<TorrentFileResource>>().Subject;
        resources.Should().HaveCount(2);

        resources[0].Progress.Should().Be(1.0);
        resources[0].BytesCompleted.Should().Be(2500);

        resources[1].Progress.Should().Be(1.0);
        resources[1].BytesCompleted.Should().Be(500);
    }

    [Test]
    public void GetFiles_WhenTorrentProgressIs100PercentEvenIfStatusIsNotCompleted_ReturnsFilesWithFullProgress()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "100% Torrent",
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
            TotalSize = 1000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 201, TorrentId = 2, Path = "file.iso", Size = 1000, PieceOffset = 0, PieceCount = 2, Progress = 0.0, BytesCompleted = 0 },
        };

        this.torrentService.Get(2).Returns(torrent);
        this.torrentFileService.GetFiles(2).Returns(files);

        var result = this.controller.GetFiles(2);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<TorrentFileResource>>().Subject;
        resources[0].Progress.Should().Be(1.0);
        resources[0].BytesCompleted.Should().Be(1000);
    }

    [Test]
    public void GetFiles_WhenDownloading_EnrichesFilesWithPieceBitfieldProgressAndBytesCompleted()
    {
        var torrent = new Torrent
        {
            Id = 3,
            Name = "Downloading Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            PieceLength = 500,
            PieceCount = 4,
            TotalSize = 2000,
        };

        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns(new[] { true, true, false, false });
        downloadTask.PieceLength.Returns(500);

        var files = new List<TorrentFile>
        {
            new() { Id = 301, TorrentId = 3, Path = "file1.bin", Size = 1000, PieceOffset = 0, PieceCount = 2, Progress = 0.0, BytesCompleted = 0 },
            new() { Id = 302, TorrentId = 3, Path = "file2.bin", Size = 1000, PieceOffset = 2, PieceCount = 2, Progress = 0.0, BytesCompleted = 0 },
        };

        this.torrentService.Get(3).Returns(torrent);
        this.torrentService.GetDownloadTask(3).Returns(downloadTask);
        this.torrentFileService.GetFiles(3).Returns(files);

        var result = this.controller.GetFiles(3);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<TorrentFileResource>>().Subject;
        resources.Should().HaveCount(2);

        // File 1 has both pieces completed -> 100%
        resources[0].Progress.Should().Be(1.0);
        resources[0].BytesCompleted.Should().Be(1000);

        // File 2 has 0 pieces completed -> 0%
        resources[1].Progress.Should().Be(0.0);
        resources[1].BytesCompleted.Should().Be(0);
    }

    [Test]
    public void GetFiles_WhenDownloadingWithPartialPieces_ComputesPartialBytesCompleted()
    {
        var torrent = new Torrent
        {
            Id = 4,
            Name = "Partial Downloading Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.25,
            PieceLength = 1000,
            PieceCount = 4,
            TotalSize = 4000,
        };

        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns(new[] { true, false, false, false });
        downloadTask.PieceLength.Returns(1000);

        var files = new List<TorrentFile>
        {
            new() { Id = 401, TorrentId = 4, Path = "file1.bin", Size = 2000, PieceOffset = 0, PieceCount = 2, Progress = 0.0, BytesCompleted = 0 },
        };

        this.torrentService.Get(4).Returns(torrent);
        this.torrentService.GetDownloadTask(4).Returns(downloadTask);
        this.torrentFileService.GetFiles(4).Returns(files);

        var result = this.controller.GetFiles(4);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<TorrentFileResource>>().Subject;

        // 1 of 2 pieces completed -> 1000 bytes, 50% progress
        resources[0].BytesCompleted.Should().Be(1000);
        resources[0].Progress.Should().Be(0.5);
    }

    [Test]
    public void GetFiles_WhenDownloadingWithoutTask_ProratesFileProgressFromTorrentProgress()
    {
        var torrent = new Torrent
        {
            Id = 5,
            Name = "Prorated Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.4,
            TotalSize = 2000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 501, TorrentId = 5, Path = "file1.bin", Size = 1000, PieceOffset = 0, PieceCount = 0, Progress = 0.0, BytesCompleted = 0 },
            new() { Id = 502, TorrentId = 5, Path = "file2.bin", Size = 1000, PieceOffset = 0, PieceCount = 0, Progress = 0.0, BytesCompleted = 0 },
        };

        this.torrentService.Get(5).Returns(torrent);
        this.torrentService.GetDownloadTask(5).Returns((IDownloadTask)null);
        this.torrentFileService.GetFiles(5).Returns(files);

        var result = this.controller.GetFiles(5);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<TorrentFileResource>>().Subject;

        resources[0].BytesCompleted.Should().Be(400);
        resources[0].Progress.Should().Be(0.4);

        resources[1].BytesCompleted.Should().Be(400);
        resources[1].Progress.Should().Be(0.4);
    }

    [Test]
    public void GetPeers_WhenTaskIsNull_ReturnsEmptyList()
    {
        this.torrentService.GetDownloadTask(1).Returns((IDownloadTask)null!);

        var result = this.controller.GetPeers(1);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<PeerResource>>().Subject;
        resources.Should().BeEmpty();
    }

    [Test]
    public void GetPeers_WhenPeersExist_MapsCountryCodeCountryNameCityAndPeerProperties()
    {
        var downloadTask = Substitute.For<IDownloadTask>();
        var peers = new List<PeerInfo>
        {
            new()
            {
                Ip = "8.8.8.8",
                Port = 51413,
                Client = "qBittorrent/4.5.0",
                UploadSpeed = 1024,
                DownloadSpeed = 2048,
                Uploaded = 10000,
                Downloaded = 20000,
                Progress = 0.75,
                Flags = "uE",
            },
            new()
            {
                Ip = "192.168.1.100",
                Port = 6881,
                Client = "Transmission/3.00",
                UploadSpeed = 0,
                DownloadSpeed = 512,
                Uploaded = 0,
                Downloaded = 5000,
                Progress = 0.25,
                Flags = "d",
            },
        };

        downloadTask.GetPeers().Returns(peers);
        this.torrentService.GetDownloadTask(1).Returns(downloadTask);

        this.geoIpService.Lookup("8.8.8.8").Returns(new GeoLocationInfo
        {
            CountryCode = "US",
            CountryName = "United States",
            City = "Mountain View",
        });
        this.geoIpService.Lookup("192.168.1.100").Returns((GeoLocationInfo)null!);

        var result = this.controller.GetPeers(1);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var resources = okResult.Value.Should().BeAssignableTo<List<PeerResource>>().Subject;
        resources.Should().HaveCount(2);

        // Peer 1 with resolved GeoIP
        resources[0].Id.Should().Be(1);
        resources[0].Ip.Should().Be("8.8.8.8");
        resources[0].Port.Should().Be(51413);
        resources[0].Client.Should().Be("qBittorrent/4.5.0");
        resources[0].UploadSpeed.Should().Be(1024);
        resources[0].DownloadSpeed.Should().Be(2048);
        resources[0].Uploaded.Should().Be(10000);
        resources[0].Downloaded.Should().Be(20000);
        resources[0].Progress.Should().Be(0.75);
        resources[0].Flags.Should().Be("uE");
        resources[0].CountryCode.Should().Be("US");
        resources[0].CountryName.Should().Be("United States");
        resources[0].City.Should().Be("Mountain View");

        // Peer 2 with unresolvable/LAN GeoIP (null lookup fallback to empty string)
        resources[1].Id.Should().Be(2);
        resources[1].Ip.Should().Be("192.168.1.100");
        resources[1].Port.Should().Be(6881);
        resources[1].Client.Should().Be("Transmission/3.00");
        resources[1].CountryCode.Should().Be(string.Empty);
        resources[1].CountryName.Should().Be(string.Empty);
        resources[1].City.Should().Be(string.Empty);
    }
}
