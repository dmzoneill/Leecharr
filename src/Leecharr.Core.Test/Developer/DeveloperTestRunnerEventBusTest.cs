// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Developer.Testing;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperTestRunnerEventBusTest
{
    private IEventAggregator eventAggregator = null!;
    private DeveloperTestRunner runner = null!;

    [SetUp]
    public void SetUp()
    {
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.runner = new DeveloperTestRunner(eventAggregator: this.eventAggregator);
    }

    [Test]
    public async Task RunTestAsync_EventBus_PublishesSmokeTestEventOnly()
    {
        var result = await this.runner.RunTestAsync("event-bus");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("DeveloperEventBusSmokeTestEvent");

        this.eventAggregator.Received(1).PublishEvent(Arg.Any<DeveloperEventBusSmokeTestEvent>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ApplicationStartedEvent>());
    }
}
