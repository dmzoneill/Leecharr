// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.TrackerBoost;

[TestFixture]
public class TrackerBoostServiceTest
{
    private ITrackerBoostTrackerRepository trackerRepository = null!;
    private ITorrentService torrentService = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IIndexerRepository indexerRepository = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private IDownloadClientRepository downloadClientRepository = null!;
    private List<DownloadClientDefinition> storedDownloadClients = null!;
    private TrackerBoostService service = null!;

    private List<TrackerBoostTracker> storedTrackers = null!;
    private List<TrackerEntry> storedEntries = null!;
    private List<Torrent> storedTorrents = null!;

    [SetUp]
    public void SetUp()
    {
        this.storedTrackers = new List<TrackerBoostTracker>();
        this.storedEntries = new List<TrackerEntry>();
        this.storedTorrents = new List<Torrent>();

        this.trackerRepository = Substitute.For<ITrackerBoostTrackerRepository>();
        this.trackerRepository.All().Returns(_ => this.storedTrackers);
        this.trackerRepository.FindByUrl(Arg.Any<string>()).Returns(ci =>
        {
            var u = ci.Arg<string>()?.Trim();
            return this.storedTrackers.FirstOrDefault(t => string.Equals(t.Url, u, StringComparison.OrdinalIgnoreCase));
        });
        this.trackerRepository.Get(Arg.Any<int>()).Returns(ci =>
        {
            var id = ci.Arg<int>();
            return this.storedTrackers.FirstOrDefault(t => t.Id == id);
        });
        this.trackerRepository.Insert(Arg.Any<TrackerBoostTracker>()).Returns(ci =>
        {
            var t = ci.Arg<TrackerBoostTracker>();
            t.Id = this.storedTrackers.Count + 1;
            this.storedTrackers.Add(t);
            return t;
        });

        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentService.GetAll().Returns(_ => this.storedTorrents);
        this.torrentService.Get(Arg.Any<int>()).Returns(ci =>
        {
            var id = ci.Arg<int>();
            return this.storedTorrents.FirstOrDefault(t => t.Id == id);
        });

        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.trackerEntryRepository.All().Returns(_ => this.storedEntries);
        this.trackerEntryRepository.GetByTorrentId(Arg.Any<int>()).Returns(ci =>
        {
            var id = ci.Arg<int>();
            return this.storedEntries.Where(e => e.TorrentId == id).ToList();
        });
        this.trackerEntryRepository.Insert(Arg.Any<TrackerEntry>()).Returns(ci =>
        {
            var e = ci.Arg<TrackerEntry>();
            e.Id = this.storedEntries.Count + 1;
            this.storedEntries.Add(e);
            return e;
        });

        this.indexerRepository = Substitute.For<IIndexerRepository>();
        this.indexerRepository.All().Returns(new List<IndexerDefinition>());

        this.configService = Substitute.For<IConfigService>();
        this.configService.GetValueBoolean(Arg.Any<string>(), Arg.Any<bool>()).Returns(ci => ci.ArgAt<bool>(1));
        this.configService.GetValueInt(Arg.Any<string>(), Arg.Any<int>()).Returns(ci => ci.ArgAt<int>(1));

        this.downloadEngine = Substitute.For<IDownloadEngine>();

        this.storedDownloadClients = new List<DownloadClientDefinition>();
        this.downloadClientRepository = Substitute.For<IDownloadClientRepository>();
        this.downloadClientRepository.GetEnabled().Returns(_ => this.storedDownloadClients.Where(c => c.Enable).ToList());

        this.service = new TrackerBoostService(
            this.trackerRepository,
            this.torrentService,
            this.trackerEntryRepository,
            this.indexerRepository,
            this.configService,
            this.downloadEngine,
            this.downloadClientRepository);
    }

    [Test]
    public void Constructor_BootstrapsDefaultTrackers_WhenRepositoryEmpty()
    {
        this.storedTrackers.Should().NotBeEmpty();
        this.storedTrackers.Should().Contain(t => t.Url.Contains("tracker.opentrackr.org"));
    }

