// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Backup;
using Leecharr.Api.V1.Categories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BackupIntegrationTest : IntegrationTestBase
{
    [Test]
    public async Task CreateAndRestoreBackup_FlushesWalAndPurgesStaleWalOnRestore()
    {
        var appFolderInfo = GlobalSetup.Factory.Services.GetRequiredService<IAppFolderInfo>();

        // 1. Create a backup via the API endpoint
        using var createResponse = await this.Client.PostAsync("/api/v1/backup", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var backup = await createResponse.Content.ReadFromJsonAsync<BackupResource>();
        backup.Should().NotBeNull();
        backup!.Path.Should().NotBeNullOrWhiteSpace();
        File.Exists(backup.Path).Should().BeTrue();

        try
        {
            // 2. Verify backup zip contents contain leecharr.db
            using (var zip = ZipFile.OpenRead(backup.Path))
            {
                zip.Entries.Should().Contain(e => e.FullName == "leecharr.db");
            }

            // 3. Simulate stale WAL/SHM leftover files in AppDataFolder
            var staleWalPath = Path.Combine(appFolderInfo.AppDataFolder, "leecharr.db-wal");
            var staleShmPath = Path.Combine(appFolderInfo.AppDataFolder, "leecharr.db-shm");
            await File.WriteAllBytesAsync(staleWalPath, new byte[] { 0x37, 0x7f, 0x06, 0x82, 0x01, 0x02, 0x03, 0x04 });
            await File.WriteAllBytesAsync(staleShmPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

            // 4. Restore the backup via API endpoint
            var restoreRequest = new RestoreBackupRequest
            {
                BackupId = backup.Id,
                Path = backup.Path,
            };

            using var restoreResponse = await this.PostJsonAsync("/api/v1/backup/restore", restoreRequest);
            restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Verify stale WAL/SHM were deleted on restore
            File.Exists(staleWalPath).Should().BeFalse("Stale WAL file must be deleted during restore");
            File.Exists(staleShmPath).Should().BeFalse("Stale SHM file must be deleted during restore");

            // 6. Verify restored database is functional by querying the categories endpoint
            var categories = await this.GetJsonAsync<List<CategoryResource>>("/api/v1/categories");
            categories.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(backup.Path))
            {
                try
                {
                    File.Delete(backup.Path);
                }
                catch
                {
                    // Ignore cleanup
                }
            }
        }
    }
}
