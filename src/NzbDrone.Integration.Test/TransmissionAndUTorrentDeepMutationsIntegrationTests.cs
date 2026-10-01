// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TransmissionAndUTorrentDeepMutationsIntegrationTests : IntegrationTestBase
{
    private const string UTorrentHash = "c7c7c7c7c7c7c7c7c7c7c7c7c7c7c7c7c7c7c7c7";
    private const string UTorrentName = "TransmissionUTorrentMutationMovie";
    private Torrent testTorrent = null!;
    private TorrentFile testFile = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
        var torrentFileRepo = (ITorrentFileRepository)services.GetService(typeof(ITorrentFileRepository))!;

        this.testTorrent = new Torrent
        {
            Name = UTorrentName,
            InfoHash = UTorrentHash,
            SavePath = "/downloads/movies",
            Category = "movies",
            Status = TorrentStatus.Paused,
            TotalSize = 1048576000L,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow,
        };
        torrentRepo.Insert(this.testTorrent);

        this.testFile = new TorrentFile
        {
            TorrentId = this.testTorrent.Id,
            Path = "movie_sample.mkv",
            Size = 1048576000L,
            PieceOffset = 0,
            PieceCount = 100,
            Priority = 3,
            Progress = 0.5,
        };
        torrentFileRepo.Insert(this.testFile);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var services = GlobalSetup.Factory.Services;
            var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
            var torrentFileRepo = (ITorrentFileRepository)services.GetService(typeof(ITorrentFileRepository))!;

            if (this.testFile != null)
            {
                torrentFileRepo.Delete(this.testFile.Id);
            }

            if (this.testTorrent != null)
            {
                torrentRepo.Delete(this.testTorrent.Id);
            }
        }
        catch
        {
        }
    }

    private async Task<HttpResponseMessage> PostTransmissionRpcAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Transmission-Session-Id", "test-valid-session-id");
        return await this.Client.SendAsync(req);
    }

    [Test]
    public async Task Transmission_TorrentRenamePathAndDeepSet_OperatesSuccessfully()
    {
        // 1. POST /transmission/rpc - torrent-rename-path
        var renamePayload = new
        {
            method = "torrent-rename-path",
            arguments = new
            {
                ids = new[] { this.testTorrent.Id },
                path = "movie_sample.mkv",
                name = "movie_sample_renamed.mkv",
            },
        };

        var renameResp = await this.PostTransmissionRpcAsync(renamePayload);
        renameResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. torrent-rename-path error cases: missing path/name or missing ID
        var badRenamePayload = new
        {
            method = "torrent-rename-path",
            arguments = new
            {
                ids = new[] { this.testTorrent.Id },
                path = "",
                name = "",
            },
        };

        var badRenameResp = await this.PostTransmissionRpcAsync(badRenamePayload);
        badRenameResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var badDoc = JsonDocument.Parse(await badRenameResp.Content.ReadAsStringAsync());
        badDoc.RootElement.GetProperty("result").GetString().Should().Be("invalid arguments");

        // 3. torrent-set with detailed options
        var setPayload = new
        {
            method = "torrent-set",
            arguments = new Dictionary<string, object>
            {
                ["ids"] = new[] { this.testTorrent.Id },
                ["bandwidthPriority"] = 1,
                ["labels"] = new[] { "movies", "hd" },
                ["seedRatioMode"] = 1,
                ["seedRatioLimit"] = 2.5,
                ["seedIdleMode"] = 1,
                ["seedIdleLimit"] = 180,
                ["downloadLimit"] = 2048,
                ["downloadLimited"] = true,
                ["uploadLimit"] = 1024,
                ["uploadLimited"] = true,
                ["files-unwanted"] = new[] { 0 },
                ["files-wanted"] = new[] { 0 },
                ["priority-high"] = new[] { 0 },
            },
        };

        var setResp = await this.PostTransmissionRpcAsync(setPayload);
        setResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var setDoc = JsonDocument.Parse(await setResp.Content.ReadAsStringAsync());
        setDoc.RootElement.GetProperty("result").GetString().Should().Be("success");
    }

    [Test]
    public async Task UTorrent_WebUiActionsAndMutations_OperateSuccessfully()
    {
        // 1. GET /gui?action=getsettings (Safe read)
        var settingsResp = await this.Client.GetAsync("/gui?action=getsettings");
        settingsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var settingsDoc = JsonDocument.Parse(await settingsResp.Content.ReadAsStringAsync());
        settingsDoc.RootElement.TryGetProperty("settings", out _).Should().BeTrue();

        // 2. GET /gui?action=getprops (Safe read)
        var propsResp = await this.Client.GetAsync($"/gui?action=getprops&hash={UTorrentHash}");
        propsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var propsDoc = JsonDocument.Parse(await propsResp.Content.ReadAsStringAsync());
        propsDoc.RootElement.TryGetProperty("props", out _).Should().BeTrue();

        // 3. GET /gui?action=getfiles (Safe read)
        var filesResp = await this.Client.GetAsync($"/gui?action=getfiles&hash={UTorrentHash}");
        filesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filesDoc = JsonDocument.Parse(await filesResp.Content.ReadAsStringAsync());
        filesDoc.RootElement.TryGetProperty("files", out _).Should().BeTrue();

        // 4. setprops via POST: label, seed_ratio, seed_time, max_dl_rate, max_ul_rate
        var setLabelResp = await this.Client.PostAsync($"/gui?action=setprops&hash={UTorrentHash}&s=label&v=updated_label", null);
        setLabelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var setRatioResp = await this.Client.PostAsync($"/gui?action=setprops&hash={UTorrentHash}&s=seed_ratio&v=2500", null);
        setRatioResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var setTimeResp = await this.Client.PostAsync($"/gui?action=setprops&hash={UTorrentHash}&s=seed_time&v=7200", null);
        setTimeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var setDlResp = await this.Client.PostAsync($"/gui?action=setprops&hash={UTorrentHash}&s=max_dl_rate&v=1024000", null);
        setDlResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var setUlResp = await this.Client.PostAsync($"/gui?action=setprops&hash={UTorrentHash}&s=max_ul_rate&v=512000", null);
        setUlResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. setprio for files via POST
        var setPrioResp = await this.Client.PostAsync($"/gui?action=setprio&hash={UTorrentHash}&p=2&f=0", null);
        setPrioResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. recheck, pause, start via POST
        var recheckResp = await this.Client.PostAsync($"/gui?action=recheck&hash={UTorrentHash}", null);
        recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var pauseResp = await this.Client.PostAsync($"/gui?action=pause&hash={UTorrentHash}", null);
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var startResp = await this.Client.PostAsync($"/gui?action=start&hash={UTorrentHash}", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
