// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class RssSyncServiceTest
{
    private IIndexerRepository indexerRepository = null!;
    private IRssRuleRepository rssRuleRepository = null!;
    private ITorznabClient torznabClient = null!;
    private ITorrentService torrentService = null!;
    private RssSyncService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.indexerRepository = Substitute.For<IIndexerRepository>();
        this.rssRuleRepository = Substitute.For<IRssRuleRepository>();
        this.torznabClient = Substitute.For<ITorznabClient>();
        this.torrentService = Substitute.For<ITorrentService>();

        this.service = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService);
    }

    #region Rule Matching Tests

    [Test]
    public void MatchesRule_WhenMustContainMatches_ReturnsTrue()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.2160p.WEB-DL.x265",
            Seeders = 10,
            Size = 5000000000,
        };

        var rule = new RssRule
        {
            Name = "Severance 2160p",
            IsEnabled = true,
            MustContain = "Severance.*2160p",
            MinSeeders = 5,
        };

        this.service.MatchesRule(release, rule).Should().BeTrue();
    }

    [Test]
    public void MatchesRule_WhenMustContainDoesNotMatch_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.1080p.WEB-DL.x265",
            Seeders = 10,
        };

        var rule = new RssRule
        {
            Name = "Severance 2160p",
            IsEnabled = true,
            MustContain = "Severance.*2160p",
        };

        this.service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenMustNotContainMatches_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.720p.HDTV.x264",
            Seeders = 10,
        };

        var rule = new RssRule
        {
            Name = "No 720p",
            IsEnabled = true,
            MustNotContain = "720p|HDTV",
        };

        this.service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenBelowMinSeeders_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Dune.Part.Two.2024.2160p",
            Seeders = 2,
        };

        var rule = new RssRule
        {
            Name = "High Seeders Only",
            IsEnabled = true,
            MinSeeders = 10,
        };

        this.service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenSizeOutsideBounds_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Movie.2024.1080p",
            Seeders = 10,
            Size = 1000000000, // 1 GB
        };

        var minRule = new RssRule
        {
            Name = "Min 2GB",
            IsEnabled = true,
            MinSizeBytes = 2000000000,
        };

        var maxRule = new RssRule
        {
            Name = "Max 500MB",
            IsEnabled = true,
            MaxSizeBytes = 500000000,
        };

        this.service.MatchesRule(release, minRule).Should().BeFalse();
        this.service.MatchesRule(release, maxRule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenFreeleechOnlyRequiredAndNotFreeleech_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Sample Release",
            Seeders = 10,
            DownloadVolumeFactor = 1.0,
        };

        var rule = new RssRule
        {
            Name = "Freeleech Only",
            IsEnabled = true,
            FreeleechOnly = true,
        };

        this.service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenCategoryIdSpecified_MatchesOnlySameCategory()
    {
        var release5040 = new TorznabSearchResult
        {
            Title = "Show.S01E01",
            Seeders = 10,
            Category = "5040",
        };

        var release2040 = new TorznabSearchResult
        {
            Title = "Movie.2024",
            Seeders = 10,
            Category = "2040",
        };

        var rule = new RssRule
        {
            Name = "TV HD Only",
            IsEnabled = true,
            CategoryId = 5040,
        };

        this.service.MatchesRule(release5040, rule).Should().BeTrue();
        this.service.MatchesRule(release2040, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenRuleDisabledOrNull_ReturnsFalse()
    {
        var release = new TorznabSearchResult { Title = "Test" };
        var disabledRule = new RssRule { IsEnabled = false };

        this.service.MatchesRule(release, disabledRule).Should().BeFalse();
        this.service.MatchesRule(release, null!).Should().BeFalse();
        this.service.MatchesRule(null!, disabledRule).Should().BeFalse();
    }

    #endregion

    #region Duplicate Grab Prevention and Sync Tests

    [Test]
    public async Task SyncRssFeedsAsync_PreventsDuplicateGrabsAcrossRuns()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule
        {
            Id = 1,
            Name = "Catch All",
            IsEnabled = true,
            MinSeeders = 1,
        };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:12345",
            Title = "Dune.Part.Two.2024.2160p",
            MagnetUrl = "magnet:?xt=urn:btih:FEDCBA0987654321FEDCBA0987654321FEDCBA09",
            Seeders = 20,
            DownloadVolumeFactor = 0,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        // First sync run: should grab the release
        var firstCount = await this.service.SyncRssFeedsAsync();
        firstCount.Should().Be(1);
        await this.torrentService.Received(1).AddFromMagnetAsync(release.MagnetUrl);

        // Second sync run: identical release should be detected as duplicate and skipped
        var secondCount = await this.service.SyncRssFeedsAsync();
        secondCount.Should().Be(0);
        await this.torrentService.Received(1).AddFromMagnetAsync(Arg.Any<string>()); // Still only 1 total invocation
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenMultipleRulesMatchSameRelease_GrabsOnlyOnce()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule1 = new RssRule { Id = 1, Name = "Rule 1", IsEnabled = true, MinSeeders = 1 };
        var rule2 = new RssRule { Id = 2, Name = "Rule 2", IsEnabled = true, MinSeeders = 1 };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule1, rule2 });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:unique-multi-rule",
            Title = "Unique.Release.2024",
            MagnetUrl = "magnet:?xt=urn:btih:1111111111111111111111111111111111111111",
            Seeders = 10,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await this.service.SyncRssFeedsAsync();
        grabbedCount.Should().Be(1);
        await this.torrentService.Received(1).AddFromMagnetAsync(release.MagnetUrl);
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenNoActiveIndexersOrRules_ReturnsZero()
    {
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition>());
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule>());

        var count = await this.service.SyncRssFeedsAsync();
        count.Should().Be(0);
    }

    #endregion
}
