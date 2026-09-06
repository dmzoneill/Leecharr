// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.SignalR;

public class PieceMapSignalREventHandler : IHandle<PieceVerifiedEvent>, IDisposable
{
    private readonly IBroadcastSignalRMessage signalRBroadcaster;
    private readonly object syncLock = new();
    private readonly Dictionary<int, HashSet<int>> pendingPieces = new();
    private readonly SemaphoreSlim flushLock = new(1, 1);
    private readonly Timer flushTimer;
    private readonly ManualResetEventSlim flushInFlight = new(true);
    private int flushInFlightCount;
    private bool disposed;

    public PieceMapSignalREventHandler(IBroadcastSignalRMessage signalRBroadcaster, int flushIntervalMs = 250)
    {
        this.signalRBroadcaster = signalRBroadcaster;
        this.flushTimer = new Timer(
            async _ =>
            {
                if (this.disposed)
                {
                    return;
                }

                try
                {
                    if (!await this.flushLock.WaitAsync(0))
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Interlocked.Increment(ref this.flushInFlightCount);
                this.flushInFlight.Reset();
                try
                {
                    if (!this.disposed)
                    {
                        this.Flush();
                    }
                }
                finally
                {
                    try
                    {
                        if (!this.disposed)
                        {
                            this.flushLock.Release();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    if (Interlocked.Decrement(ref this.flushInFlightCount) == 0)
                    {
                        try
                        {
                            this.flushInFlight.Set();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                }
            },
            null,
            flushIntervalMs,
            flushIntervalMs);
    }

    public void Handle(PieceVerifiedEvent message)
    {
        if (message == null || this.signalRBroadcaster == null || !this.signalRBroadcaster.IsConnected)
        {
            return;
        }

        lock (this.syncLock)
        {
            if (!this.pendingPieces.TryGetValue(message.TorrentId, out var set))
            {
                set = new HashSet<int>();
                this.pendingPieces[message.TorrentId] = set;
            }

            set.Add(message.PieceIndex);
        }
    }

    public void Flush()
    {
        if (this.signalRBroadcaster == null || !this.signalRBroadcaster.IsConnected)
        {
            return;
        }

        Dictionary<int, List<int>> batches;
        lock (this.syncLock)
        {
            if (this.pendingPieces.Count == 0)
            {
                return;
            }

            batches = this.pendingPieces.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderBy(x => x).ToList());

            this.pendingPieces.Clear();
        }

        foreach (var kvp in batches)
        {
            if (kvp.Value.Count > 0)
            {
                this.signalRBroadcaster.BroadcastMessage(new SignalRMessage
                {
                    Name = "pieceMapUpdated",
                    Body = new
                    {
                        torrentId = kvp.Key,
                        pieceIndex = kvp.Value.Last(),
                        pieceIndices = kvp.Value,
                        isVerified = true,
                    },
                });
            }
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.flushTimer?.Dispose();

        // Give any in-flight callback a chance to complete before disposing sync primitives
        this.flushInFlight.Wait(TimeSpan.FromSeconds(1));

        this.Flush();

        this.flushLock.Dispose();
        this.flushInFlight.Dispose();
    }
}
