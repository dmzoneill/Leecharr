// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BackupLifecycleComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Backup_ControllerEndpoints_FullLifecycle_Succeeds()
    {
        // 1. GET /api/v1/backup
        var getInitialResp = await this.Client.GetAsync("/api/v1/backup");
        getInitialResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/backup (create manual backup)
        var createResp = await this.PostJsonAsync("/api/v1/backup", new { });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var backupId = createDoc.RootElement.GetProperty("id").GetInt32();
        var backupName = createDoc.RootElement.GetProperty("name").GetString();
        var backupSize = createDoc.RootElement.GetProperty("size").GetInt64();

        backupId.Should().BeGreaterThan(0);
        backupName.Should().Contain("Leecharr_backup_");
        backupSize.Should().BeGreaterThan(0);

        try
        {
            // 3. GET /api/v1/backup/{id}/download
            var downloadResp = await this.Client.GetAsync($"/api/v1/backup/{backupId}/download");
            downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
            downloadResp.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
            var bytes = await downloadResp.Content.ReadAsByteArrayAsync();
            bytes.Length.Should().BeGreaterThan(0);

            // 4. Download non-existent backup returns 404
            var notFoundResp = await this.Client.GetAsync("/api/v1/backup/999999/download");
            notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            // 5. DELETE /api/v1/backup/{id}
            var delResp = await this.DeleteAsync($"/api/v1/backup/{backupId}");
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task Backup_ServiceDirectLifecycle_CreatesAndExecutesCommands()
    {
        var services = GlobalSetup.Factory.Services;
        var appFolderInfo = services.GetService(typeof(IAppFolderInfo)) as IAppFolderInfo;
        appFolderInfo.Should().NotBeNull();

        var backupService = new BackupService(appFolderInfo);

        // 1. Create manual backup directly
        var manualBackup = backupService.CreateBackup("Manual");
        manualBackup.Should().NotBeNull();
        manualBackup.Name.Should().Contain("Leecharr_backup_");
        manualBackup.Size.Should().BeGreaterThan(0);
        manualBackup.Type.Should().Be("Manual");
        File.Exists(manualBackup.Path).Should().BeTrue();

        // 2. Create scheduled backup directly
        await Task.Delay(1100);
        var schedBackup = backupService.CreateBackup("Scheduled");
        schedBackup.Should().NotBeNull();
        schedBackup.Type.Should().Be("Scheduled");
        File.Exists(schedBackup.Path).Should().BeTrue();

        // 3. Execute command synchronous
        await Task.Delay(1100);
        backupService.Execute(new BackupCommand { Type = "Manual" });

        // 4. Execute command asynchronous
        await Task.Delay(1100);
        await backupService.ExecuteAsync(new BackupCommand { Type = "Scheduled" });

        // Cleanup created files
        try
        {
            if (File.Exists(manualBackup.Path))
            {
                File.Delete(manualBackup.Path);
            }

            if (File.Exists(schedBackup.Path))
            {
                File.Delete(schedBackup.Path);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
