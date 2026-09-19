// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Torrents;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorrentEngineHotSwapIntegrationTest : IntegrationTestBase
{
    [Test]
    public async Task HotSwap_between_MonoTorrent_and_LibTorrent_physically_switches_backend_and_migrates_transfers()
    {
        var engineManager = GlobalSetup.Factory.Services.GetRequiredService<ITorrentEngineManager>();
        engineManager.Should().NotBeNull();

        // 1. Ensure starting state is MonoTorrent
        if (!string.Equals(engineManager.ActiveEngineId, "MonoTorrent", StringComparison.OrdinalIgnoreCase))
        {
            var resetReq = new { engineId = "MonoTorrent", preserveTransfers = true };
            var resetResp = await this.PostJsonAsync("/api/v1/torrentengine/switch", resetReq);
            resetResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        engineManager.ActiveEngineId.Should().Be("MonoTorrent");
        engineManager.ActiveEngine.Should().BeOfType<MonoTorrentDownloadEngine>();
        var monoEngine = engineManager.GetEngine("MonoTorrent");
        var libTorrentEngine = engineManager.GetEngine("LibTorrent");
        monoEngine.Should().NotBeNull();
        libTorrentEngine.Should().NotBeNull();

        // 2. Add a test torrent via REST API
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234599&dn=HotSwapVerificationTorrent"), "magnetUrl");
        form.Add(new StringContent("tv"), "category");
        form.Add(new StringContent("true"), "paused");

        var addResponse = await this.Client.PostAsync("/api/v1/torrents", form);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = Deserialize<TorrentResource>(await addResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("HotSwapVerificationTorrent");

        try
        {
            // 3. Verify torrent is physically present in MonoTorrent's backend engine
            var monoTask = monoEngine.GetTask(created.Id);
            monoTask.Should().NotBeNull("MonoTorrent backend engine must physically register the newly added torrent");
            monoTask.InfoHash.Should().BeEquivalentTo("0123456789abcdef0123456789abcdef01234599");

            // 4. Hot-swap engine to LibTorrent via API with preserveTransfers = true
            var switchReq = new { engineId = "LibTorrent", preserveTransfers = true };
            var switchResponse = await this.PostJsonAsync("/api/v1/torrentengine/switch", switchReq);
            switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var switchResult = Deserialize<JsonElement>(await switchResponse.Content.ReadAsStringAsync());
            switchResult.GetProperty("success").GetBoolean().Should().BeTrue();
            switchResult.GetProperty("activeEngine").GetString().Should().Be("LibTorrent");

            // 5. Physical Backend Verification: LibTorrent is now active
            engineManager.ActiveEngineId.Should().Be("LibTorrent");
            engineManager.ActiveEngine.Should().BeOfType<LibTorrentDownloadEngine>();
            engineManager.ActiveEngine.Should().BeSameAs(libTorrentEngine);

            // Previous engine tasks should be stopped
            monoEngine.GetAllTasks().Should().OnlyContain(t => t.Status == TorrentStatus.Stopped, "MonoTorrent tasks should be stopped when hot-swapping away");

            // LibTorrent engine should physically have the rehydrated torrent
            var libTask = libTorrentEngine.GetTask(created.Id);
            libTask.Should().NotBeNull("LibTorrent backend engine must physically contain the rehydrated transfer");
            libTask.InfoHash.Should().BeEquivalentTo("0123456789abcdef0123456789abcdef01234599");

            // 6. Verify that physical control operations on the torrent actually affect the LibTorrent engine task
            var pauseResp = await this.PostJsonAsync($"/api/v1/torrents/{created.Id}/pause", new { });
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);
            libTask.Status.Should().Be(TorrentStatus.Paused, "LibTorrent backend task status must physically reflect the pause operation");

            var resumeResp = await this.PostJsonAsync($"/api/v1/torrents/{created.Id}/resume", new { });
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            new[] { TorrentStatus.Downloading, TorrentStatus.Seeding }.Should().Contain(libTask.Status, "LibTorrent backend task status must physically reflect the resume operation");

            // 7. Hot-swap back to MonoTorrent via API with preserveTransfers = true
            var switchBackReq = new { engineId = "MonoTorrent", preserveTransfers = true };
            var switchBackResponse = await this.PostJsonAsync("/api/v1/torrentengine/switch", switchBackReq);
            switchBackResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var switchBackResult = Deserialize<JsonElement>(await switchBackResponse.Content.ReadAsStringAsync());
            switchBackResult.GetProperty("success").GetBoolean().Should().BeTrue();
            switchBackResult.GetProperty("activeEngine").GetString().Should().Be("MonoTorrent");

            // 8. Physical Backend Verification: MonoTorrent is back active and rehydrated
            engineManager.ActiveEngineId.Should().Be("MonoTorrent");
            engineManager.ActiveEngine.Should().BeOfType<MonoTorrentDownloadEngine>();
            engineManager.ActiveEngine.Should().BeSameAs(monoEngine);

            // LibTorrent tasks should be drained
            libTorrentEngine.GetAllTasks().Should().BeEmpty("LibTorrent tasks should be drained during hot-swap back to MonoTorrent");

            // MonoTorrent should physically have the rehydrated torrent again
            var rehydratedMonoTask = monoEngine.GetTask(created.Id);
            rehydratedMonoTask.Should().NotBeNull("MonoTorrent backend engine must physically receive the rehydrated transfer");
            rehydratedMonoTask.InfoHash.Should().BeEquivalentTo("0123456789abcdef0123456789abcdef01234599");
        }
        finally
        {
            // 9. Clean up test torrent
            await this.DeleteAsync($"/api/v1/torrents/{created.Id}?deleteFiles=false");

            // Ensure MonoTorrent is the active engine on exit
            if (!string.Equals(engineManager.ActiveEngineId, "MonoTorrent", StringComparison.OrdinalIgnoreCase))
            {
                await this.PostJsonAsync("/api/v1/torrentengine/switch", new { engineId = "MonoTorrent", preserveTransfers = false });
            }
        }
    }

    [Test]
    public async Task Subsystems_switch_endpoint_physically_switches_backend_engine()
    {
        var engineManager = GlobalSetup.Factory.Services.GetRequiredService<ITorrentEngineManager>();
        engineManager.Should().NotBeNull();

        // 1. Switch to LibTorrent using /api/v1/subsystems/bittorrent/switch
        var switchReq = new { providerId = "LibTorrent" };
        var switchResp = await this.PostJsonAsync("/api/v1/subsystems/bittorrent/switch", switchReq);
        switchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var switchBody = Deserialize<JsonElement>(await switchResp.Content.ReadAsStringAsync());
        switchBody.GetProperty("success").GetBoolean().Should().BeTrue();
        switchBody.GetProperty("activeProvider").GetString().Should().Be("LibTorrent");

        // Verify physical backend reflection
        engineManager.ActiveEngineId.Should().Be("LibTorrent");
        engineManager.ActiveEngine.Should().BeOfType<LibTorrentDownloadEngine>();

        // 2. Switch back to MonoTorrent using /api/v1/subsystems/bittorrent/switch
        var backReq = new { providerId = "MonoTorrent" };
        var backResp = await this.PostJsonAsync("/api/v1/subsystems/bittorrent/switch", backReq);
        backResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var backBody = Deserialize<JsonElement>(await backResp.Content.ReadAsStringAsync());
        backBody.GetProperty("success").GetBoolean().Should().BeTrue();
        backBody.GetProperty("activeProvider").GetString().Should().Be("MonoTorrent");

        // Verify physical backend reflection
        engineManager.ActiveEngineId.Should().Be("MonoTorrent");
        engineManager.ActiveEngine.Should().BeOfType<MonoTorrentDownloadEngine>();
    }
}
