// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class QBittorrentExtendedApiIntegrationTests : IntegrationTestBase
{
    private const string QHash = "e555555555555555555555555555555555555555";
    private const string QName = "QBitExtendedMovie";

    [Test]
    public async Task QBittorrent_SyncMaindataAndPeers_ReturnsServerStateAndDeltas()
    {
        // 1. Initial maindata call (rid = 0)
        var resp1 = await this.Client.GetAsync("/api/v2/sync/maindata?rid=0");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        var root1 = doc1.RootElement;
        root1.GetProperty("rid").GetInt32().Should().BeGreaterThan(0);
        root1.GetProperty("full_update").GetBoolean().Should().BeTrue();
        root1.TryGetProperty("server_state", out _).Should().BeTrue();

        var rid = root1.GetProperty("rid").GetInt32();

        // 2. Incremental maindata call (rid = previous rid)
        var resp2 = await this.Client.GetAsync($"/api/v2/sync/maindata?rid={rid}");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
        doc2.RootElement.GetProperty("rid").GetInt32().Should().BeGreaterThan(rid);

        // 3. sync/torrentPeers for a non-existent and existing torrent
        var notFoundPeers = await this.Client.GetAsync("/api/v2/sync/torrentPeers?hash=ffffffffffffffffffffffffffffffffffffffff");
        notFoundPeers.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task QBittorrent_TransferInfoAndSpeedLimits_TogglesAndSets()
    {
        // 1. transfer/info
        var infoResp = await this.Client.GetAsync("/api/v2/transfer/info");
        infoResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var docInfo = JsonDocument.Parse(await infoResp.Content.ReadAsStringAsync());
        docInfo.RootElement.TryGetProperty("dl_info_speed", out _).Should().BeTrue();

        // 2. transfer/speedLimitsMode
        var modeResp = await this.Client.GetAsync("/api/v2/transfer/speedLimitsMode");
        modeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. transfer/toggleSpeedLimitsMode
        var toggleResp = await this.Client.PostAsync("/api/v2/transfer/toggleSpeedLimitsMode", new FormUrlEncodedContent(new Dictionary<string, string>()));
        toggleResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. transfer/setSpeedLimitsMode
        var setModeResp = await this.Client.PostAsync("/api/v2/transfer/setSpeedLimitsMode", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "mode", "0" },
        }));
        setModeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. transfer/setDownloadLimit & transfer/setUploadLimit
        var setDlResp = await this.Client.PostAsync("/api/v2/transfer/setDownloadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "limit", "1048576" },
        }));
        setDlResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var setUpResp = await this.Client.PostAsync("/api/v2/transfer/setUploadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "limit", "524288" },
        }));
        setUpResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QBittorrent_AppPreferencesAndLogs_ReturnsExpectedData()
    {
        // 1. app/version & app/webapiVersion
        var verResp = await this.Client.GetAsync("/api/v2/app/version");
        verResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var webApiResp = await this.Client.GetAsync("/api/v2/app/webapiVersion");
        webApiResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. app/defaultSavePath
        var pathResp = await this.Client.GetAsync("/api/v2/app/defaultSavePath");
        pathResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. app/preferences
        var prefResp = await this.Client.GetAsync("/api/v2/app/preferences");
        prefResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. app/setPreferences
        var setPrefResp = await this.Client.PostAsync("/api/v2/app/setPreferences", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "json", "{\"max_connec\": 500}" },
        }));
        setPrefResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. log/main
        var logMainResp = await this.Client.GetAsync("/api/v2/log/main?normal=true&info=true&warning=true&error=true");
        logMainResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. log/peers
        var logPeersResp = await this.Client.GetAsync("/api/v2/log/peers");
        logPeersResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QBittorrent_CategoriesAndTags_CreateEditDeleteMatrix()
    {
        // 1. createCategory
        var createCatResp = await this.Client.PostAsync("/api/v2/torrents/createCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "category", "qbit-test-category" },
            { "savePath", "/downloads/qbit-test" },
        }));
        createCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. categories list
        var catListResp = await this.Client.GetAsync("/api/v2/torrents/categories");
        catListResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var catStr = await catListResp.Content.ReadAsStringAsync();
        catStr.Should().Contain("qbit-test-category");

        // 3. editCategory
        var editCatResp = await this.Client.PostAsync("/api/v2/torrents/editCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "category", "qbit-test-category" },
            { "savePath", "/downloads/qbit-updated" },
        }));
        editCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. removeCategories
        var removeCatResp = await this.Client.PostAsync("/api/v2/torrents/removeCategories", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "categories", "qbit-test-category" },
        }));
        removeCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. createTags
        var createTagsResp = await this.Client.PostAsync("/api/v2/torrents/createTags", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "tags", "tag-alpha,tag-beta" },
        }));
        createTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. get tags
        var tagsResp = await this.Client.GetAsync("/api/v2/torrents/tags");
        tagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. deleteTags
        var deleteTagsResp = await this.Client.PostAsync("/api/v2/torrents/deleteTags", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "tags", "tag-alpha,tag-beta" },
        }));
        deleteTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QBittorrent_TorrentOperationsMatrix_PriorityTrackersLimits()
    {
        // Add a torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{QHash}&dn={QName}",
            category = "q-matrix-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 1. Queue priority changes: topPrio, bottomPrio, increasePrio, decreasePrio
            var prioEndpoints = new[] { "topPrio", "increasePrio", "decreasePrio", "bottomPrio" };
            foreach (var ep in prioEndpoints)
            {
                var prioResp = await this.Client.PostAsync($"/api/v2/torrents/{ep}", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "hashes", QHash },
                }));
                prioResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 2. Torrent download & upload limits
            var setDlResp = await this.Client.PostAsync("/api/v2/torrents/setDownloadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "limit", "2048000" },
            }));
            setDlResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setUpResp = await this.Client.PostAsync("/api/v2/torrents/setUploadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "limit", "1024000" },
            }));
            setUpResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Sequential and First/Last piece priority
            var seqResp = await this.Client.PostAsync("/api/v2/torrents/toggleSequentialDownload", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
            }));
            seqResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setSeqResp = await this.Client.PostAsync("/api/v2/torrents/setSequentialDownload", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "value", "true" },
            }));
            setSeqResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var flResp = await this.Client.PostAsync("/api/v2/torrents/toggleFirstLastPiecePrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
            }));
            flResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setFlResp = await this.Client.PostAsync("/api/v2/torrents/setFirstLastPiecePrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "value", "true" },
            }));
            setFlResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Force start and super seeding
            var forceResp = await this.Client.PostAsync("/api/v2/torrents/setForceStart", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "value", "true" },
            }));
            forceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var superSeedResp = await this.Client.PostAsync("/api/v2/torrents/setSuperSeeding", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "value", "false" },
            }));
            superSeedResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Trackers: addTrackers, editTracker, removeTrackers
            var addTrackersResp = await this.Client.PostAsync("/api/v2/torrents/addTrackers", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", QHash },
                { "urls", "http://tracker.example.com/announce\nudp://tracker.openbittorrent.com:80/announce" },
            }));
            addTrackersResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var editTrackerResp = await this.Client.PostAsync("/api/v2/torrents/editTracker", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", QHash },
                { "origUrl", "http://tracker.example.com/announce" },
                { "newUrl", "http://tracker2.example.com/announce" },
            }));
            editTrackerResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var removeTrackerResp = await this.Client.PostAsync("/api/v2/torrents/removeTrackers", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", QHash },
                { "urls", "http://tracker2.example.com/announce" },
            }));
            removeTrackerResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Add peers, recheck, reannounce
            var addPeersResp = await this.Client.PostAsync("/api/v2/torrents/addPeers", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "peers", "192.168.1.100:6881|192.168.1.101:6881" },
            }));
            addPeersResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var recheckResp = await this.Client.PostAsync("/api/v2/torrents/recheck", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
            }));
            recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var reannounceResp = await this.Client.PostAsync("/api/v2/torrents/reannounce", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
            }));
            reannounceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 7. Tags on torrent: addTags, removeTags
            var addTagsResp = await this.Client.PostAsync("/api/v2/torrents/addTags", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "tags", "priority-tag,stream-ready" },
            }));
            addTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var removeTagsResp = await this.Client.PostAsync("/api/v2/torrents/removeTags", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "tags", "priority-tag" },
            }));
            removeTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 8. setLocation, setSavePath, setDownloadPath (use existing savePath to avoid moving non-existent payload files)
            var currentTorrentResp = await this.Client.GetAsync($"/api/v1/torrents/{torrentId}");
            var currentDoc = JsonDocument.Parse(await currentTorrentResp.Content.ReadAsStringAsync());
            var currentSavePath = currentDoc.RootElement.GetProperty("savePath").GetString() ?? "/downloads";

            var setLocResp = await this.Client.PostAsync("/api/v2/torrents/setLocation", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "location", currentSavePath },
            }));
            setLocResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setSaveResp = await this.Client.PostAsync("/api/v2/torrents/setSavePath", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "savePath", currentSavePath },
            }));
            setSaveResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setDlPathResp = await this.Client.PostAsync("/api/v2/torrents/setDownloadPath", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "downloadPath", currentSavePath },
            }));
            setDlPathResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 9. setShareLimits
            var shareLimResp = await this.Client.PostAsync("/api/v2/torrents/setShareLimits", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QHash },
                { "ratioLimit", "3.0" },
                { "seedingTimeLimit", "180" },
            }));
            shareLimResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }
}
