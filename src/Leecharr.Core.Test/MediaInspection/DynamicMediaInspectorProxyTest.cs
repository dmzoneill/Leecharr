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
    private IMediaInspectorProvider _tagLibProvider = null!;
    private IMediaInspectorProvider _mediaInfoProvider = null!;
    private IMediaInspectorProvider _ffprobeProvider = null!;
    private IConfigService _configService = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicMediaInspectorProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _tagLibProvider = Substitute.For<IMediaInspectorProvider>();
        _tagLibProvider.ProviderId.Returns("TagLib");
        _tagLibProvider.DisplayName.Returns("TagLib# & Pure EBML (Pure .NET)");
        _tagLibProvider.IsAvailable.Returns(true);
        _tagLibProvider.Capabilities.Returns(new MediaInspectorCapabilities { SupportsDolbyVision = true, SupportsHdr10Plus = true });
        _tagLibProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _tagLibProvider.InspectFile(Arg.Any<string>()).Returns(new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            VideoCodec = "HEVC / H.265",
            Resolution = "4K UHD (2160p)",
            HdrFormat = "Dolby Vision",
            AudioCodec = "Dolby Atmos"
        });
        _tagLibProvider.Inspect(Arg.Any<Stream>(), Arg.Any<string>()).Returns(new MediaContainerInfo
        {
            ContainerFormat = "MP4",
            VideoCodec = "AVC / H.264",
            Resolution = "1080p"
        });
        _tagLibProvider.InspectMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaContainerInfo
            {
                ContainerFormat = "Matroska (MKV)",
                VideoCodec = "HEVC / H.265"
            }));

        _mediaInfoProvider = Substitute.For<IMediaInspectorProvider>();
        _mediaInfoProvider.ProviderId.Returns("MediaInfo");
        _mediaInfoProvider.DisplayName.Returns("MediaInfo (CLI / Shared Library)");
        _mediaInfoProvider.IsAvailable.Returns(true);
        _mediaInfoProvider.Capabilities.Returns(new MediaInspectorCapabilities { SupportsDolbyVision = true, SupportsChapters = true });
        _mediaInfoProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _mediaInfoProvider.InspectFile(Arg.Any<string>()).Returns(new MediaContainerInfo
        {
            ContainerFormat = "Matroska (MKV)",
            VideoCodec = "HEVC / H.265",
            Resolution = "4K UHD (2160p)"
        });

        _ffprobeProvider = Substitute.For<IMediaInspectorProvider>();
        _ffprobeProvider.ProviderId.Returns("FFprobe");
        _ffprobeProvider.DisplayName.Returns("FFprobe / FFmpeg (CLI / Multi-Stream)");
        _ffprobeProvider.IsAvailable.Returns(true);
        _ffprobeProvider.Capabilities.Returns(new MediaInspectorCapabilities { SupportsVideoThumbnails = true });
        _ffprobeProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        _configService = Substitute.For<IConfigService>();
        _configService.ActiveMediaInspector.Returns("TagLib");

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IMediaInspectorProvider>
        {
            _tagLibProvider,
            _mediaInfoProvider,
            _ffprobeProvider
        };

        _proxy = new DynamicMediaInspectorProxy(
            providers,
            _configService,
            _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        _proxy.ActiveProviderId.Should().Be("TagLib");
        _proxy.ActiveProvider.Should().BeSameAs(_tagLibProvider);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "TagLib", "MediaInfo", "FFprobe" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = _proxy.GetProvider("mediainfo");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("MediaInfo");
    }

    [Test]
    public void GetProvider_WithInvalidId_ReturnsNull()
    {
        var provider = _proxy.GetProvider("NonExistentProvider");
        provider.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await _proxy.ProbeProviderAsync("MediaInfo");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await _proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProvider()
    {
        var result = await _proxy.SwitchProviderAsync("MediaInfo");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("TagLib");
        result.ActiveProvider.Should().Be("MediaInfo");

        _proxy.ActiveProviderId.Should().Be("MediaInfo");
        _proxy.ActiveProvider.Should().BeSameAs(_mediaInfoProvider);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveMediaInspector"] == "MediaInfo"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<MediaInspectorSwitchedEvent>(e => e.PreviousProvider == "TagLib" && e.NewProvider == "MediaInfo"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await _proxy.SwitchProviderAsync("TagLib");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("TagLib");

        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFailure()
    {
        var result = await _proxy.SwitchProviderAsync("UnknownProvider");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
        _proxy.ActiveProviderId.Should().Be("TagLib");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _mediaInfoProvider.ProbeHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new MediaInspectorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "mediainfo executable missing"
        }));

        var result = await _proxy.SwitchProviderAsync("MediaInfo");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        _proxy.ActiveProviderId.Should().Be("TagLib");
    }

    [Test]
    public void InspectFile_DelegatesToActiveProvider()
    {
        var info = _proxy.InspectFile("/path/to/movie.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        info.Resolution.Should().Be("4K UHD (2160p)");
        info.HdrFormat.Should().Be("Dolby Vision");

        _tagLibProvider.Received(1).InspectFile("/path/to/movie.mkv");
    }

    [Test]
    public async Task InspectFile_WhenActiveProviderFails_FallsBackToTagLib()
    {
        await _proxy.SwitchProviderAsync("MediaInfo");
        _mediaInfoProvider.InspectFile(Arg.Any<string>()).Returns((MediaContainerInfo)null!);

        var info = _proxy.InspectFile("/path/to/movie.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");
        _mediaInfoProvider.Received(1).InspectFile("/path/to/movie.mkv");
        _tagLibProvider.Received(1).InspectFile("/path/to/movie.mkv");
    }

    [Test]
    public void Inspect_DelegatesToActiveProvider()
    {
        using var stream = new MemoryStream(new byte[16]);
        var info = _proxy.Inspect(stream, "sample.mp4");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("MP4");
        info.Resolution.Should().Be("1080p");

        _tagLibProvider.Received(1).Inspect(stream, "sample.mp4");
    }

    [Test]
    public async Task InspectMediaAsync_DelegatesToActiveProvider()
    {
        var info = await _proxy.InspectMediaAsync("/path/to/movie.mkv");

        info.Should().NotBeNull();
        info.ContainerFormat.Should().Be("Matroska (MKV)");

        await _tagLibProvider.Received(1).InspectMediaAsync("/path/to/movie.mkv", Arg.Any<CancellationToken>());
    }
}
