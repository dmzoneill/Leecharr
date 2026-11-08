// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class HttpTrackerServiceTest
{
    [Test]
    public void Dispose_WhenNeverStarted_DoesNotThrow()
    {
        var service = new HttpTrackerService(Substitute.For<IEmbeddedTrackerHttpProcessor>());

        Action act = service.Dispose;

        act.Should().NotThrow();
        service.IsRunning.Should().BeFalse();
    }

    [Test]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var service = new HttpTrackerService(Substitute.For<IEmbeddedTrackerHttpProcessor>());

        service.Dispose();
        Action act = service.Dispose;

        act.Should().NotThrow();
    }

    [Test]
    public async Task StopAsync_AfterDispose_DoesNotThrow()
    {
        var service = new HttpTrackerService(Substitute.For<IEmbeddedTrackerHttpProcessor>());
        service.Dispose();

        Func<Task> act = () => service.StopAsync();

        await act.Should().NotThrowAsync();
    }

    [Test]
    public void Dispose_DisposesCancellationTokenSource()
    {
        var service = new HttpTrackerService(Substitute.For<IEmbeddedTrackerHttpProcessor>());
        var tokenSource = new CancellationTokenSource();
        typeof(HttpTrackerService)
            .GetField("cts", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(service, tokenSource);

        service.Dispose();

        Action act = () => _ = tokenSource.Token;
        act.Should().Throw<ObjectDisposedException>();
    }
}
