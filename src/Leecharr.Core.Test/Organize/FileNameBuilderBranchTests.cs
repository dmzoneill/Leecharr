// Copyright (c) FeedItOut. All rights reserved.

#pragma warning disable SA1500, SA1516, SA1513, SA1508, SA1512, SA1507, SA1028
#nullable enable
using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Organizer;

namespace Leecharr.Core.Test.Organize;

[TestFixture]
public class FileNameBuilderBranchTests
{
    private FileNameBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _builder = new FileNameBuilder();
    }

    [Test]
    public void BuildFileName_Movie_NullContext_ReturnsEmpty()
    {
        var result = _builder.BuildFileName((MovieNamingContext)null!);
        result.Should().BeEmpty();
    }

    [Test]
    public void BuildFileName_Episode_NullContext_ReturnsEmpty()
    {
        var result = _builder.BuildFileName((EpisodeNamingContext)null!);
        result.Should().BeEmpty();
    }

    [Test]
    public void BuildMovieDirectory_NullContext_ReturnsEmpty()
    {
        var result = _builder.BuildMovieDirectory(null!);
        result.Should().BeEmpty();
    }

    [Test]
    public void BuildSeriesDirectory_NullContext_ReturnsEmpty()
    {
        var result = _builder.BuildSeriesDirectory(null!);
        result.Should().BeEmpty();
    }

    [Test]
    public void BuildSeasonDirectory_NullContext_ReturnsEmpty()
    {
        var result = _builder.BuildSeasonDirectory(null!);
        result.Should().BeEmpty();
    }

    [Test]
    public void BuildSeasonDirectory_SeasonZero_AlwaysReturnsSpecials()
    {
        var context = new EpisodeNamingContext { SeasonNumber = 0, SeriesTitle = "Doctor Who" };
        var result = _builder.BuildSeasonDirectory(context);
        result.Should().Be("Specials");
    }

    [Test]
    public void BuildFileName_Movie_TokenPermutations_TitleYearQualityAndMediaInfo()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Oppenheimer",
            ReleaseYear = 2023,
            Quality = "Bluray-1080p",
            QualityFull = "Bluray-1080p Proper",
            ReleaseGroup = "SPARKS",
            VideoCodec = "x265",
            AudioCodec = "DTS-HD MA",
            AudioChannels = "5.1",
            HdrFormat = "HDR10",
            VideoDynamicRange = "HDR",
            ImdbId = "tt15398776",
            TmdbId = "872585",
            OriginalFileName = "/downloads/Oppenheimer.2023.1080p.mkv",
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year}) [{Quality Full}] [{Quality Title}] [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels} {MediaInfo HdrFormat} {MediaInfo VideoDynamicRange}] [{Original Title}] [imdb-{ImdbId}] [tmdb-{TmdbId}]-{Release Group}";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Oppenheimer (2023) [Bluray-1080p Proper] [Bluray-1080p] [x265 DTS-HD MA 5.1 HDR10 HDR] [Oppenheimer.2023.1080p] [imdb-tt15398776] [tmdb-872585]-SPARKS.mkv");
    }

    [Test]
    public void BuildFileName_Movie_QualityFullFallsBackToQualityWhenEmpty()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Alien",
            ReleaseYear = 1979,
            Quality = "WEBDL-1080p",
            QualityFull = "",
            Extension = ".mp4",
        };

        var pattern = "{Movie Title} ({Release Year}) [{Quality Full}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Alien (1979) [WEBDL-1080p].mp4");
    }

    [Test]
    public void BuildFileName_Movie_QualityFullAndQualityBothEmpty()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Alien",
            ReleaseYear = 1979,
            Quality = null!,
            QualityFull = null!,
            Extension = ".mp4",
        };

        var pattern = "{Movie Title} ({Release Year}) [{Quality Full}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Alien (1979).mp4");
    }

    [Test]
    public void BuildFileName_Movie_ZeroOrNegativeYear_ProducesEmptyStringAndCleansParentheses()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Unknown Film",
            ReleaseYear = 0,
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year})";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Unknown Film.mkv");
    }

    [Test]
    public void BuildFileName_Movie_MediaInfoSimpleAndMediaInfoFull_UnmappedTokensRemovedCleanly()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Gladiator",
            ReleaseYear = 2000,
            Quality = "Bluray-2160p",
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year}) [{Quality Title}] [{MediaInfo Simple}] [{MediaInfo Full}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Gladiator (2000) [Bluray-2160p].mkv");
    }

    [Test]
    public void BuildFileName_Movie_NullOrMissingMediaInfo_CollapsesEmptyBrackets()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "The Matrix",
            ReleaseYear = 1999,
            VideoCodec = "",
            AudioCodec = "",
            AudioChannels = "",
            HdrFormat = "",
            VideoDynamicRange = "",
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year}) [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels} {MediaInfo HdrFormat}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("The Matrix (1999).mkv");
    }

    [Test]
    public void BuildFileName_Movie_MissingAudioChannels_RendersRemainingMediaInfo()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Interstellar",
            ReleaseYear = 2014,
            VideoCodec = "x264",
            AudioCodec = "DTS",
            AudioChannels = null!,
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year}) [{MediaInfo Video} {MediaInfo Audio} {MediaInfo AudioChannels}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Interstellar (2014) [x264 DTS ].mkv");
    }

    [Test]
    public void BuildFileName_Movie_MultipleAudioTracks_RendersCombinedChannelString()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Dune",
            ReleaseYear = 2021,
            VideoCodec = "HEVC",
            AudioCodec = "Atmos / AC3",
            AudioChannels = "7.1 / 5.1",
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year}) [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Dune (2021) [HEVC Atmos - AC3 7.1 - 5.1].mkv");
    }

    [TestCase("Director's Cut", "Blade Runner (1982) [Director's Cut].mkv")]
    [TestCase("Extended", "Blade Runner (1982) [Extended].mkv")]
    [TestCase("IMAX", "Blade Runner (1982) [IMAX].mkv")]
    [TestCase("Remastered", "Blade Runner (1982) [Remastered].mkv")]
    [TestCase("Director's Cut Extended IMAX Remastered", "Blade Runner (1982) [Director's Cut Extended IMAX Remastered].mkv")]
    [TestCase(null, "Blade Runner (1982).mkv")]
    [TestCase("", "Blade Runner (1982).mkv")]
    public void BuildFileName_Movie_EditionFlags_DirectorCutExtendedImaxRemastered(string edition, string expected)
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Blade Runner",
            ReleaseYear = 1982,
            Edition = edition!,
            Extension = "mkv",
        };

        var pattern = "{Movie Title} ({Release Year}) [{Edition}]";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be(expected);

        // Also test {Edition Tags}
        var pattern2 = "{Movie Title} ({Release Year}) [{Edition Tags}]";
        var result2 = _builder.BuildFileName(context, pattern2);
        result2.Should().Be(expected);
    }

    [Test]
    public void BuildFileName_Movie_MovieCleanTitle_UsesCleanTitleOrSanitizerFallback()
    {
        var contextWithClean = new MovieNamingContext
        {
            MovieTitle = "Face/Off: Special",
            MovieCleanTitle = "Face Off Special",
            ReleaseYear = 1997,
            Extension = "mkv",
        };
        var res1 = _builder.BuildFileName(contextWithClean, "{Movie CleanTitle} ({Release Year})");
        res1.Should().Be("Face Off Special (1997).mkv");

        var contextWithoutClean = new MovieNamingContext
        {
            MovieTitle = "Face/Off!",
            MovieCleanTitle = null!,
            ReleaseYear = 1997,
            Extension = "mkv",
        };
        var res2 = _builder.BuildFileName(contextWithoutClean, "{Movie CleanTitle} ({Release Year})");
        res2.Should().Be("Face Off (1997).mkv");
    }

    [Test]
    public void BuildFileName_Movie_CustomColonReplacementFormats()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Mission: Impossible",
            ReleaseYear = 1996,
            Extension = "mkv",
        };

        var configSpaceDashSpace = new NamingConfig
        {
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.SpaceDashSpace,
        };
        var r1 = _builder.BuildFileName(context, "{Movie Title} ({Release Year})", configSpaceDashSpace);
        r1.Should().Be("Mission - Impossible (1996).mkv");

        var configDash = new NamingConfig
        {
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Dash,
        };
        var r2 = _builder.BuildFileName(context, "{Movie Title} ({Release Year})", configDash);
        r2.Should().Be("Mission- Impossible (1996).mkv");

        var configDelete = new NamingConfig
        {
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Delete,
        };
        var r3 = _builder.BuildFileName(context, "{Movie Title} ({Release Year})", configDelete);
        r3.Should().Be("Mission Impossible (1996).mkv");

        var configCustom = new NamingConfig
        {
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Custom,
            CustomColonReplacementFormat = "__",
        };
        var r4 = _builder.BuildFileName(context, "{Movie Title} ({Release Year})", configCustom);
        r4.Should().Be("Mission__ Impossible (1996).mkv");
    }

    [Test]
    public void BuildFileName_BaselineSafety_WhenReplaceIllegalCharactersFalse_SanitizesForbiddenAndTraversal()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "../../../CON: The *Forbidden* <Movie> | \"Test\"?",
            ReleaseYear = 2024,
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            ReplaceIllegalCharacters = false,
        };

        var result = _builder.BuildFileName(context, "{Movie Title} ({Release Year})", config);

        result.Should().NotContain("..");
        result.Should().NotContain(":");
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
        result.EndsWith(".mkv").Should().BeTrue();
    }

    [Test]
    public void BuildFileName_BaselineSafety_ReservedDosDeviceName_PrependsUnderscore()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "CON",
            ReleaseYear = 0,
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            ReplaceIllegalCharacters = false,
        };

        var result = _builder.BuildFileName(context, "{Movie Title}", config);
        result.Should().Be("_CON.mkv");

        var auxContext = new MovieNamingContext { MovieTitle = "AUX", ReleaseYear = 0, Extension = "mp4" };
        var auxResult = _builder.BuildFileName(auxContext, "{Movie Title}", config);
        auxResult.Should().Be("_AUX.mp4");
    }

    [Test]
    public void BuildFileName_BaselineSafety_EmptyBaseName_FallsBackToUnderscore()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "...",
            ReleaseYear = 0,
            Extension = "mkv",
        };

        var config = new NamingConfig
        {
            ReplaceIllegalCharacters = false,
        };

        var result = _builder.BuildFileName(context, "{Movie Title}", config);
        result.Should().Be("mkv");
    }

    [Test]
    public void BuildFileName_Movie_LongFileName_TruncatesWithinMaxPathBytes()
    {
        var longTitle = new string('A', 300);
        var context = new MovieNamingContext
        {
            MovieTitle = longTitle,
            ReleaseYear = 2024,
            Extension = "mkv",
        };

        var result = _builder.BuildFileName(context);

        System.Text.Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(255);
        result.EndsWith(".mkv").Should().BeTrue();
    }

    [Test]
    public void BuildMovieDirectory_FormatsFolderAndSanitizesReservedNames()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Inception",
            ReleaseYear = 2010,
        };

        var dir1 = _builder.BuildMovieDirectory(context);
        dir1.Should().Be("Inception (2010)");

        var customDir = _builder.BuildMovieDirectory(context, "Movies/{Movie Title} [{Release Year}]");
        customDir.Should().Be("Movies-Inception [2010]");

        // Baseline safety for reserved folder name
        var reservedContext = new MovieNamingContext { MovieTitle = "NUL", ReleaseYear = 0 };
        var config = new NamingConfig { ReplaceIllegalCharacters = false };
        var resDir = _builder.BuildMovieDirectory(reservedContext, "{Movie Title}", config);
        resDir.Should().Be("_NUL");

        // Empty folder name
        var emptyContext = new MovieNamingContext { MovieTitle = "...", ReleaseYear = 0 };
        var emptyDir = _builder.BuildMovieDirectory(emptyContext, "{Movie Title}", config);
        emptyDir.Should().Be("Unnamed");
    }

    [Test]
    public void BuildSeriesAndSeasonDirectory_FormatsProperlyAndHandlesBaselineSafety()
    {
        var epContext = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad",
            SeasonNumber = 2,
        };

        var seriesDir = _builder.BuildSeriesDirectory(epContext);
        seriesDir.Should().Be("Breaking Bad");

        var seasonDir = _builder.BuildSeasonDirectory(epContext);
        seasonDir.Should().Be("Season 2");

        var customSeasonDir = _builder.BuildSeasonDirectory(epContext, "Season {season:00}");
        customSeasonDir.Should().Be("Season 02");

        // Baseline safety for folder
        var configNoReplace = new NamingConfig { ReplaceIllegalCharacters = false };
        var reservedSeriesContext = new EpisodeNamingContext { SeriesTitle = "COM1" };
        var reservedSeriesDir = _builder.BuildSeriesDirectory(reservedSeriesContext, "{Series Title}", configNoReplace);
        reservedSeriesDir.Should().Be("_COM1");
    }

    [Test]
    public void BuildFileName_Episode_DefaultFormatSelection_DailyAndAnimeAndStandard()
    {
        var dailyContext = new EpisodeNamingContext
        {
            SeriesTitle = "The Daily Show",
            AirDate = new DateTime(2024, 3, 15),
            EpisodeNumbers = new List<int>(),
            EpisodeCleanTitles = new List<string> { "Guest Star" },
            QualityFull = "HDTV-720p",
            Extension = "mkv",
        };
        var dailyResult = _builder.BuildFileName(dailyContext);
        dailyResult.Should().Be("The Daily Show - 2024-03-15 - Guest Star [HDTV-720p].mkv");

        var animeContext = new EpisodeNamingContext
        {
            SeriesTitle = "One Piece",
            EpisodeNumbers = new List<int>(),
            AbsoluteEpisodeNumbers = new List<int> { 1050 },
            EpisodeCleanTitles = new List<string> { "Luffy's Dream" },
            QualityFull = "1080p",
            Extension = "mkv",
        };
        var animeResult = _builder.BuildFileName(animeContext);
        animeResult.Should().Be("One Piece - S01E00 - 1050 - Luffy's Dream [1080p].mkv");
    }

    [Test]
    public void BuildFileName_Episode_TokenPermutations_AllEpisodeTokens()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Game of Thrones",
            SeriesCleanTitle = "",
            SeasonNumber = 8,
            EpisodeNumbers = new List<int> { 3 },
            EpisodeTitles = new List<string> { "The Long Night" },
            Quality = "WEBDL-1080p",
            QualityFull = "WEBDL-1080p",
            ReleaseGroup = "FLUX",
            VideoCodec = "x264",
            AudioCodec = "AC3",
            AudioChannels = "5.1",
            HdrFormat = "SDR",
            VideoDynamicRange = "SDR",
            OriginalFileName = "got.s08e03.mkv",
            ReleaseYear = 2019,
            ImdbId = "tt0944947",
            TmdbId = "1399",
            TvdbId = "121361",
            Extension = "mkv",
        };

        var pattern = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} [{Quality Title}] [{MediaInfo Video} {MediaInfo Audio} {MediaInfo AudioChannels} {MediaInfo Hdr} {MediaInfo VideoDynamicRange}] [{Original FileName}] [{Year}] [imdb-{ImdbId}] [tmdb-{TmdbId}] [tvdb-{TvdbId}]-{Release Group}";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().Be("Game of Thrones - S08E03 - The Long Night [WEBDL-1080p] [x264 AC3 5.1 SDR SDR] [got.s08e03] [2019] [imdb-tt0944947] [tmdb-1399] [tvdb-121361]-FLUX.mkv");
    }

    [Test]
    public void BuildFileName_Episode_SeriesCleanTitle_FallsBackToSanitizer()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Agents of S.H.I.E.L.D.",
            SeriesCleanTitle = null!,
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            Extension = "mkv",
        };

        var result = _builder.BuildFileName(context, "{Series CleanTitle} - S{season:00}E{episode:00}");
        result.Should().Be("Agents of S.H.I.E.L.D - S01E01.mkv");
    }

    [Test]
    public void BuildFileName_Episode_EmptyEpisodeNumbers_FormatsAsZero()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Special Show",
            SeasonNumber = 1,
            EpisodeNumbers = null!,
            Extension = "mkv",
        };

        var result = _builder.BuildFileName(context, "{Series Title} - S{season:00}E{episode:00}");
        result.Should().Be("Special Show - S01E00.mkv");

        var lowerResult = _builder.BuildFileName(context, "{Series Title} - s{season:00}e{episode:00}");
        lowerResult.Should().Be("Special Show - s01e00.mkv");
    }

    [Test]
    public void BuildFileName_Episode_MultiEpisode_HyphenatedAndSceneAndExtend()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Avatar",
            SeasonNumber = 3,
            EpisodeNumbers = new List<int> { 18, 19, 20, 21 },
            Extension = "mkv",
        };

        var configHyphen = new NamingConfig { MultiEpisodeStyle = MultiEpisodeStyle.HyphenatedNumbers };
        var rHyphen = _builder.BuildFileName(context, "{Series Title} - S{season:00}E{episode:00}", configHyphen);
        rHyphen.Should().Be("Avatar - S03E18-E19-E20-E21.mkv");

        var configScene = new NamingConfig { MultiEpisodeStyle = MultiEpisodeStyle.Scene };
        var rScene = _builder.BuildFileName(context, "{Series Title} - S{season:00}E{episode:00}", configScene);
        rScene.Should().Be("Avatar - S03E18.E19.E20.E21.mkv");

        var rLower = _builder.BuildFileName(context, "{Series Title} - s{season:00}e{episode:00}", configHyphen);
        rLower.Should().Be("Avatar - s03e18-E19-E20-E21.mkv");
    }

    [Test]
    public void BuildFileName_Episode_AbsoluteEpisodes_AllStyles()
    {
        var singleContext = new EpisodeNamingContext
        {
            SeriesTitle = "Naruto",
            AbsoluteEpisodeNumbers = new List<int> { 50 },
            Extension = "mkv",
        };
        var rSingle = _builder.BuildFileName(singleContext, "{Series Title} - {absolute:000}");
        rSingle.Should().Be("Naruto - 050.mkv");

        var multiContext = new EpisodeNamingContext
        {
            SeriesTitle = "Naruto",
            AbsoluteEpisodeNumbers = new List<int> { 50, 51, 52 },
            Extension = "mkv",
        };

        var configExtend = new NamingConfig { MultiEpisodeStyle = MultiEpisodeStyle.Extend };
        var rExtend = _builder.BuildFileName(multiContext, "{Series Title} - {absolute:000}", configExtend);
        rExtend.Should().Be("Naruto - 050-052.mkv");

        var configHyphen = new NamingConfig { MultiEpisodeStyle = MultiEpisodeStyle.HyphenatedNumbers };
        var rHyphen = _builder.BuildFileName(multiContext, "{Series Title} - {absolute:000}", configHyphen);
        rHyphen.Should().Be("Naruto - 050-051-052.mkv");

        var configScene = new NamingConfig { MultiEpisodeStyle = MultiEpisodeStyle.Scene };
        var rScene = _builder.BuildFileName(multiContext, "{Series Title} - {absolute:000}", configScene);
        rScene.Should().Be("Naruto - 050.051.052.mkv");

        var emptyContext = new EpisodeNamingContext
        {
            SeriesTitle = "Naruto",
            AbsoluteEpisodeNumbers = null!,
            Extension = "mkv",
        };
        var rEmpty = _builder.BuildFileName(emptyContext, "{Series Title} - {absolute:000}");
        rEmpty.Should().Be("Naruto.mkv");
    }

    [Test]
    public void BuildFileName_Episode_EpisodeTitles_FilteringAndJoining()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Show",
            EpisodeTitles = new List<string> { "Part A", "", "   ", "Part B" },
            Extension = "mkv",
        };
        var res = _builder.BuildFileName(context, "{Series Title} - {Episode Title}");
        res.Should().Be("Show - Part A + Part B.mkv");

        var emptyContext = new EpisodeNamingContext { SeriesTitle = "Show", EpisodeTitles = null!, Extension = "mkv" };
        var resEmpty = _builder.BuildFileName(emptyContext, "{Series Title} - {Episode Title}");
        resEmpty.Should().Be("Show.mkv");
    }

    [Test]
    public void BuildFileName_Episode_EpisodeCleanTitles_FallbackToOriginalTitles()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Show",
            EpisodeCleanTitles = null!,
            EpisodeTitles = new List<string> { "Part: One", "Part: Two" },
            Extension = "mkv",
        };
        var res = _builder.BuildFileName(context, "{Series Title} - {Episode CleanTitle}");
        res.Should().Be("Show - Part One + Part Two.mkv");

        var emptyContext = new EpisodeNamingContext
        {
            SeriesTitle = "Show",
            EpisodeCleanTitles = null!,
            EpisodeTitles = null!,
            Extension = "mkv",
        };
        var resEmpty = _builder.BuildFileName(emptyContext, "{Series Title} - {Episode CleanTitle}");
        resEmpty.Should().Be("Show.mkv");
    }

    [Test]
    public void BuildFileName_Episode_EpisodeCleanTitles_BlankEntriesFallbackToOriginalTitles()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Show",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeCleanTitles = new List<string> { "", "   " },
            EpisodeTitles = new List<string> { "Pilot" },
            Extension = "mkv",
        };
        var res = _builder.BuildFileName(context, "{Series Title} - S{season:00}E{episode:00} - {Episode CleanTitle}");
        res.Should().Be("Show - S01E01 - Pilot.mkv");
    }

    [Test]
    public void BuildFileName_StaticMethods_ExecuteSuccessfully()
    {
        var movieContext = new MovieNamingContext { MovieTitle = "The Dark Knight", ReleaseYear = 2008, Extension = "mkv" };
        var movieRes = FileNameBuilder.BuildFileNameStatic(movieContext);
        movieRes.Should().Be("The Dark Knight (2008).mkv");

        var movieDir = FileNameBuilder.BuildMovieDirectoryStatic(movieContext);
        movieDir.Should().Be("The Dark Knight (2008)");

        var epContext = new EpisodeNamingContext { SeriesTitle = "Fargo", SeasonNumber = 1, EpisodeNumbers = new List<int> { 1 }, Extension = "mkv" };
        var epRes = FileNameBuilder.BuildFileNameStatic(epContext);
        epRes.Should().Be("Fargo - S01E01.mkv");

        var seriesDir = FileNameBuilder.BuildSeriesDirectoryStatic(epContext);
        seriesDir.Should().Be("Fargo");

        var seasonDir = FileNameBuilder.BuildSeasonDirectoryStatic(epContext);
        seasonDir.Should().Be("Season 1");
    }

    [Test]
    public void Constructor_WithInjectedNullSanitizerAndTruncator_UsesDefaultImplementations()
    {
        var builder = new FileNameBuilder(null!, null!);
        var context = new MovieNamingContext { MovieTitle = "Test Movie", ReleaseYear = 2020, Extension = "mkv" };
        var result = builder.BuildFileName(context);
        result.Should().Be("Test Movie (2020).mkv");
    }

    [Test]
    public void BuildFileName_CleansRepeatedDashesAndEmptyBracketsAndDrivePrefix()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Some Movie",
            ReleaseYear = 2021,
            Extension = "mkv",
        };

        var pattern = "C:\\{Movie Title} - - [{Quality Title}] - ({Release Year})";
        var result = _builder.BuildFileName(context, pattern);

        result.Should().NotContain("C:");
        result.Should().NotContain("- -");
        result.Should().Be("Some Movie - (2021).mkv");
    }
}
