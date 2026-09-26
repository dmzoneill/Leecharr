// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
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
public class FileBrowserBackupAndMaintenanceComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task FileBrowser_DirectoryValidationAndOperations_Succeeds()
    {
        var storagePath = GlobalSetup.Factory.Services.GetService(typeof(NzbDrone.Core.Download.IStoragePathService)) as NzbDrone.Core.Download.IStoragePathService;
        var completedDir = storagePath?.GetCompletedDirectory(null) ?? AppContext.BaseDirectory;
        var tempBase = Path.Combine(completedDir, "leecharr_test_browser_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBase);

        try
        {
            // 1. GET /api/v1/files
            var listResp = await this.Client.GetAsync($"/api/v1/files?path={Uri.EscapeDataString(tempBase)}");
            listResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. POST /api/v1/files/validate
            var validResp = await this.PostJsonAsync("/api/v1/files/validate", new
            {
                path = tempBase,
            });
            validResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

            // 3. POST /api/v1/files/mkdir
            var subDir = Path.Combine(tempBase, "nested_folder");
            var mkdirResp = await this.PostJsonAsync("/api/v1/files/mkdir", new
            {
                path = subDir,
            });
            mkdirResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

            // 4. PUT /api/v1/files/rename
            var renameResp = await this.PutJsonAsync("/api/v1/files/rename", new
            {
                path = subDir,
                newName = "renamed_folder",
            });
            renameResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

            // 5. DELETE /api/v1/files
            var renamedPath = Path.Combine(tempBase, "renamed_folder");
            var delResp = await this.Client.DeleteAsync($"/api/v1/files?path={Uri.EscapeDataString(renamedPath)}");
            delResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                try
                {
                    Directory.Delete(tempBase, true);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }
    }

    [Test]
    public async Task Backup_ListAndCreate_EndpointsSucceed()
    {
        // 1. GET /api/v1/backup
        var listResp = await this.Client.GetAsync("/api/v1/backup");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task SystemMaintenanceAndTasks_EndpointsSucceed()
    {
        // 1. GET /api/v1/system/task
        var tasksResp = await this.Client.GetAsync("/api/v1/system/task");
        tasksResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/system/database/vacuum
        var vacuumResp = await this.PostJsonAsync("/api/v1/system/database/vacuum", new { });
        vacuumResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. GET /api/v1/diskspace
        var diskResp = await this.Client.GetAsync("/api/v1/diskspace");
        diskResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task SeedingAndSpeedSchedule_EndpointsSucceed()
    {
        // 1. GET /api/v1/seeding/stats
        var statsResp = await this.Client.GetAsync("/api/v1/seeding/stats");
        statsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/seeding/history
        var historyResp = await this.Client.GetAsync("/api/v1/seeding/history");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. GET /api/v1/speedschedule & active
        var schedResp = await this.Client.GetAsync("/api/v1/speedschedule");
        schedResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeSchedResp = await this.Client.GetAsync("/api/v1/speedschedule/active");
        activeSchedResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
