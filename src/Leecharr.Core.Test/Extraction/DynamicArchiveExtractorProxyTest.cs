// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private IArchiveExtractorProvider sharpCompressProvider = null!;
    private IArchiveExtractorProvider sevenZipProvider = null!;
    private IArchiveExtractorProvider unrarProvider = null!;
    private IConfigService configService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicArchiveExtractorProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.sharpCompressProvider = Substitute.For<IArchiveExtractorProvider>();
        this.sharpCompressProvider.ProviderId.Returns("SharpCompress");
        this.sharpCompressProvider.DisplayName.Returns("SharpCompress (Pure C# .NET)");
        this.sharpCompressProvider.IsAvailable.Returns(true);
        this.sharpCompressProvider.Capabilities.Returns(new ArchiveExtractorCapabilities { SupportsZip = true, SupportsRar5 = true });
        this.sharpCompressProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.sharpCompressProvider.CanExtract(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return path.EndsWith(".zip") || path.EndsWith(".rar") || path.EndsWith(".7z");
        });
        this.sharpCompressProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        this.sevenZipProvider = Substitute.For<IArchiveExtractorProvider>();
        this.sevenZipProvider.ProviderId.Returns("SevenZip");
        this.sevenZipProvider.DisplayName.Returns("7-Zip / p7zip (CLI / Native)");
        this.sevenZipProvider.IsAvailable.Returns(true);
        this.sevenZipProvider.Capabilities.Returns(new ArchiveExtractorCapabilities { Supports7z = true, SupportsRar5 = true });
        this.sevenZipProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.sevenZipProvider.CanExtract(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return path.EndsWith(".7z") || path.EndsWith(".rar") || path.EndsWith(".zip");
        });
        this.sevenZipProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        this.unrarProvider = Substitute.For<IArchiveExtractorProvider>();
        this.unrarProvider.ProviderId.Returns("Unrar");
        this.unrarProvider.DisplayName.Returns("RARLAB UnRAR (Official Native)");
        this.unrarProvider.IsAvailable.Returns(true);
        this.unrarProvider.Capabilities.Returns(new ArchiveExtractorCapabilities { SupportsRar5 = true });
        this.unrarProvider.ProbeHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.unrarProvider.CanExtract(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            return path.EndsWith(".rar");
        });
        this.unrarProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveArchiveExtractor.Returns("SharpCompress");

        this.diskProvider = Substitute.For<IDiskProvider>();
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IArchiveExtractorProvider>
        {
            this.sharpCompressProvider,
            this.sevenZipProvider,
            this.unrarProvider,
        };

        this.proxy = new DynamicArchiveExtractorProxy(
            providers,
            this.configService,
            this.diskProvider,
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
        this.proxy.ActiveProviderId.Should().Be("SharpCompress");
        this.proxy.ActiveProvider.Should().BeSameAs(this.sharpCompressProvider);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "SharpCompress", "SevenZip", "Unrar" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = this.proxy.GetProvider("sevenzip");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("SevenZip");
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
        var probe = await this.proxy.ProbeProviderAsync("SevenZip");
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
        var result = await this.proxy.SwitchProviderAsync("SevenZip");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("SharpCompress");
        result.ActiveProvider.Should().Be("SevenZip");

        this.proxy.ActiveProviderId.Should().Be("SevenZip");
        this.proxy.ActiveProvider.Should().BeSameAs(this.sevenZipProvider);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveArchiveExtractor"] == "SevenZip"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractorSwitchedEvent>(e => e.PreviousProvider == "SharpCompress" && e.NewProvider == "SevenZip"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await this.proxy.SwitchProviderAsync("SharpCompress");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("SharpCompress");

        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFailure()
    {
        var result = await this.proxy.SwitchProviderAsync("UnknownProvider");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
        this.proxy.ActiveProviderId.Should().Be("SharpCompress");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.sevenZipProvider.ProbeHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "7z binary missing",
        }));

        var result = await this.proxy.SwitchProviderAsync("SevenZip");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        this.proxy.ActiveProviderId.Should().Be("SharpCompress");
    }

    [Test]
    public async Task ExtractArchiveAsync_DelegatesToActiveProvider()
    {
        var result = await this.proxy.ExtractArchiveAsync("/path/to/archive.zip", "/dest/dir");

        result.Should().BeTrue();
        await this.sharpCompressProvider.Received(1).ExtractAsync("/path/to/archive.zip", "/dest/dir", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExtractArchiveAsync_WhenFileMissing_ReturnsFalse()
    {
        this.diskProvider.FileExists("/path/to/missing.zip").Returns(false);

        var result = await this.proxy.ExtractArchiveAsync("/path/to/missing.zip", "/dest/dir");

        result.Should().BeFalse();
        await this.sharpCompressProvider.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExtractArchiveAsync_WhenActiveProviderFails_FallsBackToSharpCompress()
    {
        await this.proxy.SwitchProviderAsync("SevenZip");
        this.sevenZipProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        this.sharpCompressProvider.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var result = await this.proxy.ExtractArchiveAsync("/path/to/archive.rar", "/dest/dir");

        result.Should().BeTrue();
        await this.sevenZipProvider.Received(1).ExtractAsync("/path/to/archive.rar", "/dest/dir", Arg.Any<CancellationToken>());
        await this.sharpCompressProvider.Received(1).ExtractAsync("/path/to/archive.rar", "/dest/dir", Arg.Any<CancellationToken>());
    }

    [Test]
    public void IsArchiveFile_ChecksActiveProviderAndFallback()
    {
        this.proxy.IsArchiveFile("movie.rar").Should().BeTrue();
        this.proxy.IsArchiveFile("movie.7z").Should().BeTrue();
        this.proxy.IsArchiveFile("movie.mkv").Should().BeFalse();
    }
}
