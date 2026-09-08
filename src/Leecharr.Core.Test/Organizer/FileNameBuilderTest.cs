// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;

namespace Leecharr.Core.Test.Organizer;

[TestFixture]
public class FileNameBuilderTest
{
    private FileNameBuilder builder = null!;

    [SetUp]
    public void SetUp()
    {
        this.builder = new FileNameBuilder();
    }

    [Test]
    public void BuildFileName_SingleEpisode_SubstitutesTokensCorrectly()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "Pilot" },
            Quality = "WEBDL-1080p",
            QualityFull = "WEBDL-1080p Proper",
            ReleaseGroup = "FLUX",
            VideoCodec = "x264",
            AudioCodec = "DTS-HD MA",
            AudioChannels = "5.1",
            HdrFormat = "HDR10",
            VideoDynamicRange = "HDR",
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode CleanTitle} [{Quality Full}] [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels} {MediaInfo HdrFormat}]-{Release Group}",
        };

        var result = this.builder.BuildFileName(context, namingConfig: config);

        result.Should().Be("Breaking Bad - S01E01 - Pilot [WEBDL-1080p Proper] [x264 DTS-HD MA 5.1 HDR10]-FLUX.mkv");
    }

    [TestCase(MultiEpisodeStyle.Extend, "Breaking Bad - S01E01-E04 - Multi Part.mkv")]
    [TestCase(MultiEpisodeStyle.Range, "Breaking Bad - S01E01-04 - Multi Part.mkv")]
    [TestCase(MultiEpisodeStyle.HyphenatedNumbers, "Breaking Bad - S01E01-E02-E03-E04 - Multi Part.mkv")]
    [TestCase(MultiEpisodeStyle.Scene, "Breaking Bad - S01E01.E02.E03.E04 - Multi Part.mkv")]
    public void BuildFileName_MultiEpisode_SupportsDifferentFormattingStyles(
        MultiEpisodeStyle style,
        string expected)
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1, 2, 3, 4 },
            EpisodeTitles = new List<string> { "Multi Part" },
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode CleanTitle}",
            MultiEpisodeStyle = style,
        };

        var result = this.builder.BuildFileName(context, namingConfig: config);

        result.Should().Be(expected);
    }

    [Test]
    public void BuildFileName_MultiEpisodeTitles_JoinsWithPlus()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Show",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1, 2 },
            EpisodeTitles = new List<string> { "Part 1", "Part 2" },
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode Title}",
        };

        var result = this.builder.BuildFileName(context, namingConfig: config);

        result.Should().Be("Show - S01E01-E02 - Part 1 + Part 2.mkv");
    }

    [Test]
    public void BuildFileName_DirectoryTraversalTokens_NeutralizesSlashesAndDots()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "AC/DC: Live at Donington",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "../../etc/passwd" },
            ReleaseGroup = "group/name",
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} - {Release Group}",
        };

        var result = this.builder.BuildFileName(context, namingConfig: config);

        result.Should().NotContain("/");
        result.Should().NotContain("\\");
        result.Should().NotContain("..");
        result.Should().Be("AC-DC - Live at Donington - S01E01 - etc-passwd - group-name.mkv");
    }

    [Test]
    public void BuildFileName_MovieTokens_FormatsCorrectly()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "The Dark Knight",
            ReleaseYear = 2008,
            Quality = "Bluray-1080p",
            QualityFull = "Bluray-1080p Remux",
            ReleaseGroup = "SPARKS",
            VideoCodec = "x264",
            AudioCodec = "DTS",
            AudioChannels = "5.1",
            Edition = "Extended Edition",
            ImdbId = "tt0468569",
            TmdbId = "155",
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            StandardMovieFormat = "{Movie Title} ({Release Year}) [{Edition}] [{Quality Full}] [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels}] [{Release Group}] [imdbid-{ImdbId}]",
        };

        var result = this.builder.BuildFileName(context, namingConfig: config);

        result.Should().Be("The Dark Knight (2008) [Extended Edition] [Bluray-1080p Remux] [x264 DTS 5.1] [SPARKS] [imdbid-tt0468569].mkv");
    }

    [Test]
    public void BuildFileName_EmptyTokens_RemovesEmptyBracketsAndDashes()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "Pilot" },
            Quality = "1080p",
            // Empty ReleaseGroup, HdrFormat, VideoCodec
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode CleanTitle} [{Quality Full}] [{MediaInfo HdrFormat}] [{Release Group}]",
        };

        var result = this.builder.BuildFileName(context, namingConfig: config);

        result.Should().Be("Breaking Bad - S01E01 - Pilot [1080p].mkv");
    }

    [Test]
    public void BuildFileName_DailyEpisode_FormatsAirDate()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "The Daily Show",
            AirDate = new DateTime(2024, 5, 20),
            EpisodeTitles = new List<string> { "Guest Star" },
            Quality = "720p",
            Extension = "mkv",
        };

        var result = this.builder.BuildFileName(context);

        result.Should().Be("The Daily Show - 2024-05-20 - Guest Star [720p].mkv");
    }

    [Test]
    public void BuildFileName_AnimeEpisode_FormatsAbsoluteNumber()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Naruto",
            AbsoluteEpisodeNumbers = new List<int> { 101 },
            EpisodeTitles = new List<string> { "Gotta See! Gotta Know!" },
            Quality = "1080p",
            Extension = "mkv",
        };

        var result = this.builder.BuildFileName(context);

        result.Should().Be("Naruto - S01E00 - 101 - Gotta See Gotta Know [1080p].mkv");
    }

    [Test]
    public void BuildDirectoryMethods_FormatDirectoriesCorrectly()
    {
        var epContext = new EpisodeNamingContext
        {
            SeriesTitle = "Marvel's Agents of S.H.I.E.L.D.",
            SeasonNumber = 2,
        };

        var movieContext = new MovieNamingContext
        {
            MovieTitle = "Star Wars: Episode IV",
            ReleaseYear = 1977,
        };

        var seriesDir = this.builder.BuildSeriesDirectory(epContext);
        var seasonDir = this.builder.BuildSeasonDirectory(epContext);
        var movieDir = this.builder.BuildMovieDirectory(movieContext);

        seriesDir.Should().Be("Marvel's Agents of S.H.I.E.L.D");
        seasonDir.Should().Be("Season 2");
        movieDir.Should().Be("Star Wars - Episode IV (1977)");
    }

    [Test]
    public void BuildSeasonDirectory_Specials_ReturnsSpecials()
    {
        var epContext = new EpisodeNamingContext
        {
            SeriesTitle = "Doctor Who",
            SeasonNumber = 0,
        };

        var seasonDir = this.builder.BuildSeasonDirectory(epContext);
        seasonDir.Should().Be("Specials");
    }
}
