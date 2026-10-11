// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent.Tracker;

public interface IHttpTrackerService : IDisposable
{
    bool IsRunning { get; }

    int Port { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);
}
