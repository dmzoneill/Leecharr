using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationEventServiceTest
{
    private IAutomationService _automationService;
    private ITorrentRepository _torrentRepository;

    [SetUp]
    public void SetUp()
    {
        _automationService = Substitute.For<IAutomationService>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
    }

    [Test]
    public void ShouldNotBlockPublisherThreadWhenExecutingLongRunningScript()
    {
        var torrent = new Torrent { Id = 1, Name = "TestTorrent" };
        var script = new AutomationScript
        {
            Id = 10,
            Name = "Slow Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };

        var scriptStarted = new ManualResetEventSlim(false);
        var allowScriptToComplete = new ManualResetEventSlim(false);

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        _automationService.When(x => x.ExecuteScript(script, torrent)).Do(_ =>
        {
            scriptStarted.Set();
            allowScriptToComplete.Wait(3000);
        });

        var service = new AutomationEventService(_automationService);

        var stopwatch = Stopwatch.StartNew();
        service.Handle(new TorrentAddedEvent { Torrent = torrent });
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);

        scriptStarted.Wait(1000).Should().BeTrue();
        allowScriptToComplete.Set();
    }

    [Test]
    public async Task ShouldDebounceRapidTriggersWithinCooldownWindow()
    {
        var torrent = new Torrent { Id = 2, Name = "DebounceTorrent" };
        var script = new AutomationScript
        {
            Id = 20,
            Name = "Debounced Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);
        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);
        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);

        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public async Task ShouldDeduplicateSeedGoalReachedAndRatioReachedEvents()
    {
        var torrent = new Torrent { Id = 3, Name = "RatioTorrent" };
        var script = new AutomationScript
        {
            Id = 30,
            Name = "Ratio Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.RatioReached,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        service.Handle(new TorrentSeedGoalReachedEvent(torrent));
        service.Handle(new TorrentRatioReachedEvent(torrent));

        await Task.Delay(200);

        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public async Task ShouldNotRunRatioScriptsOnSeedGoalReachedEvent()
    {
        var torrent = new Torrent { Id = 31, Name = "TimeOnlyTorrent" };
        var script = new AutomationScript
        {
            Id = 31,
            Name = "Ratio Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.RatioReached,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        service.Handle(new TorrentSeedGoalReachedEvent(torrent));

        await Task.Delay(200);

        _automationService.DidNotReceive().ExecuteScript(script, torrent);
    }

    [Test]
    public async Task ShouldDispatchSeedingTimeReachedScripts()
    {
        var torrent = new Torrent { Id = 32, Name = "SeedTimeTorrent" };
        var script = new AutomationScript
        {
            Id = 32,
            Name = "Seed Time Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.SeedingTimeReached,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        service.Handle(new TorrentSeedingTimeReachedEvent(torrent, TimeSpan.FromMinutes(120)));

        await Task.Delay(200);

        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public async Task ShouldAllowExecutionAfterCooldownWindowExpires()
    {
        var torrent = new Torrent { Id = 4, Name = "ExpiringTorrent" };
        var script = new AutomationScript
        {
            Id = 40,
            Name = "Window Test Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromMilliseconds(50));

        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);
        _automationService.Received(1).ExecuteScript(script, torrent);

        await Task.Delay(100);

        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);
        _automationService.Received(2).ExecuteScript(script, torrent);
    }

    [Test]
    public async Task ShouldExecuteIndependentlyForDifferentTorrents()
    {
        var torrent1 = new Torrent { Id = 101, Name = "Torrent1" };
        var torrent2 = new Torrent { Id = 102, Name = "Torrent2" };
        var script = new AutomationScript
        {
            Id = 50,
            Name = "MultiTorrent Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent1);
        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent2);

        _automationService.Received(1).ExecuteScript(script, torrent1);
        _automationService.Received(1).ExecuteScript(script, torrent2);
    }

    [Test]
    public async Task ShouldExecuteIndependentlyForDifferentScripts()
    {
        var torrent = new Torrent { Id = 6, Name = "ScriptTestTorrent" };
        var script1 = new AutomationScript
        {
            Id = 61,
            Name = "Script 1",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };
        var script2 = new AutomationScript
        {
            Id = 62,
            Name = "Script 2",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script1, script2 });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);

        _automationService.Received(1).ExecuteScript(script1, torrent);
        _automationService.Received(1).ExecuteScript(script2, torrent);
    }

    [Test]
    public async Task ShouldFilterByCategoryAndTag()
    {
        var matchingTorrent = new Torrent
        {
            Id = 71,
            Name = "Matching",
            Category = "Movies",
            TagIds = new List<int> { 5 },
        };
        var nonMatchingCategoryTorrent = new Torrent
        {
            Id = 72,
            Name = "Wrong Category",
            Category = "Music",
            TagIds = new List<int> { 5 },
        };
        var nonMatchingTagTorrent = new Torrent
        {
            Id = 73,
            Name = "Wrong Tag",
            Category = "Movies",
            TagIds = new List<int> { 99 },
        };

        var script = new AutomationScript
        {
            Id = 70,
            Name = "Filtered Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
            TargetCategories = new List<string> { "Movies" },
            TargetTagIds = new List<int> { 5 },
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, nonMatchingCategoryTorrent);
        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, nonMatchingTagTorrent);
        await service.DispatchTrigger(AutomationTrigger.TorrentAdded, matchingTorrent);

        _automationService.DidNotReceive().ExecuteScript(script, nonMatchingCategoryTorrent);
        _automationService.DidNotReceive().ExecuteScript(script, nonMatchingTagTorrent);
        _automationService.Received(1).ExecuteScript(script, matchingTorrent);
    }

    [Test]
    public async Task ShouldDispatchTorrentErrorWhenHealthIssueHasTorrentIdOnly()
    {
        var torrent = new Torrent { Id = 90, Name = "TrackerTorrent" };
        var script = new AutomationScript
        {
            Id = 90,
            Name = "Torrent Error Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentError,
        };

        _torrentRepository.Get(90).Returns(torrent);
        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2), _torrentRepository);

        service.Handle(new HealthIssueEvent(90, "Tracker", "Connection refused", isResolved: false));

        await Task.Delay(200);

        _torrentRepository.Received(1).Get(90);
        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public async Task ShouldCatchAndLogExceptionWhenScriptFails()
    {
        var torrent = new Torrent { Id = 8, Name = "FailingScriptTorrent" };
        var failingScript = new AutomationScript
        {
            Id = 81,
            Name = "Failing Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };
        var successScript = new AutomationScript
        {
            Id = 82,
            Name = "Success Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.TorrentAdded,
        };

        _automationService.GetAll().Returns(new List<AutomationScript> { failingScript, successScript });
        _automationService.When(x => x.ExecuteScript(failingScript, torrent)).Do(_ => throw new InvalidOperationException("Crash"));

        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2));

        var act = async () => await service.DispatchTrigger(AutomationTrigger.TorrentAdded, torrent);
        await act.Should().NotThrowAsync();

        _automationService.Received(1).ExecuteScript(successScript, torrent);
    }

    [Test]
    public async Task ShouldLoadTorrentForMediaEnrichedAutomation()
    {
        var torrent = new Torrent { Id = 99, Name = "EnrichedTorrent", Category = "Movies" };
        var script = new AutomationScript
        {
            Id = 91,
            Name = "Media Enriched Script",
            IsEnabled = true,
            Trigger = AutomationTrigger.MediaEnriched,
        };

        _torrentRepository.Get(99).Returns(torrent);
        _automationService.GetAll().Returns(new List<AutomationScript> { script });
        var service = new AutomationEventService(_automationService, TimeSpan.FromSeconds(2), _torrentRepository);

        service.Handle(new MediaEnrichedEvent { TorrentId = 99 });

        await Task.Delay(200);

        _automationService.Received(1).ExecuteScript(script, torrent);
    }
}
