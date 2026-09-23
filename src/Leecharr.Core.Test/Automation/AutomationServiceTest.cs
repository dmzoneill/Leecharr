using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Automation;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationServiceTest
{
    private IAutomationScriptRepository _scriptRepository;
    private ITorrentRepository _torrentRepository;
    private ITagRepository _tagRepository;
    private IEventAggregator _eventAggregator;
    private IDownloadEngine _downloadEngine;
    private IBlocklistService _blocklistService;
    private ITrackerBoostService _trackerBoostService;
    private IExtractorService _extractorService;
    private IDiskProvider _diskProvider;
    private ITorrentService _torrentService;
    private IManageCommandQueue _commandQueue;
    private ICustomScriptService _customScriptService;
    private INotificationRepository _notificationRepository;
    private IWebhookDispatcher _webhookDispatcher;

    [SetUp]
    public void SetUp()
    {
        _scriptRepository = Substitute.For<IAutomationScriptRepository>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _tagRepository = Substitute.For<ITagRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _downloadEngine = Substitute.For<IDownloadEngine>();
        _blocklistService = Substitute.For<IBlocklistService>();
        _trackerBoostService = Substitute.For<ITrackerBoostService>();
        _extractorService = Substitute.For<IExtractorService>();
        _diskProvider = Substitute.For<IDiskProvider>();
        _torrentService = Substitute.For<ITorrentService>();
        _commandQueue = Substitute.For<IManageCommandQueue>();
        _customScriptService = Substitute.For<ICustomScriptService>();
        _notificationRepository = Substitute.For<INotificationRepository>();
        _webhookDispatcher = Substitute.For<IWebhookDispatcher>();
    }

    [Test]
    public void GetAll_ShouldReturnAllScriptsFromRepository()
    {
        var scripts = new List<AutomationScript>
        {
            new AutomationScript { Id = 1, Name = "Script 1", IsEnabled = true },
            new AutomationScript { Id = 2, Name = "Script 2", IsEnabled = false },
        };
        _scriptRepository.All().Returns(scripts);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.GetAll();

        result.Should().BeEquivalentTo(scripts);
    }

    [Test]
    public void Get_ShouldReturnScriptById()
    {
        var script = new AutomationScript { Id = 42, Name = "Script 42" };
        _scriptRepository.Get(42).Returns(script);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.Get(42);

        result.Should().BeSameAs(script);
    }

    [Test]
    public void Add_ShouldSetCreatedAt_InsertScript_AndPublishModelCreatedEvent()
    {
        var script = new AutomationScript { Id = 10, Name = "New Script" };
        _scriptRepository.Insert(Arg.Any<AutomationScript>()).Returns(call => (AutomationScript)call[0]);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var added = service.Add(script);

        added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        _scriptRepository.Received(1).Insert(script);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<AutomationScript>>(e => e.Model == script && e.Action == ModelAction.Created));
    }

    [Test]
    public void Update_ShouldUpdateScriptInRepository_AndPublishModelUpdatedEvent()
    {
        var script = new AutomationScript { Id = 11, Name = "Updated Script" };
        _scriptRepository.Update(script).Returns(script);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var updated = service.Update(script);

        _scriptRepository.Received(1).Update(script);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<AutomationScript>>(e => e.Model == script && e.Action == ModelAction.Updated));
    }

    [Test]
    public void Delete_ShouldDeleteFromRepository_AndPublishModelDeletedEvent_WhenScriptExists()
    {
        var script = new AutomationScript { Id = 12, Name = "Script To Delete" };
        _scriptRepository.Get(12).Returns(script);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.Delete(12);

        _scriptRepository.Received(1).Delete(12);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<AutomationScript>>(e => e.Model == script && e.Action == ModelAction.Deleted));
    }

    [Test]
    public void Delete_ShouldDoNothing_WhenScriptDoesNotExist()
    {
        _scriptRepository.Get(99).Returns((AutomationScript)null);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.Delete(99);

        _scriptRepository.DidNotReceive().Delete(Arg.Any<int>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ModelEvent<AutomationScript>>());
    }

    [Test]
    public void ExecuteScript_ById_ReturnsFailure_WhenScriptNotFound()
    {
        _scriptRepository.Get(999).Returns((AutomationScript)null);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(999);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Script with ID 999 not found.");
    }

    [Test]
    public void ExecuteScript_ById_WithoutTorrentId_ExecutesScriptWithNullTorrent()
    {
        var script = new AutomationScript
        {
            Id = 1,
            Name = "No Torrent Script",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "system.log('Running without torrent');",
        };
        _scriptRepository.Get(1).Returns(script);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(1);

        result.Success.Should().BeTrue();
    }

    [Test]
    public void ExecuteScript_ById_WithTorrentId_FetchesTorrentAndExecutes()
    {
        var torrent = new Torrent
        {
            Id = 100,
            Name = "FetchedTorrent",
            Category = "Original",
        };
        var script = new AutomationScript
        {
            Id = 2,
            Name = "Modify Torrent Script",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setCategory('UpdatedCategory');",
        };
        _scriptRepository.Get(2).Returns(script);
        _torrentRepository.Get(100).Returns(torrent);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(2, 100);

        result.Success.Should().BeTrue();
        torrent.Category.Should().Be("UpdatedCategory");
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void ExecuteScript_OnTorrentAdded_ExecutesScriptAndAppliesMutations()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "NewlyAddedTorrent",
            Category = "Unsorted",
        };
        var script = new AutomationScript
        {
            Id = 201,
            Name = "OnTorrentAddedScript",
            Trigger = AutomationTrigger.TorrentAdded,
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setCategory('AutoAdded'); torrent.addTag('NewArrival');",
        };
        _tagRepository.All().Returns(new List<Tag>());
        _tagRepository.Insert(Arg.Any<Tag>()).Returns(new Tag { Id = 101, Label = "NewArrival" });

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.Category.Should().Be("AutoAdded");
        torrent.TagIds.Should().Contain(101);
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Action == ModelAction.Updated));
    }

    [Test]
    public void ExecuteScript_OnTorrentFinished_ExecutesScriptAndAppliesMutations()
    {
        var torrent = new Torrent
        {
            Id = 11,
            Name = "FinishedTorrent",
            ShareLimitAction = "None",
            TargetRatio = 1.0,
        };
        var script = new AutomationScript
        {
            Id = 202,
            Name = "OnTorrentFinishedScript",
            Trigger = AutomationTrigger.TorrentCompleted,
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setShareLimitAction('Pause'); torrent.setRatioLimit(2.0);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.ShareLimitAction.Should().Be("Pause");
        torrent.TargetRatio.Should().Be(2.0);
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void ExecuteScript_OnRatioReached_ExecutesScriptAndAppliesMutations()
    {
        var torrent = new Torrent
        {
            Id = 12,
            Name = "RatioReachedTorrent",
            Status = TorrentStatus.Seeding,
            Progress = 1.0f,
        };
        var script = new AutomationScript
        {
            Id = 203,
            Name = "OnRatioReachedScript",
            Trigger = AutomationTrigger.RatioReached,
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.pause();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: _torrentService);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldPause.Should().BeTrue();
        torrent.Status.Should().Be(TorrentStatus.Paused);
        _torrentService.Received(1).PauseAsync(12);
    }

    [Test]
    public void ExecuteScript_Scheduled_ExecutesScriptWithoutTorrent()
    {
        var script = new AutomationScript
        {
            Id = 204,
            Name = "ScheduledMaintenanceScript",
            Trigger = AutomationTrigger.Scheduled,
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "system.runCommand('PerformScheduledMaintenance');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            commandQueue: _commandQueue);

        var result = service.ExecuteScript(script, torrent: null);

        result.Success.Should().BeTrue();
        _commandQueue.Received(1).PushRaw("PerformScheduledMaintenance", "{}", CommandTrigger.Manual);
    }

    [Test]
    public void ExecuteScript_Manual_ExecutesScriptSuccessfully()
    {
        var torrent = new Torrent
        {
            Id = 13,
            Name = "ManualTorrent",
            Priority = 0,
        };
        var script = new AutomationScript
        {
            Id = 205,
            Name = "ManualScript",
            Trigger = AutomationTrigger.Manual,
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setPriority('high');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.Priority.Should().Be(2);
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void TestScript_WithExplicitTorrentId_UsesRequestedTorrent()
    {
        var torrent = new Torrent
        {
            Id = 15,
            Name = "SpecificTorrent",
            Category = "TestCat",
        };
        _torrentRepository.Get(15).Returns(torrent);

        var script = new AutomationScript
        {
            Name = "Test Validation Script",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('Validated');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.TestScript(script, torrentId: 15);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("Validated");
        _torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
        _scriptRepository.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void TestScript_WithoutTorrentId_UsesFirstTorrentFromRepository_WhenTorrentsExist()
    {
        var firstTorrent = new Torrent
        {
            Id = 16,
            Name = "FirstRepoTorrent",
        };
        _torrentRepository.All().Returns(new List<Torrent> { firstTorrent });

        var script = new AutomationScript
        {
            Name = "Test Validation Script",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('TestedFirst');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.TestScript(script);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("TestedFirst");
        _torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void TestScript_WithoutTorrentId_CreatesSimulatedUbuntuTorrent_WhenRepositoryIsEmpty()
    {
        _torrentRepository.All().Returns(new List<Torrent>());

        var script = new AutomationScript
        {
            Name = "InspectSimulated",
            Language = AutomationLanguage.JavaScript,
            Code = "if (torrent.name.indexOf('Ubuntu') !== -1) { torrent.addTag('FoundSimulated'); }",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.TestScript(script);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("FoundSimulated");
        _scriptRepository.DidNotReceive().Update(Arg.Any<AutomationScript>());
        _torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void ExecuteScript_RecordsSuccessStatsAndOutputLog()
    {
        var torrent = new Torrent { Id = 30, Name = "LogTorrent" };
        var script = new AutomationScript
        {
            Id = 301,
            Name = "SuccessScript",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('LoggedTag');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        script.LastExecutedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        script.LastExecutionStatus.Should().Be("Success");
        script.LastExecutionLog.Should().NotBeNull();
        _scriptRepository.Received(1).Update(script);
    }

    [Test]
    public void ExecuteScript_RecordsFailedStatsAndErrorLog_WhenScriptFails()
    {
        var torrent = new Torrent { Id = 31, Name = "FailTorrent" };
        var script = new AutomationScript
        {
            Id = 302,
            Name = "FailingScript",
            Language = AutomationLanguage.JavaScript,
            Code = "throw new Error('Script failure boom');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeFalse();
        script.LastExecutedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        script.LastExecutionStatus.Should().Be("Failed");
        script.LastExecutionLog.Should().Contain("Script failure boom");
        _scriptRepository.Received(1).Update(script);
    }

    [Test]
    public void ExecuteScript_DoesNotThrow_WhenScriptRepositoryUpdateThrows()
    {
        var torrent = new Torrent { Id = 32, Name = "RepoErrorTorrent" };
        var script = new AutomationScript
        {
            Id = 303,
            Name = "RepoErrorScript",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('NoErrorTag');",
        };
        _scriptRepository.When(r => r.Update(Arg.Any<AutomationScript>())).Do(_ => throw new InvalidOperationException("DB update failed"));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var act = () => service.ExecuteScript(script, torrent);
        act.Should().NotThrow();
    }

    [Test]
    public void ExecuteScript_DoesNotUpdateScriptStats_WhenScriptIdIsZero()
    {
        var torrent = new Torrent { Id = 33, Name = "TransientTorrent" };
        var script = new AutomationScript
        {
            Id = 0,
            Name = "TransientScript",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('Transient');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        _scriptRepository.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void ApplyTorrentMutations_ReusesExistingTag_WhenTagAlreadyExistsInRepository()
    {
        var torrent = new Torrent { Id = 40, Name = "TagTorrent" };
        var script = new AutomationScript
        {
            Id = 401,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('PreExistingTag');",
        };
        _tagRepository.All().Returns(new List<Tag> { new Tag { Id = 88, Label = "PreExistingTag" } });

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        torrent.TagIds.Should().Contain(88);
        _tagRepository.DidNotReceive().Insert(Arg.Any<Tag>());
    }

    [Test]
    public void ApplyTorrentMutations_CreatesNewTag_WhenTagDoesNotExistInRepository()
    {
        var torrent = new Torrent { Id = 41, Name = "NewTagTorrent" };
        var script = new AutomationScript
        {
            Id = 402,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('BrandNewTag');",
        };
        _tagRepository.All().Returns(new List<Tag>());
        _tagRepository.Insert(Arg.Any<Tag>()).Returns(new Tag { Id = 99, Label = "BrandNewTag" });

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        torrent.TagIds.Should().Contain(99);
        _tagRepository.Received(1).Insert(Arg.Is<Tag>(t => t.Label == "BrandNewTag"));
    }

    [Test]
    public void ApplyTorrentMutations_RemovesTag_WhenTagExists()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "RemoveTagTorrent",
            TagIds = new List<int> { 55, 66 },
        };
        var script = new AutomationScript
        {
            Id = 403,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.removeTag('UnwantedTag');",
        };
        _tagRepository.All().Returns(new List<Tag>
        {
            new Tag { Id = 55, Label = "UnwantedTag" },
            new Tag { Id = 66, Label = "KeptTag" },
        });

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        torrent.TagIds.Should().NotContain(55);
        torrent.TagIds.Should().Contain(66);
    }

    [Test]
    public void ApplyTorrentMutations_UpdatesLimitsAndFlags()
    {
        var torrent = new Torrent
        {
            Id = 43,
            Name = "MutateLimitsTorrent",
            UploadLimit = 0,
            DownloadLimit = 0,
            Priority = 0,
            SequentialDownload = false,
            InitialSeeding = false,
            SavePath = "/old/path",
        };
        var script = new AutomationScript
        {
            Id = 404,
            Language = AutomationLanguage.JavaScript,
            Code = @"
                torrent.setUploadLimit(500);
                torrent.setDownloadLimit(1500);
                torrent.setPriority(2);
                torrent.setSequential(true);
                torrent.setSuperSeeding(true);
                torrent.moveFiles('/new/path');
            ",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        torrent.UploadLimit.Should().Be(500);
        torrent.DownloadLimit.Should().Be(1500);
        torrent.Priority.Should().Be(2);
        torrent.SequentialDownload.Should().BeTrue();
        torrent.InitialSeeding.Should().BeTrue();
        torrent.SavePath.Should().Be("/new/path");
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Action == ModelAction.Updated));
    }

    [Test]
    public void ApplyTorrentMutations_SetsTrackerUrl_WhenPreviouslyEmpty()
    {
        var torrent = new Torrent
        {
            Id = 44,
            Name = "TrackerAddTorrent",
            TrackerUrl = null,
        };
        var script = new AutomationScript
        {
            Id = 405,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTracker('https://tracker.new.org/announce');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        torrent.TrackerUrl.Should().Be("https://tracker.new.org/announce");
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void ApplyTorrentMutations_DoesNotOverwriteTrackerUrl_WhenAlreadyPopulated()
    {
        var torrent = new Torrent
        {
            Id = 45,
            Name = "KeepTrackerTorrent",
            TrackerUrl = "https://tracker.existing.org/announce",
        };
        var script = new AutomationScript
        {
            Id = 406,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTracker('https://tracker.second.org/announce');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        torrent.TrackerUrl.Should().Be("https://tracker.existing.org/announce");
        _torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void ApplyTorrentMutations_DoesNotUpdateTorrent_WhenNothingChanged()
    {
        var torrent = new Torrent
        {
            Id = 46,
            Name = "UnchangedTorrent",
            Category = "Fixed",
        };
        var script = new AutomationScript
        {
            Id = 407,
            Language = AutomationLanguage.JavaScript,
            Code = "system.log('Just logging, no mutations');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        service.ExecuteScript(script, torrent);

        _torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void ExecuteScript_PushesSyncArrCommand_WhenArrSyncRequested()
    {
        var torrent = new Torrent { Id = 47, Name = "ArrSyncTorrent" };
        var script = new AutomationScript
        {
            Id = 501,
            Language = AutomationLanguage.JavaScript,
            Code = "system.notifyArr('Radarr', 1);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            commandQueue: _commandQueue);

        service.ExecuteScript(script, torrent);

        _commandQueue.Received(1).PushRaw("SyncArr", "{}", CommandTrigger.Manual);
    }

    [Test]
    public void ExecuteScript_DispatchesWebhookNotification_ToMatchingProvider()
    {
        var torrent = new Torrent { Id = 48, Name = "WebhookTorrent" };
        var script = new AutomationScript
        {
            Id = 502,
            Language = AutomationLanguage.JavaScript,
            Code = "system.sendNotification('AlertTitle', 'AlertMessage', 'Webhook');",
        };

        var activeNotifications = new List<NotificationDefinition>
        {
            new NotificationDefinition
            {
                Name = "MyWebhook",
                Implementation = "Webhook",
                Settings = "{\"url\":\"https://example.com/webhook\"}",
            },
        };
        _notificationRepository.GetEnabled().Returns(activeNotifications);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            notificationRepository: _notificationRepository,
            webhookDispatcher: _webhookDispatcher);

        service.ExecuteScript(script, torrent);

        Thread.Sleep(100);
        _webhookDispatcher.Received(1).DispatchAsync("https://example.com/webhook", Arg.Any<object>(), Arg.Any<IDictionary<string, string>>());
    }

    [Test]
    public void ExecuteScript_ExecutesCustomScriptAsync_WhenScriptsToRunSpecified()
    {
        var torrent = new Torrent { Id = 49, Name = "CustomScriptTorrent" };
        var script = new AutomationScript
        {
            Id = 503,
            Language = AutomationLanguage.JavaScript,
            Code = "system.runScript('/opt/scripts/custom.sh', ['--verbose'], 30);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            customScriptService: _customScriptService);

        service.ExecuteScript(script, torrent);

        Thread.Sleep(100);
        _customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/custom.sh",
            torrent,
            "Automation",
            Arg.Is<List<string>>(args => args.Contains("--verbose")),
            Arg.Any<TimeSpan?>());
    }

    [Test]
    public void CleanUnwantedFiles_WhenSavePathDoesNotExist_ReturnsEarly()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "MissingPathTorrent",
            SavePath = "/non/existent/save/path",
        };
        _diskProvider.FolderExists(torrent.SavePath).Returns(false);
        _diskProvider.FileExists(torrent.SavePath).Returns(false);

        var script = new AutomationScript
        {
            Id = 601,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.cleanUnwantedFiles('*.nfo');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider);

        service.ExecuteScript(script, torrent);

        _diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public void CleanUnwantedFiles_WhenSavePathIsSingleFile_MatchesAndDeletes()
    {
        var torrent = new Torrent
        {
            Id = 51,
            Name = "SingleFileTorrent",
            SavePath = "/downloads/single-file.nfo",
        };
        _diskProvider.FolderExists(torrent.SavePath).Returns(false);
        _diskProvider.FileExists(torrent.SavePath).Returns(true);

        var script = new AutomationScript
        {
            Id = 602,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.cleanUnwantedFiles('*.nfo');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider);

        service.ExecuteScript(script, torrent);

        _diskProvider.Received(1).DeleteFile("/downloads/single-file.nfo");
    }

    [Test]
    public void CleanUnwantedFiles_CatchesFileDeleteException_AndLogsWarning()
    {
        var torrent = new Torrent
        {
            Id = 52,
            Name = "DeleteErrorTorrent",
            SavePath = "/downloads/locked",
        };
        _diskProvider.FolderExists(torrent.SavePath).Returns(true);
        _diskProvider.GetFiles(torrent.SavePath, true).Returns(new[] { "/downloads/locked/file.nfo" });
        _diskProvider.When(d => d.DeleteFile("/downloads/locked/file.nfo")).Do(_ => throw new IOException("Access denied"));

        var script = new AutomationScript
        {
            Id = 603,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.cleanUnwantedFiles('*.nfo');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider);

        var act = () => service.ExecuteScript(script, torrent);
        act.Should().NotThrow();
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_ReturnsFalse_WhenSavePathDoesNotExist()
    {
        var torrent = new Torrent
        {
            Id = 53,
            Name = "NonExistentArchiveTorrent",
            SavePath = "/downloads/nowhere",
        };
        _diskProvider.FolderExists(torrent.SavePath).Returns(false);
        _diskProvider.FileExists(torrent.SavePath).Returns(false);

        var archiveExtractor = Substitute.For<IArchiveExtractorService>();
        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractor);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, null, false);

        result.Should().BeFalse();
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_ReturnsFalse_WhenNoArchivesFound()
    {
        var torrent = new Torrent
        {
            Id = 54,
            Name = "NoArchiveFilesTorrent",
            SavePath = "/downloads/completed",
        };
        _diskProvider.FolderExists(torrent.SavePath).Returns(true);
        _diskProvider.GetFiles(torrent.SavePath, true).Returns(new[] { "/downloads/completed/video.mp4", "/downloads/completed/notes.txt" });

        var archiveExtractor = Substitute.For<IArchiveExtractorService>();
        archiveExtractor.IsArchiveFile(Arg.Any<string>()).Returns(false);

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractor);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, null, false);

        result.Should().BeFalse();
    }

    [Test]
    public void ExecuteScript_CanRunConcurrentlyAcrossMultipleThreads()
    {
        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var tasks = Enumerable.Range(1, 10).Select(i => Task.Run(() =>
        {
            var torrent = new Torrent
            {
                Id = 1000 + i,
                Name = $"ConcurrentTorrent_{i}",
                Category = "Initial",
            };
            var script = new AutomationScript
            {
                Id = 700 + i,
                Name = $"ConcurrentScript_{i}",
                Language = AutomationLanguage.JavaScript,
                Code = $"torrent.setCategory('Category_{i}');",
            };

            var res = service.ExecuteScript(script, torrent);
            res.Success.Should().BeTrue();
            torrent.Category.Should().Be($"Category_{i}");
        })).ToArray();

        var aggregateTask = Task.WhenAll(tasks);
        aggregateTask.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
    }

    [Test]
    public void ExecuteScript_WithCancellationToken_HandlesTaskCancellation()
    {
        using var cts = new CancellationTokenSource();
        var wasCancelled = false;

        var task = Task.Run(() =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        }, cts.Token);

        try
        {
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            wasCancelled = true;
        }

        wasCancelled.Should().BeTrue();
    }

    [Test]
    public void ShouldBanPeer_WhenScriptCallsBanPeer()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "TestTorrent",
            SavePath = "/downloads/TestTorrent",
        };

        var script = new AutomationScript
        {
            Id = 1,
            Name = "BanPeerScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.banPeer('198.51.100.42');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.PeersToBan.Should().Contain("198.51.100.42");
        _blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r.Contains("198.51.100.42")));
    }

    [Test]
    public void ShouldBoostTracker_WhenScriptCallsBoostTracker()
    {
        var torrent = new Torrent
        {
            Id = 20,
            Name = "BoostTorrent",
            SavePath = "/downloads/BoostTorrent",
        };

        var script = new AutomationScript
        {
            Id = 2,
            Name = "BoostScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.boostTracker();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldBoostTracker.Should().BeTrue();
        _trackerBoostService.Received(1).BoostTorrentAsync(20);
    }

    [Test]
    public void ShouldBoostTracker_WhenTrackerBoostServiceNull_FallsBackToDownloadEngine()
    {
        var torrent = new Torrent
        {
            Id = 25,
            Name = "FallbackTorrent",
            SavePath = "/downloads/FallbackTorrent",
        };

        var script = new AutomationScript
        {
            Id = 3,
            Name = "BoostScriptFallback",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.boostTracker();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: null,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldBoostTracker.Should().BeTrue();
        _downloadEngine.Received(1).ForceAnnounceAsync(25);
    }

    [Test]
    public void ShouldExtractArchive_WhenScriptCallsExtractArchive()
    {
        var torrent = new Torrent
        {
            Id = 30,
            Name = "ArchiveTorrent",
            SavePath = "/downloads/ArchiveTorrent",
        };

        var script = new AutomationScript
        {
            Id = 4,
            Name = "ExtractScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.extractArchive('/custom/extracted', true);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldExtractArchive.Should().BeTrue();
        result.ExtractDestination.Should().Be("/custom/extracted");
        result.DeleteArchiveOnExtract.Should().BeTrue();
        _extractorService.Received(1).ExtractAsync(torrent, "/custom/extracted", true);
    }

    [Test]
    public void ShouldCleanUnwantedFiles_WhenScriptCallsCleanUnwantedFiles()
    {
        var torrent = new Torrent
        {
            Id = 40,
            Name = "CleanTorrent",
            SavePath = "/downloads/CleanTorrent",
        };

        var script = new AutomationScript
        {
            Id = 5,
            Name = "CleanScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.cleanUnwantedFiles('*.nfo', '*.txt');",
        };

        _diskProvider.FolderExists(torrent.SavePath).Returns(true);
        _diskProvider.GetFiles(torrent.SavePath, true).Returns(new[]
        {
            "/downloads/CleanTorrent/movie.mkv",
            "/downloads/CleanTorrent/release.nfo",
            "/downloads/CleanTorrent/readme.txt",
        });

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            downloadEngine: _downloadEngine,
            blocklistService: _blocklistService,
            trackerBoostService: _trackerBoostService,
            extractorService: _extractorService,
            diskProvider: _diskProvider);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.CleanFilePatterns.Should().Contain("*.nfo");
        result.CleanFilePatterns.Should().Contain("*.txt");

        _diskProvider.Received(1).DeleteFile("/downloads/CleanTorrent/release.nfo");
        _diskProvider.Received(1).DeleteFile("/downloads/CleanTorrent/readme.txt");
        _diskProvider.DidNotReceive().DeleteFile("/downloads/CleanTorrent/movie.mkv");
    }

    [Test]
    public void ShouldNotThrow_WhenOptionalDependenciesAreNull()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "NullDepTorrent",
            SavePath = "/downloads/NullDepTorrent",
        };

        var script = new AutomationScript
        {
            Id = 6,
            Name = "AllActionsScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = @"
                torrent.banPeer('1.2.3.4');
                torrent.boostTracker();
                torrent.extractArchive('/extracted', false);
                torrent.cleanUnwantedFiles('*.nfo');
            ",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator);

        var act = () => service.ExecuteScript(script, torrent);
        act.Should().NotThrow();
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_ExtractsAndDeletesArchivesAndSiblings_WhenDeleteArchiveIsTrue()
    {
        var torrent = new Torrent
        {
            Id = 100,
            Name = "MultipartTorrent",
            SavePath = "/downloads/MultipartTorrent",
        };

        var archiveExtractorService = Substitute.For<IArchiveExtractorService>();
        archiveExtractorService.IsArchiveFile(Arg.Any<string>()).Returns(call =>
        {
            var p = (string)call[0];
            return p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
        });
        archiveExtractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var files = new[]
        {
            "/downloads/MultipartTorrent/movie.part1.rar",
            "/downloads/MultipartTorrent/movie.part2.rar",
            "/downloads/MultipartTorrent/movie.nfo",
        };

        _diskProvider.FolderExists("/downloads/MultipartTorrent").Returns(true);
        _diskProvider.GetFiles("/downloads/MultipartTorrent", true).Returns(files);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(call => files.Contains((string)call[0]));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractorService);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, "/extracted", true);

        result.Should().BeTrue();
        await archiveExtractorService.Received(1).ExtractArchiveAsync("/downloads/MultipartTorrent/movie.part1.rar", "/extracted");
        await archiveExtractorService.DidNotReceive().ExtractArchiveAsync("/downloads/MultipartTorrent/movie.part2.rar", Arg.Any<string>());

        _diskProvider.Received(1).DeleteFile("/downloads/MultipartTorrent/movie.part1.rar");
        _diskProvider.Received(1).DeleteFile("/downloads/MultipartTorrent/movie.part2.rar");
        _diskProvider.DidNotReceive().DeleteFile("/downloads/MultipartTorrent/movie.nfo");
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_DoesNotDeleteArchives_WhenExtractionFails()
    {
        var torrent = new Torrent
        {
            Id = 101,
            Name = "FailedTorrent",
            SavePath = "/downloads/FailedTorrent",
        };

        var archiveExtractorService = Substitute.For<IArchiveExtractorService>();
        archiveExtractorService.IsArchiveFile(Arg.Any<string>()).Returns(call =>
        {
            var p = (string)call[0];
            return p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
        });
        archiveExtractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var files = new[]
        {
            "/downloads/FailedTorrent/corrupted.part1.rar",
            "/downloads/FailedTorrent/corrupted.part2.rar",
        };

        _diskProvider.FolderExists("/downloads/FailedTorrent").Returns(true);
        _diskProvider.GetFiles("/downloads/FailedTorrent", true).Returns(files);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(call => files.Contains((string)call[0]));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractorService);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, "/extracted", true);

        result.Should().BeFalse();
        _diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public async Task ExtractArchiveWithServiceAsync_DoesNotDeleteArchives_WhenDeleteArchiveIsFalse()
    {
        var torrent = new Torrent
        {
            Id = 102,
            Name = "KeepArchiveTorrent",
            SavePath = "/downloads/KeepArchiveTorrent",
        };

        var archiveExtractorService = Substitute.For<IArchiveExtractorService>();
        archiveExtractorService.IsArchiveFile(Arg.Any<string>()).Returns(call =>
        {
            var p = (string)call[0];
            return p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
        });
        archiveExtractorService.ExtractArchiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var files = new[]
        {
            "/downloads/KeepArchiveTorrent/movie.part1.rar",
            "/downloads/KeepArchiveTorrent/movie.part2.rar",
        };

        _diskProvider.FolderExists("/downloads/KeepArchiveTorrent").Returns(true);
        _diskProvider.GetFiles("/downloads/KeepArchiveTorrent", true).Returns(files);
        _diskProvider.FileExists(Arg.Any<string>()).Returns(call => files.Contains((string)call[0]));

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            diskProvider: _diskProvider,
            archiveExtractorService: archiveExtractorService);

        var result = await service.ExtractArchiveWithServiceAsync(torrent, "/extracted", false);

        result.Should().BeTrue();
        _diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public void ShouldDelegateToTorrentServiceDeleteAsync_WhenScriptRequestsRemoveWithDeleteData()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "RemoveWithDataTorrent",
            Status = TorrentStatus.Downloading,
        };

        var script = new AutomationScript
        {
            Id = 10,
            Name = "RemoveScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.remove(true);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: _torrentService);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldRemove.Should().BeTrue();
        result.DeleteDataOnRemove.Should().BeTrue();
        _torrentService.Received(1).DeleteAsync(50, true);
        _torrentRepository.DidNotReceive().Delete(Arg.Any<int>());
    }

    [Test]
    public void ShouldDelegateToTorrentServiceDeleteAsync_WhenScriptRequestsRemoveWithoutDeletingData()
    {
        var torrent = new Torrent
        {
            Id = 51,
            Name = "RemoveKeepDataTorrent",
            Status = TorrentStatus.Downloading,
        };

        var script = new AutomationScript
        {
            Id = 11,
            Name = "RemoveKeepDataScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.remove(false);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: _torrentService);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldRemove.Should().BeTrue();
        result.DeleteDataOnRemove.Should().BeFalse();
        _torrentService.Received(1).DeleteAsync(51, false);
        _torrentRepository.DidNotReceive().Delete(Arg.Any<int>());
    }

    [Test]
    public void ShouldDelegateToTorrentServicePauseAsync_WhenScriptRequestsPause()
    {
        var torrent = new Torrent
        {
            Id = 52,
            Name = "PauseTorrent",
            Status = TorrentStatus.Downloading,
        };

        var script = new AutomationScript
        {
            Id = 12,
            Name = "PauseScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.pause();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: _torrentService);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldPause.Should().BeTrue();
        torrent.Status.Should().Be(TorrentStatus.Paused);
        _torrentService.Received(1).PauseAsync(52);
    }

    [Test]
    public void ShouldDelegateToTorrentServiceResumeAsync_WhenScriptRequestsResume()
    {
        var torrent = new Torrent
        {
            Id = 53,
            Name = "ResumeTorrent",
            Status = TorrentStatus.Paused,
            Progress = 0.5f,
        };

        var script = new AutomationScript
        {
            Id = 13,
            Name = "ResumeScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.resume();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: _torrentService);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldResume.Should().BeTrue();
        torrent.Status.Should().Be(TorrentStatus.Downloading);
        _torrentService.Received(1).ResumeAsync(53);
    }

    [Test]
    public void ShouldFallbackToTorrentRepository_WhenTorrentServiceIsNull_OnRemove()
    {
        var torrent = new Torrent
        {
            Id = 54,
            Name = "FallbackRemoveTorrent",
            Status = TorrentStatus.Downloading,
        };

        var script = new AutomationScript
        {
            Id = 14,
            Name = "FallbackRemoveScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.remove(true);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldRemove.Should().BeTrue();
        _torrentRepository.Received(1).Delete(54);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDeletedEvent>(e => e.Torrent == torrent && e.DeleteFiles));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Model == torrent && e.Action == ModelAction.Deleted));
    }

    [Test]
    public void ShouldFallbackToTorrentRepository_WhenTorrentServiceIsNull_OnPause()
    {
        var torrent = new Torrent
        {
            Id = 55,
            Name = "FallbackPauseTorrent",
            Status = TorrentStatus.Downloading,
        };

        var script = new AutomationScript
        {
            Id = 15,
            Name = "FallbackPauseScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.pause();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldPause.Should().BeTrue();
        torrent.Status.Should().Be(TorrentStatus.Paused);
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentPausedEvent>(e => e.Torrent == torrent));
    }

    [Test]
    public void ShouldFallbackToTorrentRepository_WhenTorrentServiceIsNull_OnResume()
    {
        var torrent = new Torrent
        {
            Id = 56,
            Name = "FallbackResumeTorrent",
            Status = TorrentStatus.Paused,
            Progress = 0.5f,
        };

        var script = new AutomationScript
        {
            Id = 16,
            Name = "FallbackResumeScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.resume();",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldResume.Should().BeTrue();
        torrent.Status.Should().Be(TorrentStatus.Downloading);
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStartedEvent>(e => e.Torrent == torrent));
    }

    [Test]
    public void ShouldUpdateTargetRatio_WhenScriptSetsRatioLimit()
    {
        var torrent = new Torrent
        {
            Id = 101,
            Name = "RatioTorrent",
            TargetRatio = 1.0,
        };

        var script = new AutomationScript
        {
            Id = 101,
            Name = "RatioScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setRatioLimit(2.5);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.TargetRatio.Should().Be(2.5);
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Model == torrent && e.Action == ModelAction.Updated));
    }

    [Test]
    public void ShouldUpdateTargetSeedTimeMinutes_WhenScriptSetsSeedingTimeLimit()
    {
        var torrent = new Torrent
        {
            Id = 102,
            Name = "SeedTimeTorrent",
            TargetSeedTimeMinutes = 60,
        };

        var script = new AutomationScript
        {
            Id = 102,
            Name = "SeedTimeScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setSeedingTimeLimit(180);",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.TargetSeedTimeMinutes.Should().Be(180);
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Model == torrent && e.Action == ModelAction.Updated));
    }

    [Test]
    public void ShouldUpdateShareLimitAction_WhenScriptSetsShareLimitAction()
    {
        var torrent = new Torrent
        {
            Id = 103,
            Name = "ShareLimitTorrent",
            ShareLimitAction = "Pause",
        };

        var script = new AutomationScript
        {
            Id = 103,
            Name = "ShareLimitScript",
            IsEnabled = true,
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.setShareLimitAction('RemoveWithData');",
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.ShareLimitAction.Should().Be("RemoveWithData");
        _torrentRepository.Received(1).Update(torrent);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Model == torrent && e.Action == ModelAction.Updated));
    }

    [Test]
    public void ShouldUpdateRatioSeedTimeAndShareLimitAction_WhenYamlScriptSetsLimits()
    {
        var torrent = new Torrent
        {
            Id = 104,
            Name = "YamlMutationsTorrent",
            TargetRatio = 1.0,
            TargetSeedTimeMinutes = 60,
            ShareLimitAction = "Pause",
        };

        var yaml = "name: 'Set Limits'\n" +
            "steps:\n" +
            "  - name: 'Update Limits'\n" +
            "    actions:\n" +
            "      - setRatioLimit: 3.0\n" +
            "      - setSeedingTimeLimit: 1440\n" +
            "      - setShareLimitAction: 'SuperSeeding'\n";

        var script = new AutomationScript
        {
            Id = 104,
            Name = "YamlLimitsScript",
            IsEnabled = true,
            Language = AutomationLanguage.Yaml,
            Code = yaml,
        };

        var service = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagRepository,
            _eventAggregator,
            torrentService: null);

        var result = service.ExecuteScript(script, torrent);

        result.Success.Should().BeTrue();
        torrent.TargetRatio.Should().Be(3.0);
        torrent.TargetSeedTimeMinutes.Should().Be(1440);
        torrent.ShareLimitAction.Should().Be("SuperSeeding");
        _torrentRepository.Received(1).Update(torrent);
    }
}
