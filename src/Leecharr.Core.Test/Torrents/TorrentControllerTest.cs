// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
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

        this.controller = new TorrentController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.mediaEnrichmentService,
            this.trackerEntryRepository,
            this.signalRBroadcaster);
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
}
