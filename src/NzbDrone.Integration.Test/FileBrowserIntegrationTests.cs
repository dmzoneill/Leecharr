// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Download;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class FileBrowserIntegrationTests : IntegrationTestBase
{
    private string downloadsDir = null!;

    [SetUp]
    public void SetUp()
    {
        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        this.downloadsDir = Path.Combine(baseDir, "leecharr_fb_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.downloadsDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.downloadsDir))
        {
            try
            {
                Directory.Delete(this.downloadsDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task GetListing_RootDownloadsDirectory_ReturnsListingWithFilesAndDirectories()
    {
        var testSubdir = Path.Combine(this.downloadsDir, "TestBrowserSubdir");
        var testFile = Path.Combine(this.downloadsDir, "test_browser_movie.mkv");
        Directory.CreateDirectory(testSubdir);
        await File.WriteAllTextAsync(testFile, "test data");

        try
        {
            var response = await this.GetAsync($"/api/v1/files?path={Uri.EscapeDataString(this.downloadsDir)}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.TryGetProperty("entries", out var entries).Should().BeTrue();
            entries.ValueKind.Should().Be(JsonValueKind.Array);
            var entryList = entries.EnumerateArray().ToList();
            entryList.Should().Contain(e => e.GetProperty("name").GetString() == "TestBrowserSubdir" && e.GetProperty("isDirectory").GetBoolean());
            entryList.Should().Contain(e => e.GetProperty("name").GetString() == "test_browser_movie.mkv" && !e.GetProperty("isDirectory").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(testSubdir))
            {
                Directory.Delete(testSubdir, true);
            }

            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }

    [Test]
    public async Task CreateDirectory_And_Rename_And_Delete_EndToEndOperations_Succeed()
    {
        var newFolderRelative = "NewTestDir_" + Guid.NewGuid().ToString("N");
        var newFolderPath = Path.Combine(this.downloadsDir, newFolderRelative);
        var renamedRelative = "RenamedTestDir_" + Guid.NewGuid().ToString("N");
        var renamedPath = Path.Combine(this.downloadsDir, renamedRelative);

        try
        {
            // 1. Create directory
            var mkdirResp = await this.PostJsonAsync("/api/v1/files/mkdir", new { path = newFolderPath });
            mkdirResp.StatusCode.Should().Be(HttpStatusCode.OK);
            Directory.Exists(newFolderPath).Should().BeTrue();

            // 2. Validate path
            var validateResp = await this.PostJsonAsync("/api/v1/files/validate", new { path = newFolderPath });
            validateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var validateJson = await validateResp.Content.ReadAsStringAsync();
            using var vDoc = JsonDocument.Parse(validateJson);
            vDoc.RootElement.GetProperty("isValid").GetBoolean().Should().BeTrue();

            // 3. Rename directory
            var renameResp = await this.PutJsonAsync("/api/v1/files/rename", new { path = newFolderPath, newName = renamedRelative });
            renameResp.StatusCode.Should().Be(HttpStatusCode.OK);
            Directory.Exists(newFolderPath).Should().BeFalse();
            Directory.Exists(renamedPath).Should().BeTrue();

            // 4. Delete directory
            var deleteResp = await this.DeleteAsync($"/api/v1/files?path={Uri.EscapeDataString(renamedPath)}");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);
            Directory.Exists(renamedPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(newFolderPath))
            {
                Directory.Delete(newFolderPath, true);
            }

            if (Directory.Exists(renamedPath))
            {
                Directory.Delete(renamedPath, true);
            }
        }
    }

    [Test]
    public async Task ValidatePath_WithRootOrSystemDirectory_RejectsAccess()
    {
        var response = await this.PostJsonAsync("/api/v1/files/validate", new { path = "/etc" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("restricted");
    }
}
