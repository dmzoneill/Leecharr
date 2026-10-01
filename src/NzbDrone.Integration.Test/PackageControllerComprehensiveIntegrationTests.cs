// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class PackageControllerComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string PkgHash = "a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9";
    private const string PkgName = "PackageTestMovie";
    private Torrent testTorrent = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;

        this.testTorrent = new Torrent
        {
            Name = PkgName,
            InfoHash = PkgHash,
            SavePath = "/downloads/movies",
            Category = "movies",
            Status = TorrentStatus.Paused,
            TotalSize = 1048576,
            Progress = 1.0,
            DateAdded = DateTime.UtcNow,
        };
        torrentRepo.Insert(this.testTorrent);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var services = GlobalSetup.Factory.Services;
            var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
            if (this.testTorrent != null)
            {
                torrentRepo.Delete(this.testTorrent.Id);
            }
        }
        catch
        {
        }
    }

    [Test]
    public async Task Packages_ExportAndImportEndpoints_OperateSuccessfully()
    {
        // 1. GET /api/v1/packages/export
        var getExportResp = await this.Client.GetAsync($"/api/v1/packages/export?torrentIds={this.testTorrent.Id}&includePayload=false");
        getExportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        getExportResp.Content.Headers.ContentType?.MediaType.Should().Be("application/x-leecharr-package");
        var packageBytes = await getExportResp.Content.ReadAsByteArrayAsync();
        packageBytes.Length.Should().BeGreaterThan(0);

        // 2. POST /api/v1/packages/export
        var postExportPayload = new
        {
            torrentIds = new List<int> { this.testTorrent.Id },
            includePayload = false,
        };

        var postExportResp = await this.PostJsonAsync("/api/v1/packages/export", postExportPayload);
        postExportResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Error cases: empty IDs -> 400 BadRequest; nonexistent IDs -> 404 NotFound
        var emptyExportResp = await this.Client.GetAsync("/api/v1/packages/export?torrentIds=");
        emptyExportResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var notFoundExportResp = await this.Client.GetAsync("/api/v1/packages/export?torrentIds=999999");
        notFoundExportResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. POST /api/v1/packages/import with multipart form-data
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(packageBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-leecharr-package");
        form.Add(fileContent, "file", "test_package.tar.gz");

        var importResp = await this.Client.PostAsync("/api/v1/packages/import", form);
        importResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var importJson = await importResp.Content.ReadAsStringAsync();
        using var importDoc = JsonDocument.Parse(importJson);
        importDoc.RootElement.TryGetProperty("skippedDuplicatesCount", out var skippedProp).Should().BeTrue();
        skippedProp.GetInt32().Should().Be(1);
    }
}
