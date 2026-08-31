// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class DynamicMediaMetadataProxyTest
{
    private IMediaMetadataProvider servarrProvider = null!;
    private IMediaMetadataProvider tmdbProvider = null!;
    private IMediaMetadataProvider tvdbProvider = null!;
    private IMediaMetadataProvider localNfoProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicMediaMetadataProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.servarrProvider = Substitute.For<IMediaMetadataProvider>();
        this.servarrProvider.ProviderId.Returns("ServarrSync");
        this.servarrProvider.DisplayName.Returns("Servarr Library Sync (Sonarr / Radarr / Lidarr)");
        this.servarrProvider.IsAvailable.Returns(true);
        this.servarrProvider.Capabilities.Returns(new MediaMetadataCapabilities
        {
            SupportsMovies = true,
            SupportsTvSeries = true,
            SupportsMusic = true,
            SupportsPosters = true,
            SupportsFanart = true,
        });
        this.servarrProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.servarrProvider.FetchMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>())
            .Returns(Task.FromResult(new MediaMetadata { Title = "Breaking Bad", Year = 2008, MediaType = "TV" }));

        this.tmdbProvider = Substitute.For<IMediaMetadataProvider>();
        this.tmdbProvider.ProviderId.Returns("TMDB");
        this.tmdbProvider.DisplayName.Returns("The Movie Database (TMDB v3/v4)");
        this.tmdbProvider.IsAvailable.Returns(true);
        this.tmdbProvider.Capabilities.Returns(new MediaMetadataCapabilities
        {
            SupportsMovies = true,
            SupportsTvSeries = true,
            SupportsPosters = true,
            SupportsFanart = true,
            SupportsCast = true,
        });
        this.tmdbProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.tmdbProvider.FetchMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>())
            .Returns(Task.FromResult(new MediaMetadata { Title = "Inception", Year = 2010, MediaType = "Movie" }));

        this.tvdbProvider = Substitute.For<IMediaMetadataProvider>();
        this.tvdbProvider.ProviderId.Returns("TheTVDB");
        this.tvdbProvider.DisplayName.Returns("TheTVDB API v4");
        this.tvdbProvider.IsAvailable.Returns(true);
        this.tvdbProvider.Capabilities.Returns(new MediaMetadataCapabilities
        {
            SupportsTvSeries = true,
            SupportsPosters = true,
            SupportsSeasonBanners = true,
        });
        this.tvdbProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.localNfoProvider = Substitute.For<IMediaMetadataProvider>();
        this.localNfoProvider.ProviderId.Returns("LocalNFO");
        this.localNfoProvider.DisplayName.Returns("Local Filesystem NFO & Artwork Inspector");
        this.localNfoProvider.IsAvailable.Returns(true);
        this.localNfoProvider.Capabilities.Returns(new MediaMetadataCapabilities
        {
            SupportsNfoParsing = true,
            SupportsPosters = true,
        });
        this.localNfoProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveMediaMetadataProvider.Returns("ServarrSync");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IMediaMetadataProvider> { this.servarrProvider, this.tmdbProvider, this.tvdbProvider, this.localNfoProvider };

        this.proxy = new DynamicMediaMetadataProxy(
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
        this.proxy.ActiveProviderId.Should().Be("ServarrSync");
        this.proxy.ActiveProvider.Should().BeSameAs(this.servarrProvider);
    }

    [Test]
    public void Constructor_WhenConfigEmpty_FallsBackToDefault()
    {
        var config = Substitute.For<IConfigService>();
        config.ActiveMediaMetadataProvider.Returns(string.Empty);

        using var proxy = new DynamicMediaMetadataProxy(
            new[] { this.servarrProvider, this.tmdbProvider },
            config,
            this.eventAggregator);

        proxy.ActiveProviderId.Should().Be("ServarrSync");
    }

    [Test]
    public void Constructor_WhenNoProviders_ThrowsInvalidOperationException()
    {
        var act = () => new DynamicMediaMetadataProxy(
            Enumerable.Empty<IMediaMetadataProvider>(),
            this.configService,
            this.eventAggregator);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders().ToList();
        providers.Should().HaveCount(4);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "ServarrSync", "TMDB", "TheTVDB", "LocalNFO" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = this.proxy.GetProvider("tmdb");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("TMDB");
    }

    [Test]
    public void GetProvider_WithInvalidOrEmptyId_ReturnsNull()
    {
        this.proxy.GetProvider("NonExistent").Should().BeNull();
        this.proxy.GetProvider(string.Empty).Should().BeNull();
        this.proxy.GetProvider(null).Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await this.proxy.ProbeProviderAsync("TMDB");
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
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndPersistsConfig()
    {
        var result = await this.proxy.SwitchProviderAsync("TMDB");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("ServarrSync");
        result.ActiveProvider.Should().Be("TMDB");

        this.proxy.ActiveProviderId.Should().Be("TMDB");
        this.proxy.ActiveProvider.Should().BeSameAs(this.tmdbProvider);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveMediaMetadataProvider"] == "TMDB"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<MediaMetadataProviderSwitchedEvent>(e => e.PreviousProvider == "ServarrSync" && e.NewProvider == "TMDB"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await this.proxy.SwitchProviderAsync("ServarrSync");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("ServarrSync");

        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownOrEmptyProvider_ReturnsFailure()
    {
        var result1 = await this.proxy.SwitchProviderAsync("UnknownProvider");
        result1.Success.Should().BeFalse();
        result1.Error.Should().Contain("not registered");

        var result2 = await this.proxy.SwitchProviderAsync(string.Empty);
        result2.Success.Should().BeFalse();
        result2.Error.Should().Contain("empty");

        this.proxy.ActiveProviderId.Should().Be("ServarrSync");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.tmdbProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "API Key Invalid",
        }));

        var result = await this.proxy.SwitchProviderAsync("TMDB");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        this.proxy.ActiveProviderId.Should().Be("ServarrSync");
    }

    [Test]
    public async Task Delegation_ForwardsFetchMetadataToActiveProvider()
    {
        var meta = await this.proxy.FetchMetadataAsync("Breaking Bad", "tv", 2008);

        meta.Should().NotBeNull();
        meta.Title.Should().Be("Breaking Bad");
        await this.servarrProvider.Received(1).FetchMetadataAsync("Breaking Bad", "tv", 2008);
    }

    [Test]
    public async Task ConcreteProviders_ServarrSyncMetadataProvider_Tests()
    {
        var arrRepo = Substitute.For<IArrConnectionRepository>();
        arrRepo.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Name = "Sonarr 4K", ArrType = "Sonarr" },
        });

        var provider = new ServarrSyncMetadataProvider(arrRepo);
        provider.ProviderId.Should().Be("ServarrSync");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.SupportsMovies.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("1 Arr instances");

        var movieMeta = await provider.FetchMetadataAsync("Dune 2", "movies", 2024);
        movieMeta.Should().NotBeNull();
        movieMeta!.MediaType.Should().Be("Movie");
        movieMeta.Title.Should().Be("Dune 2");

        var musicMeta = await provider.FetchMetadataAsync("Pink Floyd - Dark Side", "music", 1973);
        musicMeta.Should().NotBeNull();
        musicMeta!.MediaType.Should().Be("Music");

        var emptyMeta = await provider.FetchMetadataAsync(string.Empty);
        emptyMeta.Should().BeNull();
    }

    [Test]
    public async Task ConcreteProviders_TmdbMetadataProvider_Tests()
    {
        var provider = new TmdbMetadataProvider();
        provider.ProviderId.Should().Be("TMDB");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.Capabilities.SupportsCast.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();

        var meta = await provider.FetchMetadataAsync("Interstellar.2014.1080p", "movies");
        meta.Should().NotBeNull();
        meta!.Title.Should().Be("Interstellar");
        meta.Year.Should().Be(2014);
        meta.MediaType.Should().Be("Movie");
        meta.PosterUrl.Should().BeNull(); // Offline heuristic without API key returns clean title/year without mock URLs
        meta.Cast.Should().BeEmpty();

        var empty = await provider.FetchMetadataAsync(string.Empty);
        empty.Should().BeNull();
    }

    [Test]
    public async Task ConcreteProviders_TvdbMetadataProvider_Tests()
    {
        var provider = new TvdbMetadataProvider();
        provider.ProviderId.Should().Be("TheTVDB");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.Capabilities.SupportsSeasonBanners.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();

        var meta = await provider.FetchMetadataAsync("The.Wire.S01.1080p", "tv", 2002);
        meta.Should().NotBeNull();
        meta!.Title.Should().Be("The Wire");
        meta.MediaType.Should().Be("TV");
        meta.Year.Should().Be(2002);
        meta.BannerUrl.Should().BeNull();
        meta.PosterUrl.Should().BeNull();

        var empty = await provider.FetchMetadataAsync(string.Empty);
        empty.Should().BeNull();
    }

    [Test]
    public async Task ConcreteProviders_LocalNfoMetadataProvider_Tests()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leecharr_test_nfo_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var nfoPath = System.IO.Path.Combine(tempDir, "movie.nfo");
            await System.IO.File.WriteAllTextAsync(nfoPath, @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<movie>
    <title>Gladiator</title>
    <year>2000</year>
    <plot>A former Roman General sets out to exact vengeance against the corrupt emperor.</plot>
    <rating>8.5</rating>
    <id>tt0172495</id>
    <tmdbid>98</tmdbid>
</movie>");

            var provider = new LocalNfoMetadataProvider();
            provider.ProviderId.Should().Be("LocalNFO");
            provider.DisplayName.Should().NotBeNullOrEmpty();
            provider.Capabilities.SupportsNfoParsing.Should().BeTrue();

            var health = await provider.ProbeHealthAsync();
            health.IsHealthy.Should().BeTrue();

            var meta = await provider.FetchMetadataAsync(tempDir, "movies");
            meta.Should().NotBeNull();
            meta!.Title.Should().Be("Gladiator");
            meta.Year.Should().Be(2000);
            meta.MediaType.Should().Be("Movie");
            meta.Overview.Should().Contain("former Roman General");
            meta.Rating.Should().Be(8.5);
            meta.ImdbId.Should().Be("tt0172495");
            meta.TmdbId.Should().Be("98");

            var heuristicMeta = await provider.FetchMetadataAsync("Gladiator.2000", "movies", 2000);
            heuristicMeta.Should().NotBeNull();
            heuristicMeta!.Title.Should().Be("Gladiator");
            heuristicMeta.MediaType.Should().Be("Movie");

            var empty = await provider.FetchMetadataAsync(string.Empty);
            empty.Should().BeNull();
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }
}
