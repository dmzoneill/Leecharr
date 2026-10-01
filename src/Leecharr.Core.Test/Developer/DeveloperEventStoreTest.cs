// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.System;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperEventStoreTest
{
    private DeveloperEventStore store = null!;

    [SetUp]
    public void SetUp()
    {
        this.store = new DeveloperEventStore();
    }

    [Test]
    public void RecordEvent_WhenDeveloperEventEntryPassed_RecordsDirectEntryWithoutWrapping()
    {
        var entry = new DeveloperEventEntry
        {
            EventName = "TorrentAddedEvent",
            EventType = "Synthetic.TorrentAddedEvent",
            SourceNamespace = "Leecharr.Developer.Synthetic",
            PayloadJson = "{\"torrentId\":42}",
        };

        this.store.RecordEvent(entry);

        var events = this.store.GetRecentEvents();
        events.Should().HaveCount(1);
        events[0].EventName.Should().Be("TorrentAddedEvent");
        events[0].EventType.Should().Be("Synthetic.TorrentAddedEvent");
        events[0].PayloadJson.Should().Be("{\"torrentId\":42}");
        events[0].SourceNamespace.Should().Be("Leecharr.Developer.Synthetic");
    }

    [Test]
    public void RecordEvent_WhenDeveloperSyntheticEventPassed_IgnoresDuplicateRecording()
    {
        var synthetic = new DeveloperSyntheticEvent("TorrentAddedEvent", "{\"torrentId\":42}");

        this.store.RecordEvent(synthetic);

        var events = this.store.GetRecentEvents();
        events.Should().BeEmpty();
    }
}

[TestFixture]
public class SystemDeveloperEventsTest
{
    private IDeveloperEventStore eventStore = null!;
    private IEventAggregator eventAggregator = null!;
    private SystemDeveloperController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.eventStore = Substitute.For<IDeveloperEventStore>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.controller = new SystemDeveloperController(
            eventStore: this.eventStore,
            eventAggregator: this.eventAggregator);
    }

    [Test]
    public void PublishSyntheticEvent_WhenValidRequest_RecordsCleanEntryAndDispatchesToEventAggregator()
    {
        var request = new DeveloperPublishEventRequest
        {
            EventName = "TorrentAddedEvent",
            PayloadJson = "{\"torrentId\": 42}",
        };

        var result = this.controller.PublishSyntheticEvent(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var item = okResult.Value as DeveloperEventItem;
        item.Should().NotBeNull();
        item!.EventName.Should().Be("TorrentAddedEvent");
        item.EventType.Should().Be("Synthetic.TorrentAddedEvent");
        item.PayloadJson.Should().Be("{\"torrentId\": 42}");

        this.eventStore.Received(1).RecordEvent(Arg.Is<DeveloperEventEntry>(e =>
            e.EventName == "TorrentAddedEvent" &&
            e.EventType == "Synthetic.TorrentAddedEvent" &&
            e.PayloadJson == "{\"torrentId\": 42}"));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<DeveloperSyntheticEvent>(e =>
            e.Name == "TorrentAddedEvent" &&
            e.Payload == "{\"torrentId\": 42}"));
    }

    [Test]
    public void PublishSyntheticEvent_WhenEventNameBlank_ReturnsBadRequest()
    {
        var request = new DeveloperPublishEventRequest
        {
            EventName = "   ",
            PayloadJson = "{}",
        };

        var result = this.controller.PublishSyntheticEvent(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        this.eventStore.DidNotReceiveWithAnyArgs().RecordEvent(default(DeveloperEventEntry)!);
        this.eventAggregator.DidNotReceiveWithAnyArgs().PublishEvent(default(DeveloperSyntheticEvent)!);
    }
}
