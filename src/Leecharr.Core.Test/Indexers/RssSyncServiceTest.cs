// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Http;
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
    private IDownloadHistoryService downloadHistoryService = null!;
    private RssSyncService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.indexerRepository = Substitute.For<IIndexerRepository>();
        this.rssRuleRepository = Substitute.For<IRssRuleRepository>();
        this.torznabClient = Substitute.For<ITorznabClient>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.downloadHistoryService = Substitute.For<IDownloadHistoryService>();

        this.torrentService.AddFromMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(callInfo => Task.FromResult(new Torrent { Id = 1, Name = "Test Torrent", InfoHash = "FEDCBA0987654321FEDCBA0987654321FEDCBA09" }));

        this.service = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService,
            downloadHistoryService: this.downloadHistoryService);
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
    public void MatchesRule_WhenDestinationCategoryIdConfigured_StillMatchesTorznabReleases()
    {
        var release = new TorznabSearchResult
        {
            Title = "Show.S01E01",
            Seeders = 10,
            Category = "5040",
        };

        var rule = new RssRule
        {
            Name = "TV HD Rule",
            IsEnabled = true,
            CategoryId = 1, // Local Category ID = 1 (e.g. TV)
        };

        this.service.MatchesRule(release, rule).Should().BeTrue();
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

    [Test]
    public void MatchesRule_WhenMustContainReDosPatternTimesOut_ReturnsFalseWithoutHanging()
    {
        var release = new TorznabSearchResult
        {
            Title = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!",
            Seeders = 10,
        };

        var rule = new RssRule
        {
            Name = "ReDoS MustContain Rule",
            IsEnabled = true,
            MustContain = @"^((a+)+)+$",
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = this.service.MatchesRule(release, rule);
        stopwatch.Stop();

        result.Should().BeFalse();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    [Test]
    public void MatchesRule_WhenMustNotContainReDosPatternTimesOut_ReturnsFalseWithoutHanging()
    {
        var release = new TorznabSearchResult
        {
            Title = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!",
            Seeders = 10,
        };

        var rule = new RssRule
        {
            Name = "ReDoS MustNotContain Rule",
            IsEnabled = true,
            MustNotContain = @"^((a+)+)+$",
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = this.service.MatchesRule(release, rule);
        stopwatch.Stop();

        result.Should().BeFalse();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    [Test]
    public void MatchesRule_WhenRegexSyntaxIsInvalid_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.2160p",
            Seeders = 10,
        };

        var invalidMustContainRule = new RssRule
        {
            Name = "Invalid Regex MustContain",
            IsEnabled = true,
            MustContain = "[unclosed-bracket",
        };

        var invalidMustNotContainRule = new RssRule
        {
            Name = "Invalid Regex MustNotContain",
            IsEnabled = true,
            MustNotContain = "(?P<invalid_group_syntax>",
        };

        this.service.MatchesRule(release, invalidMustContainRule).Should().BeFalse();
        this.service.MatchesRule(release, invalidMustNotContainRule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenTitleIsNull_HandlesSafely()
    {
        var release = new TorznabSearchResult
        {
            Title = null!,
            Seeders = 10,
        };

        var rule = new RssRule
        {
            Name = "MustContain Rule",
            IsEnabled = true,
            MustContain = "Severance",
        };

        this.service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenReleaseAgeExceedsMaxAgeDays_ReturnsFalse()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.2160p",
            Seeders = 10,
            PublishDate = System.DateTime.UtcNow.AddDays(-10),
        };

        var rule = new RssRule
        {
            Name = "MaxAge 5 Days",
            IsEnabled = true,
            MaxAgeDays = 5,
        };

        this.service.MatchesRule(release, rule).Should().BeFalse();
    }

    [Test]
    public void MatchesRule_WhenReleaseAgeWithinMaxAgeDays_ReturnsTrue()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.2160p",
            Seeders = 10,
            PublishDate = System.DateTime.UtcNow.AddDays(-2),
        };

        var rule = new RssRule
        {
            Name = "MaxAge 5 Days",
            IsEnabled = true,
            MaxAgeDays = 5,
        };

        this.service.MatchesRule(release, rule).Should().BeTrue();
    }

    [Test]
    public void MatchesRule_WhenMaxAgeDaysIsZero_MatchesRegardlessOfAge()
    {
        var release = new TorznabSearchResult
        {
            Title = "Severance.S02E01.2160p",
            Seeders = 10,
            PublishDate = System.DateTime.UtcNow.AddDays(-300),
        };

        var rule = new RssRule
        {
            Name = "No MaxAge Limit",
            IsEnabled = true,
            MaxAgeDays = 0,
        };

        this.service.MatchesRule(release, rule).Should().BeTrue();
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

    [Test]
    public async Task SyncRssFeedsAsync_WhenRuleIndexerIdsIsNull_DoesNotThrowAndMatches()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule
        {
            Id = 1,
            Name = "Null IndexerIds Rule",
            IsEnabled = true,
            MinSeeders = 1,
            IndexerIds = null,
        };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:null-indexer-ids",
            Title = "Valid.Release.2024",
            MagnetUrl = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222",
            Seeders = 5,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await this.service.SyncRssFeedsAsync();
        grabbedCount.Should().Be(1);
        await this.torrentService.Received(1).AddFromMagnetAsync(release.MagnetUrl);
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenGrabbedViaMagnet_RecordsDownloadHistoryWithAttribution()
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
            Guid = "urn:guid:magnet-grab",
            Title = "Dune.Part.Two.2024.2160p",
            MagnetUrl = "magnet:?xt=urn:btih:FEDCBA0987654321FEDCBA0987654321FEDCBA09",
            DownloadUrl = "https://tracker.example.com/download.torrent",
            Seeders = 20,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await this.service.SyncRssFeedsAsync();
        grabbedCount.Should().Be(1);

        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            Arg.Is<Torrent>(t => t.Id == 1 && t.InfoHash == "FEDCBA0987654321FEDCBA0987654321FEDCBA09"),
            source: "RSS: Catch All",
            magnetUrl: release.MagnetUrl,
            downloadUrl: release.DownloadUrl,
            indexerName: "AlphaTracker");
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenGrabbedViaTorrentDownloadUrl_RecordsDownloadHistoryWithAttribution()
    {
        var safeHttpClient = Substitute.For<ISafeHttpClientService>();
        var parser = Substitute.For<ITorrentFileParser>();
        var parsedTorrent = new ParsedTorrent
        {
            Name = "Parsed.Torrent.Release",
            InfoHash = "1122334455667788990011223344556677889900",
        };
        var torrentBytes = new byte[] { 1, 2, 3, 4 };
        var createdTorrent = new Torrent
        {
            Id = 2,
            Name = "Parsed.Torrent.Release",
            InfoHash = parsedTorrent.InfoHash,
        };

        safeHttpClient.DownloadBytesAsync("https://tracker.example.com/file.torrent", Arg.Any<long>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(torrentBytes));
        parser.Parse(torrentBytes).Returns(parsedTorrent);
        this.torrentService.AddFromParsedTorrentAsync(parsedTorrent, null, null, false, torrentBytes)
            .Returns(Task.FromResult(createdTorrent));

        var testService = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService,
            torrentFileParser: parser,
            safeHttpClientService: safeHttpClient,
            downloadHistoryService: this.downloadHistoryService);

        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule
        {
            Id = 1,
            Name = "Torrent Rule",
            IsEnabled = true,
            MinSeeders = 1,
        };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:download-url-grab",
            Title = "Parsed.Torrent.Release",
            DownloadUrl = "https://tracker.example.com/file.torrent",
            MagnetUrl = null,
            Seeders = 10,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await testService.SyncRssFeedsAsync();
        grabbedCount.Should().Be(1);

        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            Arg.Is<Torrent>(t => t.Id == 2 && t.InfoHash == parsedTorrent.InfoHash),
            source: "RSS: Torrent Rule",
            magnetUrl: null,
            downloadUrl: release.DownloadUrl,
            indexerName: "AlphaTracker");
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenDownloadHistoryServiceIsNull_DoesNotThrow()
    {
        var testService = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService,
            downloadHistoryService: null);

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
            Guid = "urn:guid:null-history-svc",
            Title = "Dune.Part.Two.2024.2160p",
            MagnetUrl = "magnet:?xt=urn:btih:FEDCBA0987654321FEDCBA0987654321FEDCBA09",
            Seeders = 20,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var act = () => testService.SyncRssFeedsAsync();
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenDownloadUrlIsMagnetAndMagnetUrlEmpty_GrabsViaMagnet()
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
            Guid = "urn:guid:magnet-download-url",
            Title = "Magnet.DownloadUrl.Release",
            MagnetUrl = null,
            DownloadUrl = "magnet:?xt=urn:btih:FEDCBA0987654321FEDCBA0987654321FEDCBA09",
            Seeders = 20,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await this.service.SyncRssFeedsAsync();
        grabbedCount.Should().Be(1);
        await this.torrentService.Received(1).AddFromMagnetAsync(release.DownloadUrl, null, null);
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenRuleHasCategoryId_PassesCategoryNameAndSavePathToTorrentService()
    {
        var categoryService = Substitute.For<ICategoryService>();
        categoryService.Get(10).Returns(new Category { Id = 10, Name = "TV", SavePath = "/downloads/tv" });

        var testService = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService,
            downloadHistoryService: this.downloadHistoryService,
            categoryService: categoryService);

        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule
        {
            Id = 1,
            Name = "TV Rule",
            IsEnabled = true,
            MinSeeders = 1,
            CategoryId = 10,
        };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:category-test",
            Title = "Severance.S01E01.1080p",
            Category = "10",
            MagnetUrl = "magnet:?xt=urn:btih:FEDCBA0987654321FEDCBA0987654321FEDCBA09",
            Seeders = 10,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await testService.SyncRssFeedsAsync();
        grabbedCount.Should().Be(1);
        await this.torrentService.Received(1).AddFromMagnetAsync(release.MagnetUrl, "TV", "/downloads/tv");
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenRestartedAndReleaseExistsInDownloadHistory_SkipsGrabAndHistoryRecording()
    {
        var infoHash = "FEDCBA0987654321FEDCBA0987654321FEDCBA09";
        var existingHistory = new DownloadHistory { Id = 10, InfoHash = infoHash, Title = "Existing Torrent" };
        this.downloadHistoryService.GetByInfoHash(Arg.Is<string>(h => string.Equals(h, infoHash, System.StringComparison.OrdinalIgnoreCase))).Returns(existingHistory);

        var restartedService = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService,
            downloadHistoryService: this.downloadHistoryService);

        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule { Id = 1, Name = "Catch All", IsEnabled = true, MinSeeders = 1 };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:after-restart",
            Title = "Existing.Release.2024",
            MagnetUrl = $"magnet:?xt=urn:btih:{infoHash}",
            Seeders = 20,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await restartedService.SyncRssFeedsAsync();
        grabbedCount.Should().Be(0);

        await this.torrentService.DidNotReceive().AddFromMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.downloadHistoryService.DidNotReceive().RecordTorrentAdded(Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenRestartedAndTorrentParsedInfoHashExistsInHistory_SkipsGrabAndHistoryRecording()
    {
        var infoHash = "1122334455667788990011223344556677889900";
        this.downloadHistoryService.GetByInfoHash(infoHash).Returns(new DownloadHistory { Id = 5, InfoHash = infoHash });

        var safeHttpClient = Substitute.For<ISafeHttpClientService>();
        var parser = Substitute.For<ITorrentFileParser>();
        var parsedTorrent = new ParsedTorrent
        {
            Name = "Parsed.Duplicate.Release",
            InfoHash = infoHash,
        };
        var torrentBytes = new byte[] { 1, 2, 3, 4 };

        safeHttpClient.DownloadBytesAsync("https://tracker.example.com/duplicate.torrent", Arg.Any<long>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(torrentBytes));
        parser.Parse(torrentBytes).Returns(parsedTorrent);

        var restartedService = new RssSyncService(
            this.indexerRepository,
            this.rssRuleRepository,
            this.torznabClient,
            this.torrentService,
            torrentFileParser: parser,
            safeHttpClientService: safeHttpClient,
            downloadHistoryService: this.downloadHistoryService);

        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule { Id = 1, Name = "Catch All", IsEnabled = true, MinSeeders = 1 };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:torrent-file-restart",
            Title = "Parsed.Duplicate.Release",
            DownloadUrl = "https://tracker.example.com/duplicate.torrent",
            Seeders = 10,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await restartedService.SyncRssFeedsAsync();
        grabbedCount.Should().Be(0);

        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
        this.downloadHistoryService.DidNotReceive().RecordTorrentAdded(Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenReleaseExistsInTorrentService_SkipsGrabAndHistoryRecording()
    {
        var infoHash = "AABBCCDDEEFF00112233445566778899AABBCCDD";
        var existingTorrent = new Torrent { Id = 77, InfoHash = infoHash.ToLowerInvariant(), Name = "Existing Torrent" };
        this.torrentService.GetByInfoHash(Arg.Is<string>(h => string.Equals(h, infoHash, System.StringComparison.OrdinalIgnoreCase))).Returns(existingTorrent);

        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule { Id = 1, Name = "Catch All", IsEnabled = true, MinSeeders = 1 };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:existing-torrent-service",
            Title = "Existing.Release.In.TorrentService",
            MagnetUrl = $"magnet:?xt=urn:btih:{infoHash}",
            Seeders = 15,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await this.service.SyncRssFeedsAsync();
        grabbedCount.Should().Be(0);

        await this.torrentService.DidNotReceive().AddFromMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.downloadHistoryService.DidNotReceive().RecordTorrentAdded(Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenTorrentServiceReturnsPreExistingTorrent_DoesNotRecordHistoryOrIncrementGrabbedCount()
    {
        var infoHash = "9988776655443322110099887766554433221100";
        var existingTorrent = new Torrent { Id = 88, InfoHash = infoHash, Name = "Pre-existing Torrent" };
        var existingHistory = new DownloadHistory { Id = 12, InfoHash = infoHash };

        // TorrentService returns pre-existing torrent instance
        this.torrentService.AddFromMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(Task.FromResult(existingTorrent));
        this.downloadHistoryService.GetByInfoHash(Arg.Is<string>(h => string.Equals(h, infoHash, System.StringComparison.OrdinalIgnoreCase)))
            .Returns(existingHistory);

        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule { Id = 1, Name = "Catch All", IsEnabled = true, MinSeeders = 1 };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:pre-existing-torrent",
            Title = "Duplicate.Torrent.Release",
            MagnetUrl = $"magnet:?xt=urn:btih:{infoHash}",
            Seeders = 10,
        };

        this.torznabClient.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbedCount = await this.service.SyncRssFeedsAsync();
        grabbedCount.Should().Be(0);

        this.downloadHistoryService.DidNotReceive().RecordTorrentAdded(Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task SyncRssFeedsAsync_WhenConcurrentCallsMade_SecondCallReturnsZeroGracefully()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "AlphaTracker", EnableRss = true };
        this.indexerRepository.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule { Id = 1, Name = "Catch All", IsEnabled = true, MinSeeders = 1 };
        this.rssRuleRepository.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "urn:guid:concurrent-test",
            Title = "Concurrent.Release.2024",
            MagnetUrl = "magnet:?xt=urn:btih:3333333333333333333333333333333333333333",
            Seeders = 10,
        };

        var tcs = new TaskCompletionSource<List<TorznabSearchResult>>();

        this.torznabClient.FetchRssAsync(indexer).Returns(tcs.Task);

        // Start first sync call which will pause inside FetchRssAsync
        var firstTask = this.service.SyncRssFeedsAsync();

        // Second call while first is still running should return 0 immediately
        var secondCount = await this.service.SyncRssFeedsAsync();
        secondCount.Should().Be(0);

        // Now complete the first call
        tcs.SetResult(new List<TorznabSearchResult> { release });
        var firstCount = await firstTask;
        firstCount.Should().Be(1);
    }

    #endregion
}
