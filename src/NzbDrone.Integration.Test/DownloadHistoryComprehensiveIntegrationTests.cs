// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DownloadHistoryComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string HistoryHash = "d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1";
    private const string HistoryName = "HistoryIntegrationMovie.2024";

    [Test]
    public async Task DownloadHistory_FullLifecycleAndReAdd_OperatesCorrectly()
    {
        // 1. Add a torrent to active library
        var magnetUri = $"magnet:?xt=urn:btih:{HistoryHash}&dn={HistoryName}";
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = magnetUri,
            category = "history-test-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 2. Query download history - record should be present
            var histResp = await this.Client.GetAsync($"/api/v1/downloadhistory?query={HistoryName}");
            histResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var histDoc = JsonDocument.Parse(await histResp.Content.ReadAsStringAsync());
            histDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            histDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

            var firstRecord = histDoc.RootElement[0];
            var historyId = firstRecord.GetProperty("id").GetInt32();
            firstRecord.GetProperty("title").GetString().Should().Contain("HistoryIntegrationMovie");

            // 3. GET /api/v1/downloadhistory/{id}
            var getByIdResp = await this.Client.GetAsync($"/api/v1/downloadhistory/{historyId}");
            getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. POST /api/v1/downloadhistory/{id}/enrich
            var enrichResp = await this.PostJsonAsync($"/api/v1/downloadhistory/{historyId}/enrich", new { });
            enrichResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. POST /api/v1/downloadhistory/enrich-all
            var enrichAllResp = await this.PostJsonAsync("/api/v1/downloadhistory/enrich-all", new { });
            enrichAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. POST /api/v1/downloadhistory/reconcile
            var reconcileResp = await this.PostJsonAsync("/api/v1/downloadhistory/reconcile", new { });
            reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 7. ReAdd while torrent is still active returns 409 Conflict
            var conflictResp = await this.PostJsonAsync($"/api/v1/downloadhistory/{historyId}/readd", new { });
            conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

            // 8. Delete active torrent from library so we can re-add it
            var delTorrentResp = await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteData=false");
            delTorrentResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            // 9. Now ReAdd from history succeeds (exercises ReAddAsync)
            var reAddResp = await this.PostJsonAsync($"/api/v1/downloadhistory/{historyId}/readd", new { });
            reAddResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var reAddDoc = JsonDocument.Parse(await reAddResp.Content.ReadAsStringAsync());
            reAddDoc.RootElement.GetProperty("infoHash").GetString().Should().Be(HistoryHash);
            var reAddedId = reAddDoc.RootElement.GetProperty("id").GetInt32();

            // Clean up the re-added torrent
            await this.DeleteAsync($"/api/v1/torrents/{reAddedId}?deleteData=true");

            // 10. Delete the history record
            var delHistResp = await this.DeleteAsync($"/api/v1/downloadhistory/{historyId}");
            delHistResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }
        finally
        {
            // Ensure no leftover active torrent
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteData=true");
        }

        // 11. Error cases for non-existent records
        var notFoundGet = await this.Client.GetAsync("/api/v1/downloadhistory/999999");
        notFoundGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var notFoundReAdd = await this.PostJsonAsync("/api/v1/downloadhistory/999999/readd", new { });
        notFoundReAdd.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public void DownloadHistory_ServiceDirectMethods_OperatesCorrectly()
    {
        var services = GlobalSetup.Factory.Services;
        var historyService = services.GetService(typeof(IDownloadHistoryService)) as IDownloadHistoryService;
        historyService.Should().NotBeNull();

        var testTorrent = new Torrent
        {
            Id = 998,
            Name = "DirectServiceHistoryMovie.2024",
            InfoHash = "d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2",
            Category = "movies",
            Status = TorrentStatus.Seeding,
            TotalSize = 1048576000,
            Downloaded = 1048576000,
            Uploaded = 2097152000,
            Ratio = 2.0,
            Progress = 1.0,
            SavePath = "/downloads/movies",
            TrackerUrl = "http://tracker.history-direct.org:80/announce",
        };

        // 1. RecordTorrentAdded
        var recorded = historyService.RecordTorrentAdded(
            testTorrent,
            source: "IndexerDirect",
            magnetUrl: "magnet:?xt=urn:btih:d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2&dn=DirectServiceHistoryMovie",
            indexerName: "Prowlarr");
        recorded.Should().NotBeNull();
        recorded.Title.Should().Be("DirectServiceHistoryMovie.2024");

        // 2. GetByInfoHash
        var byHash = historyService.GetByInfoHash("d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2");
        byHash.Should().NotBeNull();
        byHash.Id.Should().Be(recorded.Id);

        // 3. RecordTorrentUpdated
        testTorrent.Uploaded = 3000000000;
        testTorrent.Ratio = 3.0;
        historyService.RecordTorrentUpdated(testTorrent);

        // 4. RecordTorrentRemoved
        historyService.RecordTorrentRemoved(testTorrent, "Archived by user");

        // 5. GetAll with status and query filter
        var all = historyService.GetAll(query: "DirectServiceHistoryMovie", status: null, limit: 10, offset: 0);
        all.Should().NotBeEmpty();

        // 6. PruneHistory
        historyService.PruneHistory(retentionDays: 365);

        // 7. Delete individual record
        historyService.Delete(recorded.Id);
        var afterDelete = historyService.Get(recorded.Id);
        afterDelete.Should().BeNull();
    }
}
