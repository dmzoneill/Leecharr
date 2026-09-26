// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class QBittorrentComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string QbitHash = "b222222222222222222222222222222222222222";
    private const string QbitName = "QBitComprehensiveMovie";

    [SetUp]
    public async Task SetUp()
    {
        // Log in to qBittorrent emulation
        var loginResp = await this.Client.PostAsync(
            "/api/v2/auth/login",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "adminadmin"),
            }));
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QBittorrent_AppAndTransferEndpoints_ReturnValidData()
    {
        // 1. Version endpoints
        var versionResp = await this.GetAsync("/api/v2/app/version");
        versionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var versionStr = await versionResp.Content.ReadAsStringAsync();
        versionStr.Should().NotBeNullOrWhiteSpace();

        var apiVersionResp = await this.GetAsync("/api/v2/app/webapiVersion");
        apiVersionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiVersionStr = await apiVersionResp.Content.ReadAsStringAsync();
        apiVersionStr.Should().NotBeNullOrWhiteSpace();

        var defaultSavePathResp = await this.GetAsync("/api/v2/app/defaultSavePath");
        defaultSavePathResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var defaultSavePathStr = await defaultSavePathResp.Content.ReadAsStringAsync();
        defaultSavePathStr.Should().NotBeNull();

        // 2. Preferences
        var prefsResp = await this.GetAsync("/api/v2/app/preferences");
        prefsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefsJson = await prefsResp.Content.ReadAsStringAsync();
        using var prefsDoc = JsonDocument.Parse(prefsJson);
        prefsDoc.RootElement.TryGetProperty("save_path", out _).Should().BeTrue();

        // 3. Set Preferences
        var setPrefsResp = await this.Client.PostAsync(
            "/api/v2/app/setPreferences",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("json", "{\"max_connec\": 500}"),
            }));
        setPrefsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Transfer Info
        var transferResp = await this.GetAsync("/api/v2/transfer/info");
        transferResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var transferJson = await transferResp.Content.ReadAsStringAsync();
        using var transferDoc = JsonDocument.Parse(transferJson);
        transferDoc.RootElement.TryGetProperty("connection_status", out _).Should().BeTrue();

        // 5. Speed limits mode
        var speedModeResp = await this.GetAsync("/api/v2/transfer/speedLimitsMode");
        speedModeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var toggleModeResp = await this.Client.PostAsync("/api/v2/transfer/toggleSpeedLimitsMode", new FormUrlEncodedContent(new KeyValuePair<string, string>[0]));
        toggleModeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QBittorrent_CategoriesAndTagsLifecycle_Succeeds()
    {
        const string catName = "qbit-integ-cat";
        const string tagName = "qbit-integ-tag";

        // 1. Create Category
        var createCatResp = await this.Client.PostAsync(
            "/api/v2/torrents/createCategory",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("category", catName),
                new KeyValuePair<string, string>("savePath", "/downloads/" + catName),
            }));
        createCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Verify in Categories list
        var catsResp = await this.GetAsync("/api/v2/torrents/categories");
        catsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var catsJson = await catsResp.Content.ReadAsStringAsync();
        using var catsDoc = JsonDocument.Parse(catsJson);
        catsDoc.RootElement.TryGetProperty(catName, out var foundCat).Should().BeTrue();
        foundCat.GetProperty("savePath").GetString().Should().Be("/downloads/" + catName);

        // 3. Edit Category
        var editCatResp = await this.Client.PostAsync(
            "/api/v2/torrents/editCategory",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("category", catName),
                new KeyValuePair<string, string>("savePath", "/downloads/" + catName + "-edited"),
            }));
        editCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Remove Category
        var removeCatResp = await this.Client.PostAsync(
            "/api/v2/torrents/removeCategories",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("categories", catName),
            }));
        removeCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Create Tag
        var createTagResp = await this.Client.PostAsync(
            "/api/v2/torrents/createTags",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("tags", tagName),
            }));
        createTagResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Delete Tag
        var deleteTagResp = await this.Client.PostAsync(
            "/api/v2/torrents/deleteTags",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("tags", tagName),
            }));
        deleteTagResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QBittorrent_TorrentActions_PauseResumeRecheckCategoryAndOptions()
    {
        // 1. Add torrent via qBittorrent add endpoint
        var addResp = await this.Client.PostAsync(
            "/api/v2/torrents/add",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("urls", $"magnet:?xt=urn:btih:{QbitHash}&dn={QbitName}"),
                new KeyValuePair<string, string>("category", "movies"),
                new KeyValuePair<string, string>("paused", "true"),
            }));
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 2. Pause
            var pauseResp = await this.Client.PostAsync(
                "/api/v2/torrents/pause",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                }));
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Resume
            var resumeResp = await this.Client.PostAsync(
                "/api/v2/torrents/resume",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                }));
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Recheck
            var recheckResp = await this.Client.PostAsync(
                "/api/v2/torrents/recheck",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                }));
            recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Set Category
            var setCatResp = await this.Client.PostAsync(
                "/api/v2/torrents/setCategory",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                    new KeyValuePair<string, string>("category", "tv"),
                }));
            setCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Add Tags & Remove Tags
            var addTagResp = await this.Client.PostAsync(
                "/api/v2/torrents/addTags",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                    new KeyValuePair<string, string>("tags", "action,thriller"),
                }));
            addTagResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var removeTagResp = await this.Client.PostAsync(
                "/api/v2/torrents/removeTags",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                    new KeyValuePair<string, string>("tags", "action"),
                }));
            removeTagResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verify tags query returns thriller
            var tagsResp = await this.GetAsync("/api/v2/torrents/tags");
            tagsResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var tagsJson = await tagsResp.Content.ReadAsStringAsync();
            using var tagsDoc = JsonDocument.Parse(tagsJson);
            var tagList = new List<string>();
            foreach (var t in tagsDoc.RootElement.EnumerateArray())
            {
                tagList.Add(t.GetString()!);
            }
            tagList.Should().Contain("thriller");

            // 7. Toggle Sequential Download
            var toggleSeqResp = await this.Client.PostAsync(
                "/api/v2/torrents/toggleSequentialDownload",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                }));
            toggleSeqResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 8. Toggle First/Last Piece Priority
            var toggleFlpResp = await this.Client.PostAsync(
                "/api/v2/torrents/toggleFirstLastPiecePrio",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                }));
            toggleFlpResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 9. Delete torrent
            var deleteResp = await this.Client.PostAsync(
                "/api/v2/torrents/delete",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", QbitHash),
                    new KeyValuePair<string, string>("deleteFiles", "false"),
                }));
            deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