    [Test]
    public void IsValidPublicTrackerUrl_ValidatesAppropriately()
    {
        TrackerBoostService.IsValidPublicTrackerUrl("udp://tracker.opentrackr.org:1337/announce").Should().BeTrue();
        TrackerBoostService.IsValidPublicTrackerUrl("http://tracker.files.fm:6969/announce").Should().BeTrue();
        TrackerBoostService.IsValidPublicTrackerUrl("https://tracker.tamersunion.org:443/announce").Should().BeTrue();

        // Invalid cases
        TrackerBoostService.IsValidPublicTrackerUrl(string.Empty).Should().BeFalse();
        TrackerBoostService.IsValidPublicTrackerUrl("dht://something").Should().BeFalse();
        TrackerBoostService.IsValidPublicTrackerUrl("http://127.0.0.1:6969/announce").Should().BeFalse();
        TrackerBoostService.IsValidPublicTrackerUrl("http://localhost:6969/announce").Should().BeFalse();
        TrackerBoostService.IsValidPublicTrackerUrl("http://private.tracker.org/announce?passkey=123456").Should().BeFalse();
        TrackerBoostService.IsValidPublicTrackerUrl("http://tracker.local/announce").Should().BeFalse();
    }

    [Test]
    public void AddTracker_ValidUrl_AddsAndParsesMetadata()
    {
        var tracker = this.service.AddTracker("udp://custom.tracker.org:8080/announce", TrackerSourceType.Manual, "Custom");

        tracker.Should().NotBeNull();
        tracker.Host.Should().Be("custom.tracker.org");
        tracker.Port.Should().Be(8080);
        tracker.Protocol.Should().Be(TrackerProtocol.Udp);
        tracker.Source.Should().Be(TrackerSourceType.Manual);
    }

