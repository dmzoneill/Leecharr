// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Subtitles;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class MediaOrganizationAndSubtitlesComprehensiveIntegrationTests : IntegrationTestBase
{
    private FileNameBuilder fileNameBuilder;
    private SubtitleDiscoveryService subtitleDiscoveryService;

    [SetUp]
    public void SetUp()
    {
        this.fileNameBuilder = new FileNameBuilder();
        this.subtitleDiscoveryService = new SubtitleDiscoveryService();
    }

    [Test]
    public void FileNameBuilder_MovieNaming_Permutations_BuildsAccurately()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Blade Runner 2049: Director's Cut",
            MovieCleanTitle = "Blade Runner 2049 Director's Cut",
            ReleaseYear = 2017,
            Quality = "Bluray-2160p",
            QualityFull = "Bluray-2160p Remux",
            ReleaseGroup = "FraMeSToR",
            VideoCodec = "x265",
            AudioCodec = "TrueHD Atmos",
            AudioChannels = "7.1",
            HdrFormat = "DV HDR10",
            VideoDynamicRange = "DV HDR10",
            Edition = "Director's Cut",
            ImdbId = "tt1856101",
            TmdbId = "335984",
            Extension = ".mkv",
        };

        // 1. Standard movie format
        var standardName = this.fileNameBuilder.BuildFileName(context);
        standardName.Should().Contain("Blade Runner 2049");
        standardName.Should().Contain("2017");
        standardName.Should().EndWith(".mkv");

        // 2. Custom pattern with all tokens
        var pattern = "{Movie Title} ({Release Year}) [{Quality Title}] [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels}] [{MediaInfo VideoDynamicRange}] [{Edition}] [imdb-{ImdbId}]-{Release Group}";
        var customName = this.fileNameBuilder.BuildFileName(context, pattern);
        customName.Should().Contain("FraMeSToR");
        customName.Should().Contain("tt1856101");
        customName.Should().Contain("x265");
        customName.Should().Contain("7.1");

        // 3. Sanitization of illegal characters
        var config = new NamingConfig
        {
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.SpaceDash,
        };
        var sanitizedName = this.fileNameBuilder.BuildFileName(context, pattern, config);
        sanitizedName.Should().NotContain(":");
    }

    [Test]
    public void FileNameBuilder_EpisodeNaming_Permutations_BuildsAccurately()
    {
        // 1. Standard single episode
        var singleContext = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad: Final Season",
            SeasonNumber = 5,
            EpisodeNumbers = new List<int> { 14 },
            EpisodeTitles = new List<string> { "Ozymandias" },
            Quality = "WEBDL-1080p",
            ReleaseGroup = "NTb",
            VideoCodec = "h264",
            AudioCodec = "EAC3",
            AudioChannels = "5.1",
            Extension = "mkv",
        };

        var singleName = this.fileNameBuilder.BuildFileName(singleContext);
        singleName.Should().Contain("Breaking Bad");
        singleName.Should().Contain("S05E14");
        singleName.Should().Contain("Ozymandias");

        // 2. Multi-episode
        var multiContext = new EpisodeNamingContext
        {
            SeriesTitle = "Avatar: The Last Airbender",
            SeasonNumber = 3,
            EpisodeNumbers = new List<int> { 18, 19, 20, 21 },
            EpisodeTitles = new List<string> { "Sozin's Comet, Part 1", "Part 2", "Part 3", "Part 4" },
            Quality = "Bluray-1080p",
            ReleaseGroup = "CtrlHD",
            Extension = ".mkv",
        };

        var multiName = this.fileNameBuilder.BuildFileName(multiContext);
        multiName.Should().Contain("S03E18-E21");

        // 3. Daily episode with air date
        var dailyContext = new EpisodeNamingContext
        {
            SeriesTitle = "The Daily Show",
            AirDate = new DateTime(2024, 4, 15),
            Quality = "HDTV-720p",
            ReleaseGroup = "FiHTV",
            Extension = ".mp4",
        };

        var dailyName = this.fileNameBuilder.BuildFileName(dailyContext);
        dailyName.Should().Contain("2024-04-15");

        // 4. Anime absolute episode
        var animeContext = new EpisodeNamingContext
        {
            SeriesTitle = "One Piece",
            AbsoluteEpisodeNumbers = new List<int> { 1085 },
            EpisodeTitles = new List<string> { "The Last Island" },
            Quality = "WEBDL-1080p",
            ReleaseGroup = "SubsPlease",
            Extension = ".mkv",
        };

        var animeName = this.fileNameBuilder.BuildFileName(animeContext);
        animeName.Should().Contain("1085");
    }

    [Test]
    public void SubtitleDiscoveryService_FileDetectionAndLanguageParsing_Accurate()
    {
        // 1. IsSubtitleFile
        this.subtitleDiscoveryService.IsSubtitleFile("movie.en.srt").Should().BeTrue();
        this.subtitleDiscoveryService.IsSubtitleFile("movie.es.vtt").Should().BeTrue();
        this.subtitleDiscoveryService.IsSubtitleFile("subtitles.sub").Should().BeTrue();
        this.subtitleDiscoveryService.IsSubtitleFile("movie.mkv").Should().BeFalse();
        this.subtitleDiscoveryService.IsSubtitleFile("").Should().BeFalse();

        // 2. IsMediaFile
        this.subtitleDiscoveryService.IsMediaFile("movie.mkv").Should().BeTrue();
        this.subtitleDiscoveryService.IsMediaFile("video.mp4").Should().BeTrue();
        this.subtitleDiscoveryService.IsMediaFile("clip.avi").Should().BeTrue();
        this.subtitleDiscoveryService.IsMediaFile("movie.srt").Should().BeFalse();
        this.subtitleDiscoveryService.IsMediaFile(null).Should().BeFalse();

        // 3. Language parsing and discovery from filenames
        var subs = this.subtitleDiscoveryService.DiscoverSubtitles(
            "Inception.2010.mkv",
            new[]
            {
                "Inception.2010.en.srt",
                "Inception.2010.Spanish.forced.srt",
                "Inception.2010.fre.sdh.vtt",
                "Inception.2010.jpn.sub",
            });

        subs.Should().HaveCount(4);
        subs.Should().Contain(s => s.TwoLetterCode == "en");
        subs.Should().Contain(s => s.TwoLetterCode == "es" && s.IsForced);
        subs.Should().Contain(s => s.TwoLetterCode == "fr" && s.IsHearingImpaired);
        subs.Should().Contain(s => s.TwoLetterCode == "ja");
    }
}
