// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class QBittorrentTagsAndMutationsDeepIntegrationTests : IntegrationTestBase
{
    private const string QBitHash = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string QBitName = "QBitMutationTestMovie";

    [Test]
    public async Task QBittorrent_TagsAndCategoryMutations_OperateSuccessfully()
    {
        // 1. Add a torrent first via qBittorrent API
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{QBitHash}&dn={QBitName}"),
            new KeyValuePair<string, string>("category", "initial-qbit-cat"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResp = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 2. Create tags: POST /api/v2/torrents/createTags
            var createTagsResp = await this.Client.PostAsync("/api/v2/torrents/createTags", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "tags", "qbit-tag-1,qbit-tag-2" },
            }));
            createTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Add tags to torrent: POST /api/v2/torrents/addTags
            var addTagsResp = await this.Client.PostAsync("/api/v2/torrents/addTags", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QBitHash },
                { "tags", "qbit-tag-1" },
            }));
            addTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Remove tags from torrent: POST /api/v2/torrents/removeTags
            var removeTagsResp = await this.Client.PostAsync("/api/v2/torrents/removeTags", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QBitHash },
                { "tags", "qbit-tag-1" },
            }));
            removeTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Delete tags: POST /api/v2/torrents/deleteTags
            var deleteTagsResp = await this.Client.PostAsync("/api/v2/torrents/deleteTags", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "tags", "qbit-tag-1,qbit-tag-2" },
            }));
            deleteTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Set category: POST /api/v2/torrents/setCategory
            var setCatResp = await this.Client.PostAsync("/api/v2/torrents/setCategory", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", QBitHash },
                { "category", "updated-qbit-cat" },
            }));
            setCatResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            var delForm = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", QBitHash),
                new KeyValuePair<string, string>("deleteFiles", "true"),
            });
            await this.Client.PostAsync("/api/v2/torrents/delete", delForm);
        }
    }

    [Test]
    public async Task QBittorrent_TransferLimitsAndPriorityMovement_OperatesSuccessfully()
    {
        // Add a torrent via qBittorrent API
        const string hash = "a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2";
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{hash}&dn=QBitPriorityMovie"),
            new KeyValuePair<string, string>("category", "priority-cat"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResp = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 1. Transfer speed limits toggles
            var toggleResp = await this.Client.PostAsync("/api/v2/transfer/toggleSpeedLimitsMode", null);
            toggleResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setModeResp = await this.Client.PostAsync("/api/v2/transfer/setSpeedLimitsMode", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "mode", "1" },
            }));
            setModeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var getModeResp = await this.Client.GetAsync("/api/v2/transfer/speedLimitsMode");
            getModeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setDlResp = await this.Client.PostAsync("/api/v2/transfer/setDownloadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "limit", "10485760" },
            }));
            setDlResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setUlResp = await this.Client.PostAsync("/api/v2/transfer/setUploadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "limit", "5242880" },
            }));
            setUlResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. Priority movement: topPrio, bottomPrio, increasePrio, decreasePrio
            var topPrioResp = await this.Client.PostAsync("/api/v2/torrents/topPrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            topPrioResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var increasePrioResp = await this.Client.PostAsync("/api/v2/torrents/increasePrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            increasePrioResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var decreasePrioResp = await this.Client.PostAsync("/api/v2/torrents/decreasePrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            decreasePrioResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var bottomPrioResp = await this.Client.PostAsync("/api/v2/torrents/bottomPrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            bottomPrioResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Sequential and First/Last piece priority
            var toggleSeqResp = await this.Client.PostAsync("/api/v2/torrents/toggleSequentialDownload", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            toggleSeqResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var toggleFirstLastResp = await this.Client.PostAsync("/api/v2/torrents/toggleFirstLastPiecePrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            toggleFirstLastResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            var delForm = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", "true"),
            });
            await this.Client.PostAsync("/api/v2/torrents/delete", delForm);
        }
    }

    [Test]
    public async Task QBittorrent_TorrentPropertiesAndTrackers_OperatesSuccessfully()
    {
        const string hash = "a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3";
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{hash}&dn=QBitTrackersMovie"),
            new KeyValuePair<string, string>("category", "tracker-cat"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResp = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 1. Get properties
            var propResp = await this.Client.GetAsync($"/api/v2/torrents/properties?hash={hash}");
            propResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var propJson = await propResp.Content.ReadAsStringAsync();
            using var propDoc = JsonDocument.Parse(propJson);
            propDoc.RootElement.TryGetProperty("save_path", out _).Should().BeTrue();

            // 2. Add trackers
            var addTrackersResp = await this.Client.PostAsync("/api/v2/torrents/addTrackers", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", hash },
                { "urls", "udp://tracker.opentrackr.org:1337/announce\nudp://tracker.torrent.eu.org:451/announce" },
            }));
            addTrackersResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Get trackers
            var getTrackersResp = await this.Client.GetAsync($"/api/v2/torrents/trackers?hash={hash}");
            getTrackersResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var trackersJson = await getTrackersResp.Content.ReadAsStringAsync();
            trackersJson.Should().Contain("tracker.opentrackr.org");

            // 4. Edit tracker
            var editTrackerResp = await this.Client.PostAsync("/api/v2/torrents/editTracker", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", hash },
                { "origUrl", "udp://tracker.opentrackr.org:1337/announce" },
                { "newUrl", "udp://tracker.edited.org:1337/announce" },
            }));
            editTrackerResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Remove tracker
            var removeTrackersResp = await this.Client.PostAsync("/api/v2/torrents/removeTrackers", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", hash },
                { "urls", "udp://tracker.edited.org:1337/announce" },
            }));
            removeTrackersResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            var delForm = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", "true"),
            });
            await this.Client.PostAsync("/api/v2/torrents/delete", delForm);
        }
    }

    [Test]
    public async Task QBittorrent_TorrentMutationsAndLifecycle_OperatesSuccessfully()
    {
        const string hash = "a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4";
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{hash}&dn=QBitMutationLifecycleMovie"),
            new KeyValuePair<string, string>("category", "mutation-cat"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResp = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 1. setSavePath & setLocation & setDownloadPath
            var setSaveResp = await this.Client.PostAsync("/api/v2/torrents/setSavePath", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "location", "/tmp/qbit_save" },
            }));
            setSaveResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setLocResp = await this.Client.PostAsync("/api/v2/torrents/setLocation", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "location", "/tmp/qbit_loc" },
            }));
            setLocResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setDlPathResp = await this.Client.PostAsync("/api/v2/torrents/setDownloadPath", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "path", "/tmp/qbit_dl" },
            }));
            setDlPathResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. setShareLimits
            var shareLimResp = await this.Client.PostAsync("/api/v2/torrents/setShareLimits", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "ratioLimit", "2.5" },
                { "seedingTimeLimit", "120" },
                { "maxRatioAction", "0" },
            }));
            shareLimResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. setForceStart & setSuperSeeding
            var forceResp = await this.Client.PostAsync("/api/v2/torrents/setForceStart", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "value", "true" },
            }));
            forceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var superSeedResp = await this.Client.PostAsync("/api/v2/torrents/setSuperSeeding", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "value", "false" },
            }));
            superSeedResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. setSequentialDownload & setFirstLastPiecePrio
            var setSeqResp = await this.Client.PostAsync("/api/v2/torrents/setSequentialDownload", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "value", "true" },
            }));
            setSeqResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setFirstLastResp = await this.Client.PostAsync("/api/v2/torrents/setFirstLastPiecePrio", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "value", "true" },
            }));
            setFirstLastResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Torrent download & upload limits
            var setTorDlResp = await this.Client.PostAsync("/api/v2/torrents/setDownloadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "limit", "1024000" },
            }));
            setTorDlResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setTorUlResp = await this.Client.PostAsync("/api/v2/torrents/setUploadLimit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
                { "limit", "512000" },
            }));
            setTorUlResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Pause, Resume, Recheck, Reannounce, Rename
            var pauseResp = await this.Client.PostAsync("/api/v2/torrents/pause", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var resumeResp = await this.Client.PostAsync("/api/v2/torrents/resume", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var recheckResp = await this.Client.PostAsync("/api/v2/torrents/recheck", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var reannResp = await this.Client.PostAsync("/api/v2/torrents/reannounce", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hashes", hash },
            }));
            reannResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var renameResp = await this.Client.PostAsync("/api/v2/torrents/rename", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "hash", hash },
                { "name", "RenamedQBitMovie" },
            }));
            renameResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            var delForm = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", "true"),
            });
            await this.Client.PostAsync("/api/v2/torrents/delete", delForm);
        }
    }

    [Test]
    public async Task QBittorrent_SyncMaindataAndPeers_DeltaSync_OperatesSuccessfully()
    {
        const string hash = "a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5";
        var addForm = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{hash}&dn=QBitSyncMovie"),
            new KeyValuePair<string, string>("category", "sync-cat"),
            new KeyValuePair<string, string>("paused", "true"),
        });
        var addResp = await this.Client.PostAsync("/api/v2/torrents/add", addForm);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 1. Initial maindata sync (rid=0)
            var initMainResp = await this.Client.GetAsync("/api/v2/sync/maindata?rid=0");
            initMainResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var initMainJson = await initMainResp.Content.ReadAsStringAsync();
            using var initDoc = JsonDocument.Parse(initMainJson);
            initDoc.RootElement.TryGetProperty("rid", out var ridProp).Should().BeTrue();
            var currentRid = ridProp.GetInt32();

            // 2. Incremental maindata sync (rid > 0)
            var deltaMainResp = await this.Client.GetAsync($"/api/v2/sync/maindata?rid={currentRid}");
            deltaMainResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Initial peers sync (rid=0)
            var initPeersResp = await this.Client.GetAsync($"/api/v2/sync/torrentPeers?hash={hash}&rid=0");
            initPeersResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var initPeersJson = await initPeersResp.Content.ReadAsStringAsync();
            using var initPeersDoc = JsonDocument.Parse(initPeersJson);
            initPeersDoc.RootElement.TryGetProperty("full_update", out var fullUpProp).Should().BeTrue();
            fullUpProp.GetBoolean().Should().BeTrue();

            // 4. Incremental peers sync (rid > 0)
            var deltaPeersResp = await this.Client.GetAsync($"/api/v2/sync/torrentPeers?hash={hash}&rid=1");
            deltaPeersResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            var delForm = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", "true"),
            });
            await this.Client.PostAsync("/api/v2/torrents/delete", delForm);
        }
    }

    [Test]
    public async Task QBittorrent_SetPreferences_OperatesSuccessfully()
    {
        var services = GlobalSetup.Factory.Services;
        var configService = (NzbDrone.Core.Configuration.IConfigService)services.GetService(typeof(NzbDrone.Core.Configuration.IConfigService))!;
        var originalDownloadDir = configService.DownloadDir;
        var originalPort = configService.ListeningPort;

        var tempSave = Path.Combine(Path.GetTempPath(), "qbit_save_pref");
        try
        {
            // 1. Set preferences via JSON payload
            var prefsForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "json", $"{{\"save_path\":\"{tempSave.Replace("\\", "\\\\")}\",\"encryption\":1,\"listen_port\":6881,\"dht\":true}}" },
            });
            var setPrefResp = await this.Client.PostAsync("/api/v2/app/setPreferences", prefsForm);
            setPrefResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. Default save path endpoint
            var defaultPathResp = await this.Client.GetAsync("/api/v2/app/defaultSavePath");
            defaultPathResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["DownloadDir"] = originalDownloadDir,
                ["ListeningPort"] = originalPort,
            });
        }
    }
}
