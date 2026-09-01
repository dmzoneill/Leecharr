// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
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

        this.service = new TrackerBoostService(
            this.trackerRepository,
            this.torrentService,
            this.trackerEntryRepository,
            this.indexerRepository,
            this.configService,
            this.downloadEngine);
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
}