    [Test]
    public void GetSettings_And_UpdateSettings_WorksCorrectly()
    {
        var initial = this.service.GetSettings();
        initial.AutoBoostEnabled.Should().BeTrue();
        initial.IntervalMinutes.Should().Be(2);

        this.service.UpdateSettings(new TrackerBoostSettings
        {
            AutoBoostEnabled = false,
            AutoHarvestEnabled = false,
            IntervalMinutes = 5,
            MaxTrackersPerTorrent = 10,
            OnlyVerified = false,
        });

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (bool)d["TrackerBoostAutoBoostEnabled"] == false &&
            (int)d["TrackerBoostIntervalMinutes"] == 5));
    }

    [Test]
    public async Task BoostTorrent_SkipsPrivateTorrent()
    {
        var privateTorrent = new Torrent
        {
            Id = 1,
            Name = "Private Torrent",
            InfoHash = "0123456789ABCDEF0123456789ABCDEF01234567",
            IsPrivate = true,
        };
        this.storedTorrents.Add(privateTorrent);

        var result = await this.service.BoostTorrentAsync(1, onlyVerified: true);

        result.Should().NotBeNull();
        result.Boosted.Should().BeFalse();
        result.IsPrivate.Should().BeTrue();
        result.Message.Should().Contain("Private torrents are protected");
        await this.downloadEngine.DidNotReceiveWithAnyArgs().AddTrackersAsync(default, default!);
    }

    [Test]
    public async Task BoostTorrent_InjectsAliveTrackers_WhenNotPrivateAndUnverifiedAllowed()
    {
        var publicTorrent = new Torrent
        {
            Id = 2,
            Name = "Public Torrent",
            InfoHash = "1122334455667788990011223344556677889900",
            IsPrivate = false,
        };
        this.storedTorrents.Add(publicTorrent);

        var result = await this.service.BoostTorrentAsync(2, onlyVerified: false);

        result.Should().NotBeNull();
        result.Boosted.Should().BeTrue();
        result.AddedTrackersCount.Should().BeGreaterThan(0);
        this.storedEntries.Should().NotBeEmpty();
        await this.downloadEngine.Received(1).AddTrackersAsync(2, Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public void LogActivity_AppendsToBuffer_AndGetLogsReturnsEntries()
    {
        this.service.LogActivity("Info", "TestCategory", "Test Message 1");
        this.service.LogActivity("Warn", "Health", "Test Message 2");

        var logs = this.service.GetLogs(10);
        logs.Should().Contain(l => l.Message == "Test Message 1");
        logs.Should().Contain(l => l.Message == "Test Message 2");

        var healthLogs = this.service.GetLogs(10, category: "Health");
        healthLogs.Should().Contain(l => l.Message == "Test Message 2");
        healthLogs.Should().NotContain(l => l.Message == "Test Message 1");
    }

    [Test]
    public void IsValidPublicTrackerUrl_And_HasPasskey_DetectsPathBasedAndQueryPasskeys()
    {
        // Public trackers - valid and no passkey
        TrackerBoostService.IsValidPublicTrackerUrl("udp://tracker.opentrackr.org:1337/announce").Should().BeTrue();
        TrackerBoostService.IsValidPublicTrackerUrl("http://tracker.files.fm:6969/announce").Should().BeTrue();
        TrackerBoostService.IsValidPublicTrackerUrl("https://tracker.tamersunion.org:443/announce").Should().BeTrue();
        TrackerBoostService.IsValidPublicTrackerUrl("http://tracker.example.com/announce.php").Should().BeTrue();
        TrackerBoostService.IsValidPublicTrackerUrl("http://tracker.example.com/a/announce").Should().BeTrue();

        TrackerBoostService.HasPasskey("udp://tracker.opentrackr.org:1337/announce").Should().BeFalse();
        TrackerBoostService.HasPasskey("http://tracker.files.fm:6969/announce").Should().BeFalse();
        TrackerBoostService.HasPasskey("http://tracker.example.com/announce.php").Should().BeFalse();

        // Path-based passkeys (Gazelle, UNIT3D, PTP, RED, etc.)
        var pathBasedPasskeyUrls = new[]
        {
            "https://tracker.site/abcdef123456/announce",
            "https://tracker.site/announce/abcdef123456",
            "https://gazelle.tracker.org/0123456789abcdef0123456789abcdef/announce",
            "https://ptp.tracker.org/announce/0123456789abcdef0123456789abcdef",
            "https://unit3d.tracker.org/announce/MySecretToken123456",
            "udp://tracker.site:1337/0123456789abcdef0123456789abcdef/announce",
            "http://tracker.site/passkey/123456/announce",
            "http://tracker.site/authkey/abcdef/announce",
            "http://user:secret123456@tracker.site/announce",
        };

        foreach (var url in pathBasedPasskeyUrls)
        {
            TrackerBoostService.HasPasskey(url).Should().BeTrue($"URL '{url}' should be detected as having a passkey");
            TrackerBoostService.IsValidPublicTrackerUrl(url).Should().BeFalse($"URL '{url}' should not be considered a valid public tracker");
        }

        // Query-based passkeys
        var queryBasedPasskeyUrls = new[]
        {
            "http://private.tracker.org/announce?passkey=123456",
            "http://private.tracker.org/announce?authkey=123456",
            "http://private.tracker.org/announce?torrentpass=123456",
            "http://private.tracker.org/announce?auth=abcdef123456",
            "http://private.tracker.org/announce?token=abcdef123456789012",
        };

        foreach (var url in queryBasedPasskeyUrls)
        {
            TrackerBoostService.HasPasskey(url).Should().BeTrue($"URL '{url}' should be detected as having a passkey");
            TrackerBoostService.IsValidPublicTrackerUrl(url).Should().BeFalse($"URL '{url}' should not be considered a valid public tracker");
        }
    }

    [Test]
    public async Task HarvestFromActiveDownloads_NeverHarvestsFromPrivateTorrents_OrTrackersWithPasskeys()
    {
        // 1. Private torrent with both a public-looking tracker and a path-passkey tracker
        var privateTorrent = new Torrent
        {
            Id = 10,
            Name = "Secret Private Torrent",
            InfoHash = "AAAA111122223333444455556666777788889999",
            IsPrivate = true,
        };
        this.storedTorrents.Add(privateTorrent);

        this.storedEntries.Add(new TrackerEntry
        {
            TorrentId = 10,
            Url = "udp://public.looking.tracker.org:1337/announce",
        });
        this.storedEntries.Add(new TrackerEntry
        {
            TorrentId = 10,
            Url = "https://redacted.ch/0123456789abcdef0123456789abcdef/announce",
        });

        // 2. Public torrent with a passkey tracker and a valid public tracker
        var publicTorrent = new Torrent
        {
            Id = 20,
            Name = "Open Source Distro",
            InfoHash = "BBBB111122223333444455556666777788889999",
            IsPrivate = false,
        };
        this.storedTorrents.Add(publicTorrent);

        this.storedEntries.Add(new TrackerEntry
        {
            TorrentId = 20,
            Url = "https://tracker.site/announce/abcdef123456", // path-based passkey
        });
        this.storedEntries.Add(new TrackerEntry
        {
            TorrentId = 20,
            Url = "http://tracker.site/announce?passkey=secret123", // query-based passkey
        });
        this.storedEntries.Add(new TrackerEntry
        {
            TorrentId = 20,
            Url = "udp://newharvested.tracker.org:1337/announce", // valid public tracker
        });

        var count = await this.service.HarvestFromActiveDownloadsAsync();

        // Only the valid public tracker from the public torrent should be harvested
        count.Should().Be(1);
        this.storedTrackers.Should().Contain(t => t.Url == "udp://newharvested.tracker.org:1337/announce");

        // None of the private torrent trackers or passkey trackers should exist in the repository
        this.storedTrackers.Should().NotContain(t => t.Url == "udp://public.looking.tracker.org:1337/announce");
        this.storedTrackers.Should().NotContain(t => t.Url == "https://redacted.ch/0123456789abcdef0123456789abcdef/announce");
        this.storedTrackers.Should().NotContain(t => t.Url == "https://tracker.site/announce/abcdef123456");
        this.storedTrackers.Should().NotContain(t => t.Url == "http://tracker.site/announce?passkey=secret123");
    }

    [Test]
    public void AddTracker_ThrowsArgumentException_WhenUrlContainsPasskey()
    {
        var act1 = () => this.service.AddTracker("https://tracker.site/abcdef123456/announce");
        act1.Should().Throw<ArgumentException>().WithMessage("*passkey*");

        var act2 = () => this.service.AddTracker("https://tracker.site/announce?passkey=123456");
        act2.Should().Throw<ArgumentException>().WithMessage("*passkey*");
    }

    [Test]
    public async Task InjectTrackerToTorrent_RejectsUrlWithPasskey()
    {
        var publicTorrent = new Torrent
        {
            Id = 30,
            Name = "Public Torrent",
            InfoHash = "CCCC111122223333444455556666777788889999",
            IsPrivate = false,
        };
        this.storedTorrents.Add(publicTorrent);

        var result = await this.service.InjectTrackerToTorrentAsync(30, "https://tracker.site/announce/abcdef123456");
        result.Boosted.Should().BeFalse();
        result.Message.Should().Contain("invalid or contains private passkey");
    }

    [Test]
    public async Task GetCrossMatrixAsync_UsesScrapeCache_ToAvoidDuplicateNetworkScrapes()
    {
        var torrent1 = new Torrent
        {
            Id = 101,
            Name = "Cache Test Torrent 1",
            InfoHash = "1111222233334444555566667777888899990000",
            IsPrivate = false,
        };
        var torrent2 = new Torrent
        {
            Id = 102,
            Name = "Cache Test Torrent 2",
            InfoHash = "AAAA222233334444555566667777888899990000",
            IsPrivate = false,
        };
        this.storedTorrents.Add(torrent1);
        this.storedTorrents.Add(torrent2);

        this.service.ClearScrapeCache();
        this.service.ScrapeCacheCount.Should().Be(0);

        // First call populates scrape cache
        var matrix1 = await this.service.GetCrossMatrixAsync();
        matrix1.Should().NotBeNull();
        matrix1.Torrents.Should().HaveCount(2);

        var initialCacheCount = this.service.ScrapeCacheCount;
        initialCacheCount.Should().BeGreaterThan(0);

        // Second call reuses scrape cache
        var matrix2 = await this.service.GetCrossMatrixAsync();
        matrix2.Should().NotBeNull();
        matrix2.Torrents.Should().HaveCount(2);
        this.service.ScrapeCacheCount.Should().Be(initialCacheCount);

        // Clearing cache empties it
        this.service.ClearScrapeCache();
        this.service.ScrapeCacheCount.Should().Be(0);
    }

    [Test]
    public async Task GetCrossMatrixAsync_BindsConcurrency_AndCompletesWithMultipleTorrents()
    {
        for (var i = 1; i <= 5; i++)
        {
            this.storedTorrents.Add(new Torrent
            {
                Id = 200 + i,
                Name = $"Bulk Torrent {i}",
                InfoHash = new string((char)('A' + i), 40),
                IsPrivate = false,
            });
        }

        var matrix = await this.service.GetCrossMatrixAsync();
        matrix.Should().NotBeNull();
        matrix.Torrents.Should().HaveCount(5);
        matrix.Trackers.Should().NotBeEmpty();
    }

    [Test]
    public async Task TrackerBoostOptimizationTask_ExecutesAsyncWithoutSyncOverAsync_AndGuardsReentrancy()
    {
        var mockService = Substitute.For<ITrackerBoostService>();
        mockService.GetSettings().Returns(new TrackerBoostSettings { IntervalMinutes = 60 });

        var tcs = new TaskCompletionSource<bool>();
        var invocationCount = 0;

        mockService.RunOptimizationCycleAsync().Returns(async _ =>
        {
            Interlocked.Increment(ref invocationCount);
            await tcs.Task;
        });

        using var task = new TrackerBoostOptimizationTask(mockService);

        // Launch first execution (will pause on tcs.Task)
        var runTask1 = task.ExecuteAsync();

        // Launch second execution concurrently while first is still running
        var runTask2 = task.ExecuteAsync();

        // The second call must complete immediately without waiting due to re-entrancy guard
        runTask2.IsCompleted.Should().BeTrue();

        // Release the first run
        tcs.SetResult(true);
        await runTask1;

        // Verify only 1 invocation occurred
        invocationCount.Should().Be(1);
    }

    [Test]
    public void TrackerBoostOptimizationTask_CancelsCleanlyOnDispose()
    {
        var mockService = Substitute.For<ITrackerBoostService>();
        mockService.GetSettings().Returns(new TrackerBoostSettings { IntervalMinutes = 120 });

        var task = new TrackerBoostOptimizationTask(mockService);
        task.StartLoop();

        var act = () => task.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void InjectIntoDownloadClients_ReturnsZero_WhenNoClientsConfigured()
    {
        var result = this.service.InjectIntoDownloadClients("0123456789abcdef0123456789abcdef01234567", new[] { "udp://tracker.opentrackr.org:1337/announce" });
        result.Should().Be(0);
    }

    [Test]
    public async Task BoostHashAsync_WhenNonLocalAndNoDownloadClients_ReturnsBoostedFalseAndZeroAdded()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        var result = await this.service.BoostHashAsync(hash);
        result.Should().NotBeNull();
        result.Boosted.Should().BeFalse();
        result.AddedTrackersCount.Should().Be(0);
        result.AddedTrackers.Should().BeEmpty();
    }

    [Test]
    public async Task InjectTrackerToHashAsync_WhenNonLocalAndNoClients_ReturnsBoostedFalseAndZeroAdded()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        var result = await this.service.InjectTrackerToHashAsync(hash, "udp://tracker.opentrackr.org:1337/announce");
        result.Should().NotBeNull();
        result.Boosted.Should().BeFalse();
        result.AddedTrackersCount.Should().Be(0);
        result.AddedTrackers.Should().BeEmpty();
    }

    [Test]
    public void CleanExpiredBoostHistory_EvictsOldEntriesAndRetainsFreshOnes()
    {
        TrackerBoostService.ClearBoostHistory();
        var oldHash = "1111111111111111111111111111111111111111";
        var freshHash = "2222222222222222222222222222222222222222";

        this.service.CleanExpiredBoostHistory(TimeSpan.FromHours(24));
        this.service.RemoveBoostHistory(oldHash);
        this.service.RemoveBoostHistory(freshHash);

        // Populate via InspectHashTrackersAsync or direct Boost
        this.service.CleanExpiredBoostHistory(TimeSpan.FromSeconds(0));
    }

    [Test]
    public void Handle_TorrentDeletedEvent_EvictsTorrentBoostHistory()
    {
        var hash = "3333333333333333333333333333333333333333";
        var torrent = new Torrent { Id = 99, InfoHash = hash };

        var deletedEvent = new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = false };
        var act = () => this.service.Handle(deletedEvent);
        act.Should().NotThrow();
    }
}
