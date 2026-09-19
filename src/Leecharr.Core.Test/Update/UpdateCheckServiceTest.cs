// Copyright (c) PlaceholderCompany. All rights reserved.

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
        ""name"": ""Leecharr.1.4.3.linux-x64.tar.gz""
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
        latest.Changes.New.Should().Contain("Added advanced network proxy");
        latest.Changes.Fixed.Should().Contain("Fixed crash in download engine");

        var older = updates[1];
        older.Version.Should().Be("1.4.2");
        older.Latest.Should().BeFalse();
        older.Changes.New.Should().Contain("Minor improvements");
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenGitHubFailsOrReturnsError_FallsBackToDefaultList()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().NotBeEmpty();
        updates[0].Installed.Should().BeTrue();
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenHttpThrowsException_GracefullyFallsBackToDefault()
    {
        var handler = new MockHttpMessageHandler
        {
            ExceptionHandler = _ => new HttpRequestException("Network failure"),
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().NotBeEmpty();
        updates[0].Installed.Should().BeTrue();
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
    public async Task UpdateController_InstallUpdate_SignalsRestartAndStopsApplication()
    {
        var runtimeInfo = Substitute.For<IRuntimeInfo>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var updateCheckService = Substitute.For<IUpdateCheckService>();

        updateCheckService.GetAvailableUpdatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UpdatePackage>
            {
                new()
                {
                    Version = "1.5.0",
                    Latest = true,
                    FileName = "Leecharr.1.5.0.linux-x64.tar.gz",
                },
            }));

        var controller = new UpdateController(updateCheckService, runtimeInfo, lifetime);

        var result = await controller.InstallUpdate(new UpdateResource { Version = "1.5.0" });

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        runtimeInfo.RestartPending.Should().BeTrue();

        await Task.Delay(600);
        lifetime.Received(1).StopApplication();
    }
}
