// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.MediaInspection;

[TestFixture]
public class DynamicMediaInspectorProxyTest
{
    private IMediaInspectorProvider tagLibProvider = null!;
    private IMediaInspectorProvider mediaInfoProvider = null!;
    private IMediaInspectorProvider ffprobeProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicMediaInspectorProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.tagLibProvider = Substitute.For<IMediaInspectorProvider>();
        this.tagLibProvider.ProviderId.Returns("TagLib");
        this.tagLibProvider.DisplayName.Returns("TagLib# & Pure EBML (Pure .NET)");
        this.tagLibProvider.IsAvailable.Returns(true);
        this.tagLibProvider.Capabilities.Returns(new MediaInspectorCapabilities { SupportsDolbyVision = true, SupportsHdr10Plus = true });
        this.tagLibProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.tagLibProvider.InspectFile(Arg.Any<string>()).Returns(new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            VideoCodec = "HEVC / H.265",
            Resolution = "4K UHD (2160p)",
            HdrFormat = "Dolby Vision",
            AudioCodec = "Dolby Atmos",
        });
        this.tagLibProvider.Inspect(Arg.Any<Stream>(), Arg.Any<string>()).Returns(new MediaContainerInfo
        {
            ContainerFormat = "MP4",
            VideoCodec = "AVC / H.264",
            Resolution = "1080p",
        });
        this.tagLibProvider.InspectMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaContainerInfo
            {
                ContainerFormat = "Matroska (MKV)",
                VideoCodec = "HEVC / H.265",
            }));

        this.mediaInfoProvider = Substitute.For<IMediaInspectorProvider>();
        this.mediaInfoProvider.ProviderId.Returns("MediaInfo");
        this.mediaInfoProvider.DisplayName.Returns("MediaInfo (CLI / Shared Library)");
        this.mediaInfoProvider.IsAvailable.Returns(true);
        this.mediaInfoProvider.Capabilities.Returns(new MediaInspectorCapabilities { SupportsDolbyVision = true, SupportsChapters = true });
        this.mediaInfoProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.mediaInfoProvider.InspectFile(Arg.Any<string>()).Returns(new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            VideoCodec = "HEVC / H.265",
            Resolution = "4K UHD (2160p)",
        });

        this.ffprobeProvider = Substitute.For<IMediaInspectorProvider>();
        this.ffprobeProvider.ProviderId.Returns("FFprobe");
        this.ffprobeProvider.DisplayName.Returns("FFprobe / FFmpeg (CLI / Multi-Stream)");
        this.ffprobeProvider.IsAvailable.Returns(true);
        this.ffprobeProvider.Capabilities.Returns(new MediaInspectorCapabilities { SupportsVideoThumbnails = true });
        this.ffprobeProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveMediaInspector.Returns("TagLib");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IMediaInspectorProvider>
        {
            this.tagLibProvider,
            this.mediaInfoProvider,
            this.ffprobeProvider,
        };

        this.proxy = new DynamicMediaInspectorProxy(
            providers,
            this.configService,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        this.proxy.ActiveProviderId.Should().Be("TagLib");
        this.proxy.ActiveProvider.Should().BeSameAs(this.tagLibProvider);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "TagLib", "MediaInfo", "FFprobe" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = this.proxy.GetProvider("mediainfo");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("MediaInfo");
    }

    [Test]
    public void GetProvider_WithInvalidId_ReturnsNull()
    {
        var provider = this.proxy.GetProvider("NonExistentProvider");
        provider.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await this.proxy.ProbeProviderAsync("MediaInfo");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await this.proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProvider()
    {
        var result = await this.proxy.SwitchProviderAsync("MediaInfo");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("TagLib");
        result.ActiveProvider.Should().Be("MediaInfo");

        this.proxy.ActiveProviderId.Should().Be("MediaInfo");
        this.proxy.ActiveProvider.Should().BeSameAs(this.mediaInfoProvider);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveMediaInspector"] == "MediaInfo"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaInspectorSwitchedEvent>(e => e.PreviousProvider == "TagLib" && e.NewProvider == "MediaInfo"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await this.proxy.SwitchProviderAsync("TagLib");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("TagLib");

        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFailure()
    {
        var result = await this.proxy.SwitchProviderAsync("UnknownProvider");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
        this.proxy.ActiveProviderId.Should().Be("TagLib");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.mediaInfoProvider.ProbeHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new MediaInspectorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "mediainfo executable missing",
        }));

        var result = await this.proxy.SwitchProviderAsync("MediaInfo");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        this.proxy.ActiveProviderId.Should().Be("TagLib");
    }

    [Test]
    public void InspectFile_DelegatesToActiveProvider()
    {
        var info = this.proxy.InspectFile("/path/to/movie.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        info.Resolution.Should().Be("4K UHD (2160p)");
        info.HdrFormat.Should().Be("Dolby Vision");

        this.tagLibProvider.Received(1).InspectFile("/path/to/movie.mkv");
    }

    [Test]
    public async Task InspectFile_WhenActiveProviderFails_FallsBackToTagLib()
    {
        await this.proxy.SwitchProviderAsync("MediaInfo");
        this.mediaInfoProvider.InspectFile(Arg.Any<string>()).Returns((MediaContainerInfo)null!);

        var info = this.proxy.InspectFile("/path/to/movie.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        this.mediaInfoProvider.Received(1).InspectFile("/path/to/movie.mkv");
        this.tagLibProvider.Received(1).InspectFile("/path/to/movie.mkv");
    }

    [Test]
    public void Inspect_DelegatesToActiveProvider()
    {
        using var stream = new MemoryStream(new byte[16]);
        var info = this.proxy.Inspect(stream, "sample.mp4");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP4");
        info.Resolution.Should().Be("1080p");

        this.tagLibProvider.Received(1).Inspect(stream, "sample.mp4");
    }

    [Test]
    public async Task InspectMediaAsync_DelegatesToActiveProvider()
    {
        var info = await this.proxy.InspectMediaAsync("/path/to/movie.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");

        await this.tagLibProvider.Received(1).InspectMediaAsync("/path/to/movie.mkv", Arg.Any<CancellationToken>());
    }
}
