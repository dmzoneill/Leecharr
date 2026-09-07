// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryControllerTest
{
    private IDownloadHistoryService historyService = null!;
    private ITorrentMediaMetadataRepository mediaMetadataRepository = null!;
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private IDownloadHistoryRepository downloadHistoryRepository = null!;
    private ITorrentRepository torrentRepository = null!;
    private DownloadHistoryController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.historyService = Substitute.For<IDownloadHistoryService>();
        this.mediaMetadataRepository = Substitute.For<ITorrentMediaMetadataRepository>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.downloadHistoryRepository = Substitute.For<IDownloadHistoryRepository>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();

        this.controller = new DownloadHistoryController(
            this.historyService,
            this.mediaMetadataRepository,
            this.mediaEnrichmentService,
            this.downloadHistoryRepository,
            this.torrentRepository);
    }

    [Test]
    public void GetAll_ReturnsMappedResources()
    {
        var records = new List<DownloadHistory>
        {
            new DownloadHistory
            {
                Id = 1,
                TorrentId = 10,
                Title = "Severance.S01E01",
                Status = "Active",
            },
        };

        this.historyService.GetAll(null, null, 500).Returns(records);

        var result = this.controller.GetAll();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        var list = okResult!.Value as List<DownloadHistoryResource>;
        list.Should().NotBeNull();
        list.Should().HaveCount(1);
        list![0].Title.Should().Be("Severance.S01E01");
    }

    [Test]
    public void Get_WhenRecordNotFound_ReturnsNotFound()
    {
        this.historyService.Get(99).Returns((DownloadHistory)null!);

        var result = this.controller.Get(99);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task Enrich_WhenRecordNotFound_ReturnsNotFound()
    {
        this.historyService.Get(99).Returns((DownloadHistory)null!);

        var result = await this.controller.Enrich(99);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task Enrich_WhenTorrentIsRemoved_EnrichesMetadataAndUpdatesDataJson()
    {
        var record = new DownloadHistory
        {
            Id = 5,
            TorrentId = null,
            Title = "Oppenheimer.2023.1080p",
            InfoHash = "abc123hash",
            Source = "movies",
            Status = "Removed",
        };

        this.historyService.Get(5).Returns(record);

        var enrichedMeta = new TorrentMediaMetadata
        {
            TorrentId = 0,
            Title = "Oppenheimer",
            Year = 2023,
            ArrType = "Radarr",
            PosterUrl = "https://image.tmdb.org/t/p/w500/oppenheimer.jpg",
        };

        this.mediaEnrichmentService.EnrichTorrentAsync(Arg.Is<Torrent>(t =>
            t.Id == 0 &&
            t.Name == "Oppenheimer.2023.1080p" &&
            t.InfoHash == "abc123hash" &&
            t.Category == "movies"))
            .Returns(Task.FromResult(enrichedMeta));

        var result = await this.controller.Enrich(5);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        var resource = okResult!.Value as DownloadHistoryResource;

        resource.Should().NotBeNull();
        resource!.Title.Should().Be("Oppenheimer.2023.1080p");
        resource.Metadata.Should().NotBeNull();
        resource.Metadata.Title.Should().Be("Oppenheimer");
        resource.Metadata.Year.Should().Be(2023);
        resource.Metadata.ArrType.Should().Be("Radarr");
        resource.Metadata.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w500/oppenheimer.jpg");

        record.DataJson.Should().NotBeNullOrEmpty();
        record.DataJson.Should().Contain("Oppenheimer");
        this.downloadHistoryRepository.Received(1).Update(record);
    }

    [Test]
    public async Task EnrichAll_WhenContainsRemovedTorrents_EnrichesAllAndSavesDataJson()
    {
        var records = new List<DownloadHistory>
        {
            new DownloadHistory
            {
                Id = 1,
                TorrentId = null,
                Title = "Severance.S02E01",
                InfoHash = "sev123",
                Source = "tv",
                Status = "Removed",
            },
            new DownloadHistory
            {
                Id = 2,
                TorrentId = 10,
                Title = "Dune.Part.Two.2024",
                InfoHash = "dune456",
                Source = "movies",
                Status = "Active",
            },
        };

        this.historyService.GetAll(null, null, 1000).Returns(records);

        this.mediaEnrichmentService.EnrichTorrentAsync(Arg.Any<Torrent>())
            .Returns(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                return Task.FromResult(new TorrentMediaMetadata
                {
                    TorrentId = t.Id,
                    Title = t.Name,
                    ArrType = t.Category == "tv" ? "Sonarr" : "Radarr",
                });
            });

        var result = await this.controller.EnrichAll();

        result.Should().BeOfType<OkObjectResult>();
        records[0].DataJson.Should().NotBeNullOrEmpty();
        records[0].DataJson.Should().Contain("Sonarr");
        records[1].DataJson.Should().NotBeNullOrEmpty();
        records[1].DataJson.Should().Contain("Radarr");

        this.downloadHistoryRepository.Received(2).Update(Arg.Any<DownloadHistory>());
    }

    [Test]
    public async Task Reconcile_EnrichesRecordsWithMissingDataJson()
    {
        var records = new List<DownloadHistory>
        {
            new DownloadHistory
            {
                Id = 1,
                TorrentId = null,
                Title = "Dark.Matter.S01E01",
                InfoHash = "dm123",
                Source = "tv",
                DataJson = null,
            },
            new DownloadHistory
            {
                Id = 2,
                TorrentId = null,
                Title = "Already.Enriched.2024",
                InfoHash = "ae456",
                Source = "movies",
                DataJson = "{\"Title\":\"Already Enriched\"}",
            },
        };

        this.historyService.ReconcileAllTorrents().Returns(2);
        this.historyService.GetAll(null, null, 1000).Returns(records);

        this.mediaEnrichmentService.EnrichTorrentAsync(Arg.Is<Torrent>(t => t.Name == "Dark.Matter.S01E01"))
            .Returns(Task.FromResult(new TorrentMediaMetadata
            {
                TorrentId = 0,
                Title = "Dark Matter",
                ArrType = "Sonarr",
            }));

        var result = await this.controller.Reconcile();

        result.Should().BeOfType<OkObjectResult>();
        records[0].DataJson.Should().NotBeNullOrEmpty();
        records[0].DataJson.Should().Contain("Dark Matter");
        this.downloadHistoryRepository.Received(1).Update(records[0]);
    }

    [Test]
    public void Delete_CallsHistoryServiceDelete()
    {
        var result = this.controller.Delete(42);

        result.Should().BeOfType<OkResult>();
        this.historyService.Received(1).Delete(42);
    }

    [Test]
    public void ClearAll_CallsHistoryServiceClearAll()
    {
        var result = this.controller.ClearAll();

        result.Should().BeOfType<OkResult>();
        this.historyService.Received(1).ClearAll();
    }
}
