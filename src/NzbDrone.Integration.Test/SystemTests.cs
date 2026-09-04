// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SystemTests : IntegrationTestBase
{
    [Test]
    public async Task GetSystemStatus_ReturnsOkAndValidJson()
    {
        var response = await this.GetAsync("/api/v1/system/status");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Does.Contain("appName").IgnoreCase.Or.Contain("version").IgnoreCase);
    }

    [Test]
    public async Task GetLogFiles_ReturnsOk()
    {
        var response = await this.GetAsync("/api/v1/logfile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetLogFile_BothRoutes_ReturnOkAndContent()
    {
        var appFolderInfo = GlobalSetup.Factory.Services.GetRequiredService<IAppFolderInfo>();
        var logDir = Path.Combine(appFolderInfo.AppDataFolder, "logs");
        Directory.CreateDirectory(logDir);

        var testFileName = "test-log-download.txt";
        var testFilePath = Path.Combine(logDir, testFileName);
        var expectedContent = "integration test log file content";
        await File.WriteAllTextAsync(testFilePath, expectedContent);

        try
        {
            // Test standard route: /api/v1/logfile/{filename}
            var standardResponse = await this.GetAsync($"/api/v1/logfile/{testFileName}");
            standardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var standardContent = await standardResponse.Content.ReadAsStringAsync();
            standardContent.Should().Be(expectedContent);

            // Test backwards-compatible route alias: /api/v1/log/file/{filename}
            var aliasResponse = await this.GetAsync($"/api/v1/log/file/{testFileName}");
            aliasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var aliasContent = await aliasResponse.Content.ReadAsStringAsync();
            aliasContent.Should().Be(expectedContent);
        }
        finally
        {
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
        }
    }
}
