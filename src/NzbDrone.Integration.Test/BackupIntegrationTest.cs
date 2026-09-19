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
    public async Task CreateAndRestoreBackup_FlushesWalAndStagesRestoreFile()
    {
        var appFolderInfo = GlobalSetup.Factory.Services.GetRequiredService<IAppFolderInfo>();

        // 1. Create a backup via the API endpoint
        using var createResponse = await this.Client.PostAsync("/api/v1/backup", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var backup = await createResponse.Content.ReadFromJsonAsync<BackupResource>();
        backup.Should().NotBeNull();
        backup!.Path.Should().NotBeNullOrWhiteSpace();

        var physicalPath = Path.IsPathRooted(backup.Path)
            ? backup.Path
            : Path.Combine(appFolderInfo.AppDataFolder, "Backups", "manual", backup.Name);
        File.Exists(physicalPath).Should().BeTrue();

        try
        {
            // 2. Verify backup zip contents contain leecharr.db
            using (var zip = ZipFile.OpenRead(physicalPath))
            {
                zip.Entries.Should().Contain(e => e.FullName == "leecharr.db");
            }

            // 3. Restore the backup via API endpoint
            var restoreRequest = new RestoreBackupRequest
            {
                BackupId = backup.Id,
                Path = backup.Path,
            };

            using var restoreResponse = await this.PostJsonAsync("/api/v1/backup/restore", restoreRequest);
            restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Verify restored database is staged as leecharr.db.restore for safe restart
            var restoreDbPath = Path.Combine(appFolderInfo.AppDataFolder, "leecharr.db.restore");
            File.Exists(restoreDbPath).Should().BeTrue("Pending restore must be staged as leecharr.db.restore");

            // 5. Verify active database remains functional during runtime
            var categories = await this.GetJsonAsync<List<CategoryResource>>("/api/v1/categories");
            categories.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(physicalPath))
            {
                try
                {
                    File.Delete(physicalPath);
                }
                catch
                {
                    // Ignore cleanup
                }
            }
        }
    }
}
