// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Creation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentCreationAndQueueManagementComprehensiveIntegrationTests : IntegrationTestBase
{
    private string tempTestDir = null!;
    private string originalDownloadDir = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var configService = (IConfigService)services.GetService(typeof(IConfigService))!;
        this.originalDownloadDir = configService.DownloadDir;
        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        this.tempTestDir = Path.Combine(baseDir, "creation_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempTestDir);

        configService.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["DownloadDir"] = this.tempTestDir,
        });
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var services = GlobalSetup.Factory.Services;
            var configService = (IConfigService)services.GetService(typeof(IConfigService))!;
            if (!string.IsNullOrEmpty(this.originalDownloadDir))
            {
                configService.SaveConfigDictionary(new Dictionary<string, object>
                {
                    ["DownloadDir"] = this.originalDownloadDir,
                });
            }
        }
        catch
        {
        }

        if (Directory.Exists(this.tempTestDir))
        {
            try
            {
                Directory.Delete(this.tempTestDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task TorrentCreation_SingleFileAndDirectory_CreatesValidTorrentFiles()
    {
        // 1. Create a dummy source file
        var sourceFilePath = Path.Combine(this.tempTestDir, "sample_media.mkv");
        var dummyData = new byte[65536];
        new Random(42).NextBytes(dummyData);
        await File.WriteAllBytesAsync(sourceFilePath, dummyData);

        var outputTorrentPath = Path.Combine(this.tempTestDir, "sample_media.torrent");

        // 2. POST /api/v1/torrents/create - Single File
        var singleFilePayload = new
        {
            path = sourceFilePath,
            outputPath = outputTorrentPath,
            name = "SampleMediaTorrent",
            comment = "Created during integration testing",
            createdBy = "Leecharr Automated Tests",
            isPrivate = true,
            pieceLength = 16384,
            trackers = new List<string> { "http://tracker.example.com:8080/announce" },
            webSeeds = new List<string> { "http://seed.example.com/sample_media.mkv" },
        };

        var createSingleResp = await this.PostJsonAsync("/api/v1/torrents/create", singleFilePayload);
        createSingleResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var singleDoc = JsonDocument.Parse(await createSingleResp.Content.ReadAsStringAsync());
        singleDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        singleDoc.RootElement.GetProperty("infoHash").GetString().Should().NotBeNullOrWhiteSpace();
        singleDoc.RootElement.GetProperty("pieceCount").GetInt32().Should().Be(4);
        singleDoc.RootElement.GetProperty("totalSize").GetInt64().Should().Be(65536);
        File.Exists(outputTorrentPath).Should().BeTrue();

        // 3. Create a directory with multiple files for multi-file torrent
        var albumDir = Path.Combine(this.tempTestDir, "MusicAlbum");
        Directory.CreateDirectory(albumDir);
        await File.WriteAllBytesAsync(Path.Combine(albumDir, "track01.flac"), new byte[32768]);
        await File.WriteAllBytesAsync(Path.Combine(albumDir, "track02.flac"), new byte[32768]);

        var multiFilePayload = new
        {
            path = albumDir,
            name = "MusicAlbumRelease",
            pieceLength = 32768,
            trackers = new List<string> { "udp://tracker.openbittorrent.com:80/announce" },
        };

        var createMultiResp = await this.PostJsonAsync("/api/v1/torrents/create", multiFilePayload);
        createMultiResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var multiDoc = JsonDocument.Parse(await createMultiResp.Content.ReadAsStringAsync());
        multiDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        multiDoc.RootElement.GetProperty("pieceCount").GetInt32().Should().Be(2);

        // 4. Error validation: sensitive Unix directory path should be rejected
        var invalidPathPayload = new
        {
            path = "/etc/shadow",
        };

        var invalidResp = await this.PostJsonAsync("/api/v1/torrents/create", invalidPathPayload);
        invalidResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var invalidDoc = JsonDocument.Parse(await invalidResp.Content.ReadAsStringAsync());
        invalidDoc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        invalidDoc.RootElement.GetProperty("errorMessage").GetString().Should().NotBeNullOrWhiteSpace();

        // 5. Empty path returns 400 BadRequest
        var emptyPathResp = await this.PostJsonAsync("/api/v1/torrents/create", new { path = "" });
        emptyPathResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task QueueManagerService_ProcessQueueAndEvents_HandlesQueueStateTransitions()
    {
        var services = GlobalSetup.Factory.Services;
        var queueManager = (IQueueManagerService)services.GetService(typeof(IQueueManagerService))!;

        queueManager.Should().NotBeNull();

        // 1. ProcessQueueAsync execution
        await queueManager.ProcessQueueAsync();

        // 2. Handle TorrentStatusChangedEvent
        var statusEvent = new TorrentStatusChangedEvent
        {
            Torrent = new Torrent { Id = 1, Name = "StatusTest" },
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Seeding,
        };
        if (queueManager is IHandle<TorrentStatusChangedEvent> statusHandler)
        {
            statusHandler.Handle(statusEvent);
        }

        // 3. Handle ConfigSavedEvent
        var cfgEvent = new ConfigSavedEvent();
        if (queueManager is IHandle<ConfigSavedEvent> cfgHandler)
        {
            cfgHandler.Handle(cfgEvent);
        }

        // 4. Handle TorrentAddedEvent and TorrentDeletedEvent
        var addEvent = new TorrentAddedEvent
        {
            Torrent = new Torrent { Id = 9999, Name = "QueueTestTorrent" },
        };
        if (queueManager is IHandle<TorrentAddedEvent> addHandler)
        {
            addHandler.Handle(addEvent);
        }

        var delEvent = new TorrentDeletedEvent
        {
            Torrent = new Torrent { Id = 9999, Name = "QueueTestTorrent" },
            DeleteFiles = false,
        };
        if (queueManager is IHandle<TorrentDeletedEvent> delHandler)
        {
            delHandler.Handle(delEvent);
        }
    }
}
