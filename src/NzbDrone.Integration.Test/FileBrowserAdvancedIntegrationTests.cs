// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class FileBrowserAdvancedIntegrationTests : IntegrationTestBase
{
    private string workDir = null!;

    [SetUp]
    public void SetUp()
    {
        var baseDir = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        this.workDir = Path.Combine(baseDir, "leecharr_fb_adv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.workDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.workDir))
        {
            try
            {
                Directory.Delete(this.workDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task FileBrowser_PasteCopyAndMove_AndPreview_AndBatchDelete_AllSucceed()
    {
        var subDirA = Path.Combine(this.workDir, "SubA");
        var subDirB = Path.Combine(this.workDir, "SubB");
        Directory.CreateDirectory(subDirA);
        Directory.CreateDirectory(subDirB);

        var sampleTextFile = Path.Combine(subDirA, "sample.txt");
        await File.WriteAllTextAsync(sampleTextFile, "Hello FileBrowser Preview Content");

        var extraFile1 = Path.Combine(subDirA, "extra1.tmp");
        var extraFile2 = Path.Combine(subDirA, "extra2.tmp");
        await File.WriteAllTextAsync(extraFile1, "tmp1");
        await File.WriteAllTextAsync(extraFile2, "tmp2");

        // 1. Preview text file
        var previewResp = await this.GetAsync($"/api/v1/files/preview?path={Uri.EscapeDataString(sampleTextFile)}");
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.Content.ReadAsStringAsync();
        using var prevDoc = JsonDocument.Parse(previewJson);
        prevDoc.RootElement.GetProperty("type").GetString().Should().Be("text");
        prevDoc.RootElement.GetProperty("content").GetString().Should().Contain("Hello FileBrowser Preview");

        // 2. Download endpoint
        var dlResp = await this.GetAsync($"/api/v1/files/download?path={Uri.EscapeDataString(sampleTextFile)}");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dlContent = await dlResp.Content.ReadAsStringAsync();
        dlContent.Should().Be("Hello FileBrowser Preview Content");

        // 3. Paste (Copy) sample.txt to subDirB
        var copyPayload = new
        {
            sources = new[] { sampleTextFile },
            destination = subDirB,
            operation = "copy",
        };
        var copyResp = await this.PostJsonAsync("/api/v1/files/paste", copyPayload);
        copyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        File.Exists(sampleTextFile).Should().BeTrue("original file should remain after copy");
        File.Exists(Path.Combine(subDirB, "sample.txt")).Should().BeTrue("copied file should exist in destination");

        // 4. Paste (Move) extra1.tmp to subDirB
        var movePayload = new
        {
            sources = new[] { extraFile1 },
            destination = subDirB,
            operation = "move",
        };
        var moveResp = await this.PostJsonAsync("/api/v1/files/paste", movePayload);
        moveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        File.Exists(extraFile1).Should().BeFalse("moved file should no longer exist in source");
        File.Exists(Path.Combine(subDirB, "extra1.tmp")).Should().BeTrue("moved file should exist in destination");

        // 5. Batch Delete
        var batchDelPayload = new
        {
            paths = new[] { extraFile2, Path.Combine(subDirB, "extra1.tmp") },
        };
        var batchDelResp = await this.PostJsonAsync("/api/v1/files/batch-delete", batchDelPayload);
        batchDelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchDelJson = await batchDelResp.Content.ReadAsStringAsync();
        using var bDoc = JsonDocument.Parse(batchDelJson);
        bDoc.RootElement.GetProperty("deletedCount").GetInt32().Should().Be(2);
        File.Exists(extraFile2).Should().BeFalse();
        File.Exists(Path.Combine(subDirB, "extra1.tmp")).Should().BeFalse();
    }
}
