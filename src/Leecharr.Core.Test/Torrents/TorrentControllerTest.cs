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

        this.controller = new TorrentController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.mediaEnrichmentService,
            this.trackerEntryRepository,
            this.signalRBroadcaster,
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
}
