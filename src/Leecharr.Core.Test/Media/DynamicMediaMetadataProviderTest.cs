// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Media;

[TestFixture]
public class DynamicMediaMetadataProviderTest
{
    private IMediaMetadataProvider primaryProvider = null!;
    private IMediaMetadataProvider fallbackProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicMediaMetadataProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.primaryProvider = Substitute.For<IMediaMetadataProvider>();
        this.primaryProvider.ProviderId.Returns("PrimaryProvider");
        this.primaryProvider.DisplayName.Returns("Primary Metadata Provider");
        this.primaryProvider.IsAvailable.Returns(true);
        this.primaryProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.fallbackProvider = Substitute.For<IMediaMetadataProvider>();
        this.fallbackProvider.ProviderId.Returns("FallbackProvider");
        this.fallbackProvider.DisplayName.Returns("Fallback Metadata Provider");
        this.fallbackProvider.IsAvailable.Returns(true);
        this.fallbackProvider.ProbeHealthAsync().Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveMediaMetadataProvider.Returns("PrimaryProvider");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.proxy = new DynamicMediaMetadataProxy(
            new[] { this.primaryProvider, this.fallbackProvider },
            this.configService,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public async Task FetchMetadataAsync_WhenPosterUrlIsNull_FallbackMergesRichMetadata()
    {
        this.primaryProvider.FetchMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string>())
            .Returns(Task.FromResult(new MediaMetadata
            {
                Title = "Gladiator",
                Year = 2000,
                MediaType = "Movie",
            }));

        this.fallbackProvider.FetchMetadataAsync("Gladiator", "movies", 2000, Arg.Any<string>())
            .Returns(Task.FromResult(new MediaMetadata
            {
                Title = "Gladiator",
                Year = 2000,
                MediaType = "Movie",
                Overview = "A former Roman General sets out to exact vengeance.",
                Rating = 8.5,
                Genres = "Action, Drama",
                ImdbId = "tt0172495",
                TmdbId = "98",
                PosterUrl = null,
                Cast = new List<string> { "Russell Crowe", "Joaquin Phoenix" },
            }));

        var result = await this.proxy.FetchMetadataAsync("Gladiator", "movies", 2000);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Gladiator");
        result.Overview.Should().Be("A former Roman General sets out to exact vengeance.");
        result.Rating.Should().Be(8.5);
        result.Genres.Should().Be("Action, Drama");
        result.ImdbId.Should().Be("tt0172495");
        result.TmdbId.Should().Be("98");
        result.PosterUrl.Should().BeNull();
        result.Cast.Should().ContainInOrder("Russell Crowe", "Joaquin Phoenix");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenPrimaryReturnsEmptyPlaceholder_SupersededByFallbackMetadata()
    {
        this.primaryProvider.FetchMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string>())
            .Returns(Task.FromResult(new MediaMetadata
            {
                Title = "Inception",
                Year = 2010,
                MediaType = "Movie",
                Overview = string.Empty,
                Rating = 0.0,
                PosterUrl = null,
            }));

        this.fallbackProvider.FetchMetadataAsync("Inception", "movies", 2010, Arg.Any<string>())
            .Returns(Task.FromResult(new MediaMetadata
            {
                Title = "Inception",
                Year = 2010,
                MediaType = "Movie",
                Overview = "A thief who steals corporate secrets through the use of dream-sharing technology.",
                Rating = 8.8,
                Genres = "Action, Sci-Fi",
                ImdbId = "tt1375666",
                TmdbId = "27205",
                PosterUrl = null,
                Cast = new List<string> { "Leonardo DiCaprio", "Joseph Gordon-Levitt" },
            }));

        var result = await this.proxy.FetchMetadataAsync("Inception", "movies", 2010);

        result.Should().NotBeNull();
        result!.Overview.Should().Be("A thief who steals corporate secrets through the use of dream-sharing technology.");
        result.Rating.Should().Be(8.8);
        result.ImdbId.Should().Be("tt1375666");
        result.TmdbId.Should().Be("27205");
        result.Genres.Should().Be("Action, Sci-Fi");
        result.Cast.Should().Contain("Leonardo DiCaprio");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenPrimaryThrowsException_FallbackIsUtilizedWithoutPoster()
    {
        this.primaryProvider.FetchMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string>())
            .Returns(Task.FromException<MediaMetadata>(new HttpRequestException("Primary service down")));

        this.fallbackProvider.FetchMetadataAsync("Dune", "movies", 2021, Arg.Any<string>())
            .Returns(Task.FromResult(new MediaMetadata
            {
                Title = "Dune",
                Year = 2021,
                MediaType = "Movie",
                Overview = "Paul Atreides must travel to the most dangerous planet in the universe.",
                Rating = 8.0,
                ImdbId = "tt1160419",
                TmdbId = "438631",
                PosterUrl = null,
            }));

        var result = await this.proxy.FetchMetadataAsync("Dune", "movies", 2021);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Dune");
        result.Overview.Should().Be("Paul Atreides must travel to the most dangerous planet in the universe.");
        result.Rating.Should().Be(8.0);
        result.ImdbId.Should().Be("tt1160419");
        result.TmdbId.Should().Be("438631");
    }

    [Test]
    public void HasEnrichedMetadata_And_IsEmptyPlaceholder_DetectStateCorrectly()
    {
        DynamicMediaMetadataProxy.IsEmptyPlaceholder(null).Should().BeTrue();
        DynamicMediaMetadataProxy.HasEnrichedMetadata(null).Should().BeFalse();

        var placeholder = new MediaMetadata
        {
            Title = "Test",
            Year = 2020,
            MediaType = "Movie",
            Overview = string.Empty,
            Rating = 0.0,
        };
        DynamicMediaMetadataProxy.IsEmptyPlaceholder(placeholder).Should().BeTrue();
        DynamicMediaMetadataProxy.HasEnrichedMetadata(placeholder).Should().BeFalse();

        var withOverview = new MediaMetadata { Title = "Test", Overview = "A plot synopsis." };
        DynamicMediaMetadataProxy.HasEnrichedMetadata(withOverview).Should().BeTrue();
        DynamicMediaMetadataProxy.IsEmptyPlaceholder(withOverview).Should().BeFalse();

        var withRating = new MediaMetadata { Title = "Test", Rating = 7.5 };
        DynamicMediaMetadataProxy.HasEnrichedMetadata(withRating).Should().BeTrue();
        DynamicMediaMetadataProxy.IsEmptyPlaceholder(withRating).Should().BeFalse();

        var withPoster = new MediaMetadata { Title = "Test", PosterUrl = "https://example.com/poster.jpg" };
        DynamicMediaMetadataProxy.HasEnrichedMetadata(withPoster).Should().BeTrue();
        DynamicMediaMetadataProxy.IsEmptyPlaceholder(withPoster).Should().BeFalse();

        var withId = new MediaMetadata { Title = "Test", ImdbId = "tt1234567" };
        DynamicMediaMetadataProxy.HasEnrichedMetadata(withId).Should().BeTrue();
        DynamicMediaMetadataProxy.IsEmptyPlaceholder(withId).Should().BeFalse();
    }
}
