// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Update;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;

namespace Leecharr.Core.Test.Update;

[TestFixture]
public class UpdateCheckServiceTest
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Func<HttpRequestMessage, HttpResponseMessage> ResponseHandler { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        public Func<HttpRequestMessage, Exception> ExceptionHandler { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.CallCount++;

            if (this.ExceptionHandler != null)
            {
                var ex = this.ExceptionHandler(request);
                if (ex != null)
                {
                    throw ex;
                }
            }

            return Task.FromResult(this.ResponseHandler(request));
        }
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenGitHubReturnsValidReleases_ParsesPackagesCorrectly()
    {
        var jsonResponse = @"[
    {
    ""tag_name"": ""v1.4.3"",
    ""published_at"": ""2026-09-10T22:35:18Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.3"",
    ""body"": ""## New Features\n* Added advanced network proxy\n## Bug Fixes\n* Fixed crash in download engine"",
    ""assets"": [
        {
        ""name"": ""Leecharr.1.4.3.linux-x64.tar.gz"",
        ""browser_download_url"": ""https://github.com/dmzoneill/Leecharr/releases/download/v1.4.3/Leecharr.1.4.3.linux-x64.tar.gz""
        }
    ]
    },
    {
    ""tag_name"": ""v1.4.2"",
    ""published_at"": ""2026-09-08T10:00:00Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.2"",
    ""body"": ""* Minor improvements"",
    ""assets"": []
    }
]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().HaveCount(2);

        var latest = updates[0];
        latest.Version.Should().Be("1.4.3");
        latest.Latest.Should().BeTrue();
        latest.FileName.Should().Be("Leecharr.1.4.3.linux-x64.tar.gz");
        latest.DownloadUrl.Should().Be("https://github.com/dmzoneill/Leecharr/releases/download/v1.4.3/Leecharr.1.4.3.linux-x64.tar.gz");
        latest.Changes.New.Should().Contain("Added advanced network proxy");
        latest.Changes.Fixed.Should().Contain("Fixed crash in download engine");

        var older = updates[1];
        older.Version.Should().Be("1.4.2");
        older.Latest.Should().BeFalse();
        older.Changes.New.Should().Contain("Minor improvements");
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenNewestReleaseIsPrerelease_MarksHighestStableAsLatest()
    {
        var jsonResponse = @"[
    {
    ""tag_name"": ""v2.0.0-beta.1"",
    ""prerelease"": true,
    ""published_at"": ""2026-10-10T12:00:00Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v2.0.0-beta.1"",
    ""body"": ""* Beta channel"",
    ""assets"": []
    },
    {
    ""tag_name"": ""v1.4.3"",
    ""prerelease"": false,
    ""published_at"": ""2026-09-10T22:35:18Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.3"",
    ""body"": ""* Stable release"",
    ""assets"": []
    },
    {
    ""tag_name"": ""v1.4.2"",
    ""prerelease"": false,
    ""published_at"": ""2026-09-08T10:00:00Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.2"",
    ""body"": ""* Older stable"",
    ""assets"": []
    }
]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().HaveCount(3);
        updates[0].Version.Should().Be("2.0.0-beta.1");
        updates[0].Latest.Should().BeFalse();
        updates.Single(u => u.Version == "1.4.3").Latest.Should().BeTrue();
        updates.Single(u => u.Version == "1.4.2").Latest.Should().BeFalse();
    }

    [TestCase("1.2.30", "1.2.3", false)]
    [TestCase("1.0.20", "1.0.2", false)]
    [TestCase("1.0.2.1", "1.0.2", false)]
    [TestCase("1.0.1", "1.0", false)]
    [TestCase("1.2.3", "1.2.30", false)]
    [TestCase("1.2.3", "1.2.3", true)]
    [TestCase("1.2.3.0", "1.2.3", false)]
    [TestCase("2.0.0-beta.1", "2.0.0", false)]
    [TestCase("2.0.0-beta.1", "2.0.0-beta.1", true)]
    public void IsInstalledRelease_MatchesOnlyTheSameVersion(string releaseVersion, string runningVersion, bool expected)
    {
        UpdateCheckService.IsInstalledRelease(releaseVersion, runningVersion).Should().Be(expected);
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenTagOnlySharesVersionPrefix_DoesNotMarkItInstalled()
    {
        var current = BuildInfo.Version.ToString();
        var prefixed = current + "1";
        var extended = current + ".1";
        var jsonResponse =
            "[\n" +
            "  {\n" +
            "    \"tag_name\": \"v" + prefixed + "\",\n" +
            "    \"published_at\": \"2026-10-01T00:00:00Z\",\n" +
            "    \"html_url\": \"https://github.com/dmzoneill/Leecharr/releases/tag/v" + prefixed + "\",\n" +
            "    \"body\": \"* Prefix\",\n" +
            "    \"assets\": []\n" +
            "  },\n" +
            "  {\n" +
            "    \"tag_name\": \"v" + current + "\",\n" +
            "    \"published_at\": \"2026-09-01T00:00:00Z\",\n" +
            "    \"html_url\": \"https://github.com/dmzoneill/Leecharr/releases/tag/v" + current + "\",\n" +
            "    \"body\": \"* Current\",\n" +
            "    \"assets\": []\n" +
            "  },\n" +
            "  {\n" +
            "    \"tag_name\": \"v" + extended + "\",\n" +
            "    \"published_at\": \"2026-08-01T00:00:00Z\",\n" +
            "    \"html_url\": \"https://github.com/dmzoneill/Leecharr/releases/tag/v" + extended + "\",\n" +
            "    \"body\": \"* Extended\",\n" +
            "    \"assets\": []\n" +
            "  }\n" +
            "]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().HaveCount(3);
        updates.Single(u => u.Version == prefixed).Installed.Should().BeFalse();
        updates.Single(u => u.Version == current).Installed.Should().BeTrue();
        updates.Single(u => u.Version == extended).Installed.Should().BeFalse();
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenGitHubFailsOrReturnsError_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().BeEmpty();
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenHttpThrowsException_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler
        {
            ExceptionHandler = _ => new HttpRequestException("Network failure"),
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().BeEmpty();
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenCalledMultipleTimes_UsesCache()
    {
        var jsonResponse = @"[
    {
    ""tag_name"": ""v1.4.3"",
    ""published_at"": ""2026-09-10T22:35:18Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.3"",
    ""body"": ""* Initial release"",
    ""assets"": []
    }
]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var firstCall = await service.GetAvailableUpdatesAsync();
        var secondCall = await service.GetAvailableUpdatesAsync();

        firstCall.Should().BeEquivalentTo(secondCall);
        handler.CallCount.Should().Be(1);
    }

    [Test]
    public void GetPackagePlatformIdentifier_DetectsPlatformCorrectly()
    {
        UpdateCheckService.GetPackagePlatformIdentifier(true, false, Architecture.X64).Should().Be("win-x64");
        UpdateCheckService.GetPackagePlatformIdentifier(true, false, Architecture.Arm64).Should().Be("win-arm64");
        UpdateCheckService.GetPackagePlatformIdentifier(false, true, Architecture.X64).Should().Be("osx-x64");
        UpdateCheckService.GetPackagePlatformIdentifier(false, true, Architecture.Arm64).Should().Be("osx-arm64");
        UpdateCheckService.GetPackagePlatformIdentifier(false, false, Architecture.X64).Should().Be("linux-x64");
        UpdateCheckService.GetPackagePlatformIdentifier(false, false, Architecture.Arm64).Should().Be("linux-arm64");
    }

    [Test]
    public void GetPackageExtension_ReturnsZipForWindowsAndTarGzForOthers()
    {
        UpdateCheckService.GetPackageExtension(true).Should().Be(".zip");
        UpdateCheckService.GetPackageExtension(false).Should().Be(".tar.gz");
    }

    [Test]
    public void GetPackageFileName_FormatsExpectedFileName()
    {
        var fileName = UpdateCheckService.GetPackageFileName("1.5.0");
        fileName.Should().StartWith("Leecharr.1.5.0.");
        fileName.Should().Match(s => s.EndsWith(".tar.gz") || s.EndsWith(".zip"));
    }

    [Test]
    public void SelectPackageAssetFileName_PrefersPlatformMatchedArchive()
    {
        var assets = new[]
        {
            "Leecharr.1.0.0.linux-x64.tar.gz.sha256",
            "Leecharr.win-x64.zip",
            "Leecharr.1.0.0.linux-x64.tar.gz",
        };
        var fallback = "Leecharr.1.0.0.linux-x64.tar.gz";

        UpdateCheckService.SelectPackageAssetFileName(assets, "linux-x64", ".tar.gz", fallback)
            .Should().Be("Leecharr.1.0.0.linux-x64.tar.gz");
    }

    [Test]
    public void SelectPackageAssetFileName_IgnoresWrongPlatformAndChecksumAssets()
    {
        var assets = new[] { "Leecharr.1.0.0.win-x64.zip", "Leecharr.1.0.0.linux-x64.tar.gz.sha256" };
        var fallback = "Leecharr.1.0.0.linux-x64.tar.gz";

        UpdateCheckService.SelectPackageAssetFileName(assets, "linux-x64", ".tar.gz", fallback)
            .Should().Be(fallback);
    }

    [Test]
    public void SelectPackageAssetFileName_DoesNotTreatArm64AsArm()
    {
        var assets = new[] { "Leecharr.1.0.0.linux-arm64.tar.gz" };
        var fallback = "Leecharr.1.0.0.linux-arm.tar.gz";

        UpdateCheckService.SelectPackageAssetFileName(assets, "linux-arm", ".tar.gz", fallback)
            .Should().Be(fallback);
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenOnlyWrongPlatformAssetExists_UsesExpectedPackageFileName()
    {
        var platform = UpdateCheckService.GetPackagePlatformIdentifier();
        var extension = UpdateCheckService.GetPackageExtension();
        var wrongPlatform = platform.StartsWith("win", StringComparison.OrdinalIgnoreCase) ? "linux-x64" : "win-x64";
        var wrongExtension = extension == ".zip" ? ".tar.gz" : ".zip";
        var wrongAssetName = $"Leecharr.9.9.9.{wrongPlatform}{wrongExtension}";
        var jsonResponse = @"[
    {
    ""tag_name"": ""v9.9.9"",
    ""published_at"": ""2026-09-10T22:35:18Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v9.9.9"",
    ""body"": ""* Test"",
    ""assets"": [
        {
        ""name"": """ + wrongAssetName + @"""
        }
    ]
    }
]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().ContainSingle();
        updates[0].FileName.Should().Be(UpdateCheckService.GetPackageFileName("9.9.9"));
    }

    [Test]
    public async Task UpdateController_InstallUpdate_SignalsRestartAndStopsApplication()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var updateCheckService = Substitute.For<IUpdateCheckService>();
        var updateInstallService = Substitute.For<IUpdateInstallService>();

        updateCheckService.GetAvailableUpdatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UpdatePackage>
            {
                new()
                {
                    Version = "1.5.0",
                    Latest = true,
                    Installed = false,
                    FileName = "Leecharr.1.5.0.linux-x64.tar.gz",
                    DownloadUrl = "https://github.com/dmzoneill/Leecharr/releases/download/v1.5.0/Leecharr.1.5.0.linux-x64.tar.gz",
                },
            }));

        updateInstallService.PrepareUpdateAsync(Arg.Any<UpdatePackage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var controller = new UpdateController(updateCheckService, updateInstallService, runtimeInfo, lifetime);

        var result = await controller.InstallUpdate(new UpdateResource { Version = "1.5.0" });

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        runtimeInfo.RestartPending.Should().BeTrue();
        await updateInstallService.Received(1).PrepareUpdateAsync(
            Arg.Is<UpdatePackage>(p => p.Version == "1.5.0"),
            Arg.Any<CancellationToken>());

        await Task.Delay(600);
        lifetime.Received(1).StopApplication();
    }
}
