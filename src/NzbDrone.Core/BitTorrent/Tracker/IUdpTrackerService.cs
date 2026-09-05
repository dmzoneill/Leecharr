// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent.Tracker;

public interface IUdpTrackerService : IDisposable
{
    bool IsRunning { get; }

    int Port { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    byte[] HandlePacket(ReadOnlySpan<byte> packet, IPEndPoint remoteEndPoint);
}
