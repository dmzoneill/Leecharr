// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class EpisodicParserTest
{
    private EpisodicParser parser = null!;

    [SetUp]
    public void SetUp()
    {
        this.parser = new EpisodicParser();
    }

    [TestCase("The.Show.S01E05.1080p.WEB-DL", 1, 5)]
    [TestCase("The.Show.2x08.720p.HDTV", 2, 8)]
    [TestCase("The.Show.S00E01.Special.1080p", 0, 1)]
    [TestCase("The.Show.SP01.1080p", 0, 1)]
    [TestCase("The.Show.Special.02.1080p", 0, 2)]
    public void ExtractEpisodicInfo_SingleEpisodes_ParsesSeasonAndEpisode(string name, int expectedSeason, int expectedEpisode)
    {
        var (season, episode, title) = this.parser.ExtractEpisodicInfo(name);
        season.Should().Be(expectedSeason);
        episode.Should().Be(expectedEpisode);
        title.Should().BeNull();
    }

    [TestCase("Show.S01E01-E02-E03.1080p", 1, 1, new[] { 1, 2, 3 }, "S01E01-E03")]
    [TestCase("Show.S01E01-E03-E05.1080p", 1, 1, new[] { 1, 3, 5 }, "S01E01E03E05")]
    [TestCase("The.Show.1x01-1x03-1x05.720p", 1, 1, new[] { 1, 3, 5 }, "S01E01E03E05")]
    [TestCase("The.Show.S01E01-E04.1080p.WEB-DL", 1, 1, new[] { 1, 2, 3, 4 }, "S01E01-E04")]
    [TestCase("The.Show.S02E05-08.720p.HDTV", 2, 5, new[] { 5, 6, 7, 8 }, "S02E05-E08")]
    [TestCase("The.Show.S01E01E02.1080p", 1, 1, new[] { 1, 2 }, "S01E01-E02")]
    [TestCase("The.Show.S01E01.S01E02.1080p", 1, 1, new[] { 1, 2 }, "S01E01-E02")]
    [TestCase("The.Show.S01E01E02E03.1080p", 1, 1, new[] { 1, 2, 3 }, "S01E01-E03")]
    [TestCase("The.Show.1x01-04.720p", 1, 1, new[] { 1, 2, 3, 4 }, "S01E01-E04")]
    [TestCase("The.Show.1x01x02.720p", 1, 1, new[] { 1, 2 }, "S01E01-E02")]
    [TestCase("The.Show.1x01.1x02.720p", 1, 1, new[] { 1, 2 }, "S01E01-E02")]
    [TestCase("The.Show.SP03.1080p", 0, 3, new[] { 3 }, "S00E03")]
    public void ExtractDetailedEpisodicInfo_MultiEpisodeRangesAndSpecials_ExtractsAllEpisodes(
        string name,
        int expectedSeason,
        int expectedStartEp,
        int[] expectedEpisodes,
        string expectedFormattedRange)
    {
        var (season, startEp, _, allEpisodes, formattedRange) = this.parser.ExtractDetailedEpisodicInfo(name);
        season.Should().Be(expectedSeason);
        startEp.Should().Be(expectedStartEp);
        allEpisodes.Should().Equal(expectedEpisodes);
        formattedRange.Should().Be(expectedFormattedRange);
    }

    [TestCase("The.Show.S01E02-720.WEB.mkv", 1, 2, new[] { 2 }, "S01E02")]
    [TestCase("The.Show.S01E02-264.mkv", 1, 2, new[] { 2 }, "S01E02")]
    [TestCase("The.Show.S01E02-720p.WEB.mkv", 1, 2, new[] { 2 }, "S01E02")]
    [TestCase("The.Show.1x02-720.WEB.mkv", 1, 2, new[] { 2 }, "S01E02")]
    public void ExtractDetailedEpisodicInfo_ResolutionSuffix_DoesNotParseAsEpisode(
        string name,
        int expectedSeason,
        int expectedStartEp,
        int[] expectedEpisodes,
        string expectedFormattedRange)
    {
        var (season, startEp, _, allEpisodes, formattedRange) = this.parser.ExtractDetailedEpisodicInfo(name);
        season.Should().Be(expectedSeason);
        startEp.Should().Be(expectedStartEp);
        allEpisodes.Should().Equal(expectedEpisodes);
        formattedRange.Should().Be(expectedFormattedRange);
    }

    [Test]
    public void ExtractEpisodeNumbers_ReturnsListOfAllParsedEpisodes()
    {
        var episodes = this.parser.ExtractEpisodeNumbers("The.Show.S03E10-E13.1080p");
        episodes.Should().Equal(10, 11, 12, 13);
    }

    [Test]
    public void ExtractEpisodicInfo_WhenNameNullOrEmpty_ReturnsNulls()
    {
        var (season, episode, title) = this.parser.ExtractEpisodicInfo(null!);
        season.Should().BeNull();
        episode.Should().BeNull();
        title.Should().BeNull();
    }

    [TestCase(0, "00:00:00")]
    [TestCase(-1, "00:00:00")]
    [TestCase(3661, "01:01:01")]
    [TestCase(90061, "1d 01:01:01")]
    [TestCase(8640000, "100d 00:00:00")]
    [TestCase(10000000, "115d 17:46:40")]
    public void FormatEta_FormatsSecondsIncludingLongDurations(long seconds, string expected)
    {
        this.parser.FormatEta(seconds).Should().Be(expected);
        EpisodicParser.FormatEtaStatic(seconds).Should().Be(expected);
    }
}
