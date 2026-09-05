// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment.Providers;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class LocalNfoMetadataProviderTest
{
    private LocalNfoMetadataProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.provider = new LocalNfoMetadataProvider();
    }

    [TestCase("Show.S1E01", "Show", "TV")]
    [TestCase("Show.1x05", "Show", "TV")]
    [TestCase("Severance.S02E01.1080p.WEB-DL", "Severance", "TV")]
    [TestCase("Game.of.Thrones.Season.1.1080p", "Game of Thrones", "TV")]
    [TestCase("The.Mandalorian.Episode.01.2160p", "The Mandalorian", "TV")]
    [TestCase("Anime.E05.1080p", "Anime", "TV")]
    public async Task FetchMetadataAsync_ClassifiesTvAndCleansEpisodicTags(
        string releaseName, string expectedTitle, string expectedMediaType)
    {
        var metadata = await this.provider.FetchMetadataAsync(releaseName);

        metadata.Should().NotBeNull();
        metadata!.Title.Should().Be(expectedTitle);
        metadata.MediaType.Should().Be(expectedMediaType);
    }

    [TestCase("Season.of.the.Witch.2011.1080p", "Season of the Witch", 2011, "Movie")]
    [TestCase("Open.Season.1080p", "Open Season", 0, "Movie")]
    public async Task FetchMetadataAsync_WhenMovieContainsWordSeason_ClassifiedAsMovieAndCleaned(
        string releaseName, string expectedTitle, int expectedYear, string expectedMediaType)
    {
        var metadata = await this.provider.FetchMetadataAsync(releaseName);

        metadata.Should().NotBeNull();
        metadata!.Title.Should().Be(expectedTitle);
        metadata.Year.Should().Be(expectedYear);
        metadata.MediaType.Should().Be(expectedMediaType);
    }

    [TestCase("Show.S1E01", "Show")]
    [TestCase("Show.1x05", "Show")]
    [TestCase("Season.of.the.Witch.2011.1080p", "Season of the Witch")]
    [TestCase("Severance.S02E01.1080p.WEB-DL", "Severance")]
    [TestCase("Game.of.Thrones.Season.1.1080p", "Game of Thrones")]
    [TestCase("The.Mandalorian.Episode.01.2160p", "The Mandalorian")]
    [TestCase("Open.Season.1080p", "Open Season")]
    [TestCase("Blade.Runner.2049.2017.1080p", "Blade Runner 2049")]
    [TestCase("2001.A.Space.Odyssey.1968.1080p", "2001 A Space Odyssey")]
    [TestCase("1917.2019.1080p", "1917")]
    [TestCase("1984.1956.720p", "1984")]
    [TestCase("2012.2009.1080p", "2012")]
    public void CleanTitle_StripsEpisodicTagsAndYears(string raw, string expected)
    {
        LocalNfoMetadataProvider.CleanTitle(raw).Should().Be(expected);
    }

    [Test]
    public async Task FetchMetadataAsync_WithCategoryOverrides_ClassifiesCorrectly()
    {
        var tvBySonarr = await this.provider.FetchMetadataAsync("SomeTitle", "sonarr");
        tvBySonarr.Should().NotBeNull();
        tvBySonarr!.MediaType.Should().Be("TV");

        var tvBySeries = await this.provider.FetchMetadataAsync("SomeTitle", "tv-series");
        tvBySeries.Should().NotBeNull();
        tvBySeries!.MediaType.Should().Be("TV");

        var movieByRadarr = await this.provider.FetchMetadataAsync("Show.S1E01", "radarr");
        movieByRadarr.Should().NotBeNull();
        movieByRadarr!.MediaType.Should().Be("Movie");

        var movieByCategory = await this.provider.FetchMetadataAsync("Show.S1E01", "movies");
        movieByCategory.Should().NotBeNull();
        movieByCategory!.MediaType.Should().Be("Movie");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenTitleIsEmpty_ReturnsNull()
    {
        var result = await this.provider.FetchMetadataAsync(string.Empty);
        result.Should().BeNull();

        var whitespaceResult = await this.provider.FetchMetadataAsync("   ");
        whitespaceResult.Should().BeNull();
    }

    [Test]
    public async Task ProbeHealthAsync_ReturnsHealthy()
    {
        var health = await this.provider.ProbeHealthAsync();
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
    }

    [Test]
    public async Task FetchMetadataAsync_WithNfoFile_ParsesMetadataAndArtwork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_local_nfo_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var nfoPath = Path.Combine(tempDir, "tvshow.nfo");
            await File.WriteAllTextAsync(nfoPath, @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<tvshow>
    <title>Severance</title>
    <year>2022</year>
    <plot>Mark leads a team of office workers whose memories have been surgically divided between their work and personal lives.</plot>
    <rating>8.7</rating>
    <id>tt11280740</id>
    <tmdbid>95396</tmdbid>
</tvshow>");

            var posterPath = Path.Combine(tempDir, "poster.jpg");
            await File.WriteAllTextAsync(posterPath, "fake image content");

            var fanartPath = Path.Combine(tempDir, "fanart.jpg");
            await File.WriteAllTextAsync(fanartPath, "fake backdrop content");

            var metadata = await this.provider.FetchMetadataAsync(tempDir);

            metadata.Should().NotBeNull();
            metadata!.Title.Should().Be("Severance");
            metadata.Year.Should().Be(2022);
            metadata.MediaType.Should().Be("TV");
            metadata.Overview.Should().Contain("surgically divided");
            metadata.Rating.Should().Be(8.7);
            metadata.ImdbId.Should().Be("tt11280740");
            metadata.TmdbId.Should().Be("95396");
            metadata.PosterUrl.Should().Be(posterPath);
            metadata.BackdropUrl.Should().Be(fanartPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
