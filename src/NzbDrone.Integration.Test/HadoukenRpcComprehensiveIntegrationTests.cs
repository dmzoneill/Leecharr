// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using MonoTorrent.BEncoding;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class HadoukenRpcComprehensiveIntegrationTests : IntegrationTestBase
{
    private readonly List<string> trackedHashes = new();

    [TearDown]
    public async Task CleanupTrackedTorrentsAsync()
    {
        foreach (var hash in this.trackedHashes)
        {
            await this.CleanupTorrentByHashAsync(hash);
        }

        this.trackedHashes.Clear();
    }

    [Test]
    public async Task Core_GetSystemInfoAndGetVersion_ReturnsSystemDetailsAndVersion()
    {
        // 1. core.getsysteminfo
        var sysInfoDoc = await this.SendHadoukenRpcAsync("core.getsysteminfo");
        AssertSuccess(sysInfoDoc);
        var sysResult = sysInfoDoc.RootElement.GetProperty("result");
        sysResult.GetProperty("committish").GetString().Should().Be("5.3.0");
        sysResult.GetProperty("branch").GetString().Should().Be("master");
        var versions = sysResult.GetProperty("versions");
        versions.GetProperty("hadouken").GetString().Should().Be("5.3.0");
        versions.GetProperty("libtorrent").GetString().Should().Be("1.2.14");

        // 2. core.get_system_info alias
        var sysInfoAliasDoc = await this.SendHadoukenRpcAsync("core.get_system_info");
        AssertSuccess(sysInfoAliasDoc);
        sysInfoAliasDoc.RootElement.GetProperty("result").GetProperty("committish").GetString().Should().Be("5.3.0");

        // 3. core.getversion
        var verDoc = await this.SendHadoukenRpcAsync("core.getversion");
        AssertSuccess(verDoc);
        verDoc.RootElement.GetProperty("result").GetString().Should().Be("5.3.0");

        // 4. hadouken.getversion alias
        var hadoukenVerDoc = await this.SendHadoukenRpcAsync("hadouken.getversion");
        AssertSuccess(hadoukenVerDoc);
        hadoukenVerDoc.RootElement.GetProperty("result").GetString().Should().Be("5.3.0");
    }

    [Test]
    public async Task WebUi_GetSettings_ReturnsBitTorrentDefaultSavePath()
    {
        // 1. webui.getsettings
        var settingsDoc = await this.SendHadoukenRpcAsync("webui.getsettings");
        AssertSuccess(settingsDoc);
        var res = settingsDoc.RootElement.GetProperty("result");
        res.TryGetProperty("bittorrent.default_save_path", out var savePathElem).Should().BeTrue();
        savePathElem.GetString().Should().NotBeNull();

        // 2. webui.get_settings alias
        var settingsAliasDoc = await this.SendHadoukenRpcAsync("webui.get_settings");
        AssertSuccess(settingsAliasDoc);
        settingsAliasDoc.RootElement.GetProperty("result").TryGetProperty("bittorrent.default_save_path", out _).Should().BeTrue();
    }

    [Test]
    public async Task WebUi_AddTorrent_WithMagnetLinkUrl_AddsTorrentAndReturnsInfoHash()
    {
        const string hash = "d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1";
        this.trackedHashes.Add(hash);

        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=HadoukenMagnetUrlTest";
        var options = new
        {
            save_path = "/downloads/hadouken_magnet",
            label = "hadouken-cat",
            paused = true,
        };

        var addDoc = await this.SendHadoukenRpcAsync(
            "webui.addtorrent",
            new object[] { "url", magnet, options },
            id: 101);

        AssertSuccess(addDoc);
        var returnedHash = addDoc.RootElement.GetProperty("result").GetString();
        returnedHash.Should().NotBeNullOrWhiteSpace();
        string.Equals(returnedHash, hash, StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        // Verify the torrent exists via Leecharr REST API
        var torrents = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        var added = torrents.FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        added.Should().NotBeNull();
        added!.Category.Should().Be("hadouken-cat");

        await this.CleanupTorrentByHashAsync(hash);
    }

    [Test]
    public async Task WebUi_AddTorrent_WithBase64File_AddsTorrentAndReturnsInfoHash()
    {
        var torrentBytes = CreateTestTorrentBytes("hadouken_sample.bin");
        var base64Data = Convert.ToBase64String(torrentBytes);

        var options = new
        {
            save_path = "/downloads/hadouken_b64",
            tags = new[] { "hadouken-file-cat" },
            paused = true,
        };

        var addDoc = await this.SendHadoukenRpcAsync(
            "webui.addtorrent",
            new object[] { "file", base64Data, options },
            id: 102);

        AssertSuccess(addDoc);
        var returnedHash = addDoc.RootElement.GetProperty("result").GetString();
        returnedHash.Should().NotBeNullOrWhiteSpace();
        returnedHash!.Length.Should().Be(40);
        this.trackedHashes.Add(returnedHash);

        // Verify torrent is queryable
        var torrents = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        var added = torrents.FirstOrDefault(t => string.Equals(t.InfoHash, returnedHash, StringComparison.OrdinalIgnoreCase));
        added.Should().NotBeNull();
        added!.Category.Should().Be("hadouken-file-cat");

        await this.CleanupTorrentByHashAsync(returnedHash);
    }

    [Test]
    public async Task WebUi_List_ReturnsTorrentRowsWithMetadata()
    {
        const string hash = "d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2";
        this.trackedHashes.Add(hash);

        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=HadoukenListRowTest";
        var options = new
        {
            save_path = "/downloads/hadouken_list",
            label = "list-category",
            paused = true,
        };

        var addDoc = await this.SendHadoukenRpcAsync("webui.addtorrent", new object[] { "url", magnet, options });
        AssertSuccess(addDoc);

        // Call webui.list
        var listDoc = await this.SendHadoukenRpcAsync("webui.list", id: 200);
        AssertSuccess(listDoc);
        var resultElem = listDoc.RootElement.GetProperty("result");
        resultElem.GetProperty("torrentc").GetString().Should().Be("1");

        var torrentsElem = resultElem.GetProperty("torrents");
        torrentsElem.ValueKind.Should().Be(JsonValueKind.Array);

        JsonElement? matchingRow = null;
        foreach (var row in torrentsElem.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 27)
            {
                var rowHash = row[0].GetString();
                if (string.Equals(rowHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    matchingRow = row;
                    break;
                }
            }
        }

        matchingRow.Should().NotBeNull("Added torrent must appear in webui.list rows");
        var matched = matchingRow!.Value;
        matched[0].GetString().Should().Be(hash.ToUpperInvariant());
        matched[1].GetInt32().Should().BeOneOf(128, 160);
        matched[2].GetString().Should().Be("HadoukenListRowTest");
        matched[11].GetString().Should().Be("list-category");

        await this.CleanupTorrentByHashAsync(hash);
    }

    [Test]
    public async Task WebUi_Perform_ExecutesAllSpecifiedActions()
    {
        const string hash1 = "d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3";
        const string hash2 = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
        this.trackedHashes.Add(hash1);
        this.trackedHashes.Add(hash2);

        // Add two torrents to perform actions on
        var magnet1 = $"magnet:?xt=urn:btih:{hash1}&dn=PerformActionTorrent1";
        await this.SendHadoukenRpcAsync("webui.addtorrent", new object[] { "url", magnet1, new { paused = true } });

        var magnet2 = $"magnet:?xt=urn:btih:{hash2}&dn=PerformActionTorrent2";
        await this.SendHadoukenRpcAsync("webui.addtorrent", new object[] { "url", magnet2, new { paused = true } });

        // 1. pause
        var pauseDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "pause", new[] { hash1 } });
        pauseDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 2. resume
        var resumeDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "resume", new[] { hash1 } });
        resumeDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 3. recheck
        var recheckDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "recheck", new[] { hash1 } });
        recheckDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 4. queueup
        var queueUpDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "queueup", new[] { hash1 } });
        queueUpDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 5. queuedown
        var queueDownDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "queuedown", new[] { hash1 } });
        queueDownDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 6. queuetop
        var queueTopDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "queuetop", new[] { hash1 } });
        queueTopDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 7. queuebottom
        var queueBottomDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "queuebottom", new[] { hash1 } });
        queueBottomDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 8. stop
        var stopDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "stop", new[] { hash1 } });
        stopDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 9. remove (deletes torrent metadata without data)
        var removeDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "remove", new[] { hash1 } });
        removeDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        var torrentsAfterRemove = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        torrentsAfterRemove.Any(t => string.Equals(t.InfoHash, hash1, StringComparison.OrdinalIgnoreCase)).Should().BeFalse();

        // 10. removedata (deletes torrent metadata and data)
        var removeDataDoc = await this.SendHadoukenRpcAsync("webui.perform", new object[] { "removedata", new[] { hash2 } });
        removeDataDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        var torrentsAfterRemoveData = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        torrentsAfterRemoveData.Any(t => string.Equals(t.InfoHash, hash2, StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Test]
    public async Task Torrents_AddUriPauseResumeSetPropsAndDelete_ExecutesFullLifecycle()
    {
        const string hash = "d5d5d5d5d5d5d5d5d5d5d5d5d5d5d5d5d5d5d5d5";
        this.trackedHashes.Add(hash);

        // 1. torrents.adduri
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=TorrentsNamespaceTest";
        var addOptions = new
        {
            save_path = "/downloads/adduri_test",
            tags = new[] { "initial-category" },
            paused = false,
        };

        var addDoc = await this.SendHadoukenRpcAsync("torrents.adduri", new object[] { magnet, addOptions });
        AssertSuccess(addDoc);
        var addedHash = addDoc.RootElement.GetProperty("result").GetString();
        string.Equals(addedHash, hash, StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        // 2. torrents.pause
        var pauseDoc = await this.SendHadoukenRpcAsync("torrents.pause", new object[] { hash });
        pauseDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 3. torrents.resume
        var resumeDoc = await this.SendHadoukenRpcAsync("torrents.resume", new object[] { hash });
        resumeDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 4. torrents.set_props
        var props = new
        {
            tags = new[] { "updated-category" },
            download_limit = 1048576,
            upload_limit = 524288,
        };

        var setPropsDoc = await this.SendHadoukenRpcAsync("torrents.set_props", new object[] { hash, props });
        setPropsDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // Verify props update in backend
        var torrents = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        var updated = torrents.FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        updated.Should().NotBeNull();
        updated!.Category.Should().Be("updated-category");
        updated.DownloadLimit.Should().Be(1048576);
        updated.UploadLimit.Should().Be(524288);

        // Also test torrents.setprops alias
        var setPropsAliasDoc = await this.SendHadoukenRpcAsync("torrents.setprops", new object[] { hash, new { download_limit = 2097152 } });
        setPropsAliasDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // 5. torrents.delete with delete_data object
        var deleteDoc = await this.SendHadoukenRpcAsync("torrents.delete", new object[] { hash, new { delete_data = true } });
        deleteDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        // Verify torrent deleted
        var torrentsAfterDelete = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
        torrentsAfterDelete.Any(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Test]
    public async Task HadoukenRpc_AlternativeRoutesAndAuthFlow_Succeeds()
    {
        // Test route aliases: api/hadouken, api/rpc, hadouken/rpc, hadouken
        var routes = new[] { "/api/hadouken", "/api/rpc", "/hadouken/rpc", "/hadouken" };
        foreach (var route in routes)
        {
            var req = new
            {
                method = "core.getversion",
                @params = new object[0],
                id = 999,
            };
            var resp = await this.PostJsonAsync(route, req);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("result").GetString().Should().Be("5.3.0");
        }

        // Test auth.login and auth.logout
        var loginDoc = await this.SendHadoukenRpcAsync("auth.login", new object[] { "dummy_token_or_pass" });
        AssertSuccess(loginDoc);
        var token = loginDoc.RootElement.GetProperty("result").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        var logoutDoc = await this.SendHadoukenRpcAsync("auth.logout", new object[] { token });
        logoutDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();
    }

    private static byte[] CreateTestTorrentBytes(string fileName)
    {
        var pieceLength = 16384;
        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i + 7);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString(fileName) },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(16384) },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
            { "created by", new BEncodedString("HadoukenIntegrationTests") },
        };

        return rootDict.Encode();
    }

    private static void AssertSuccess(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null)
        {
            Assert.Fail($"RPC returned error: {err.GetString()}");
        }
    }

    private async Task<JsonDocument> SendHadoukenRpcAsync(string method, object parameters = null, object id = null)
    {
        var payload = new
        {
            method = method,
            @params = parameters ?? new object[0],
            id = id ?? 1,
        };

        var response = await this.PostJsonAsync("/hadouken/api", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private async Task CleanupTorrentByHashAsync(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        try
        {
            var torrents = await this.GetJsonAsync<List<TorrentResource>>("/api/v1/torrents");
            var match = torrents.FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                await this.DeleteAsync($"/api/v1/torrents/{match.Id}?deleteFiles=true");
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
