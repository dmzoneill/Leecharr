using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class DynamicArchiveExtractorProxyTest
{
    private IArchiveExtractorProvider _sharpCompressProvider = null!;
    private IArchiveExtractorProvider _sevenZipProvider = null!;
    private IArchiveExtractorProvider _unrarProvider = null!;
    private IConfigService _configService = null!;
    private IDiskProvider _diskProvider = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicArchiveExtractorProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _sharpCompressProvider = Substitute.For<IArchiveExtractorProvider>();
        _sharpCompressProvider.ProviderId.Returns("SharpCompress");
        _sharpCompressProvider.DisplayName.Returns("SharpCompress (Pure C# .NET)");
        _sharpCompressProvider.IsAvailable.Returns(true);
        _sharpCompressProvider.Capabilities.Returns(new ArchiveExtractorCapabilities { SupportsZip = true, SupportsRar5 = true });
        _sharpCompressProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _sharpCompressProvider.CanExtract(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return path.EndsWith(".zip") || path.EndsWith(".rar") || path.EndsWith(".7z");
        });
        _sharpCompressProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        _sevenZipProvider = Substitute.For<IArchiveExtractorProvider>();
        _sevenZipProvider.ProviderId.Returns("SevenZip");
        _sevenZipProvider.DisplayName.Returns("7-Zip / p7zip (CLI / Native)");
        _sevenZipProvider.IsAvailable.Returns(true);
        _sevenZipProvider.Capabilities.Returns(new ArchiveExtractorCapabilities { Supports7z = true, SupportsRar5 = true });
        _sevenZipProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _sevenZipProvider.CanExtract(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return path.EndsWith(".7z") || path.EndsWith(".rar") || path.EndsWith(".zip");
        });
        _sevenZipProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        _unrarProvider = Substitute.For<IArchiveExtractorProvider>();
        _unrarProvider.ProviderId.Returns("Unrar");
        _unrarProvider.DisplayName.Returns("RARLAB UnRAR (Official Native)");
        _unrarProvider.IsAvailable.Returns(true);
        _unrarProvider.Capabilities.Returns(new ArchiveExtractorCapabilities { SupportsRar5 = true });
        _unrarProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _unrarProvider.CanExtract(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return path.EndsWith(".rar");
        });
        _unrarProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        _configService = Substitute.For<IConfigService>();
        _configService.ActiveArchiveExtractor.Returns("SharpCompress");

        _diskProvider = Substitute.For<IDiskProvider>();
        _diskProvider.FileExists(Arg.Any<string>()).Returns(true);

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IArchiveExtractorProvider>
        {
            _sharpCompressProvider,
            _sevenZipProvider,
            _unrarProvider
        };

        _proxy = new DynamicArchiveExtractorProxy(
            providers,
            _configService,
            _diskProvider,
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
        _proxy.ActiveProviderId.Should().Be("SharpCompress");
        _proxy.ActiveProvider.Should().BeSameAs(_sharpCompressProvider);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "SharpCompress", "SevenZip", "Unrar" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = _proxy.GetProvider("sevenzip");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("SevenZip");
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
        var probe = await _proxy.ProbeProviderAsync("SevenZip");
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
        var result = await _proxy.SwitchProviderAsync("SevenZip");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("SharpCompress");
        result.ActiveProvider.Should().Be("SevenZip");

        _proxy.ActiveProviderId.Should().Be("SevenZip");
        _proxy.ActiveProvider.Should().BeSameAs(_sevenZipProvider);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveArchiveExtractor"] == "SevenZip"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractorSwitchedEvent>(e => e.PreviousProvider == "SharpCompress" && e.NewProvider == "SevenZip"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await _proxy.SwitchProviderAsync("SharpCompress");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("SharpCompress");

        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFailure()
    {
        var result = await _proxy.SwitchProviderAsync("UnknownProvider");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
        _proxy.ActiveProviderId.Should().Be("SharpCompress");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _sevenZipProvider.ProbeHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "7z binary missing"
        }));

        var result = await _proxy.SwitchProviderAsync("SevenZip");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        _proxy.ActiveProviderId.Should().Be("SharpCompress");
    }

    [Test]
    public async Task ExtractArchiveAsync_DelegatesToActiveProvider()
    {
        var result = await _proxy.ExtractArchiveAsync("/path/to/archive.zip", "/dest/dir");

        result.Should().BeTrue();
        await _sharpCompressProvider.Received(1).ExtractAsync("/path/to/archive.zip", "/dest/dir", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExtractArchiveAsync_WhenFileMissing_ReturnsFalse()
    {
        _diskProvider.FileExists("/path/to/missing.zip").Returns(false);

        var result = await _proxy.ExtractArchiveAsync("/path/to/missing.zip", "/dest/dir");

        result.Should().BeFalse();
        await _sharpCompressProvider.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExtractArchiveAsync_WhenActiveProviderFails_FallsBackToSharpCompress()
    {
        await _proxy.SwitchProviderAsync("SevenZip");
        _sevenZipProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        _sharpCompressProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var result = await _proxy.ExtractArchiveAsync("/path/to/archive.rar", "/dest/dir");

        result.Should().BeTrue();
        await _sevenZipProvider.Received(1).ExtractAsync("/path/to/archive.rar", "/dest/dir", Arg.Any<CancellationToken>());
        await _sharpCompressProvider.Received(1).ExtractAsync("/path/to/archive.rar", "/dest/dir", Arg.Any<CancellationToken>());
    }

    [Test]
    public void IsArchiveFile_ChecksActiveProviderAndFallback()
    {
        _proxy.IsArchiveFile("movie.rar").Should().BeTrue();
        _proxy.IsArchiveFile("movie.7z").Should().BeTrue();
        _proxy.IsArchiveFile("movie.mkv").Should().BeFalse();
    }
}
