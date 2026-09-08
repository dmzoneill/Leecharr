// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.SignalR;

public class PieceMapSignalREventHandler : IHandle<PieceVerifiedEvent>, IDisposable
{
    private readonly IBroadcastSignalRMessage signalRBroadcaster;
    private readonly Logger logger;
    private readonly object syncLock = new();
    private readonly Dictionary<int, HashSet<int>> pendingPieces = new();
    private readonly SemaphoreSlim flushLock = new(1, 1);
    private readonly Timer flushTimer;
    private bool disposed;

    public PieceMapSignalREventHandler(
        IBroadcastSignalRMessage signalRBroadcaster,
        Logger logger = null,
        int flushIntervalMs = 250)
    {
        this.signalRBroadcaster = signalRBroadcaster;
        this.logger = logger ?? LogManager.GetCurrentClassLogger();
        this.flushTimer = new Timer(
            _ =>
            {
                if (this.disposed)
                {
                    return;
                }

                try
                {
                    if (!this.flushLock.Wait(0))
                    {
                        return;
                    }

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
                            this.flushLock.Release();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Error occurred during periodic piece map SignalR flush");
                }
            },
            null,
            flushIntervalMs,
            flushIntervalMs);
    }

    public void Handle(PieceVerifiedEvent message)
    {
        if (this.disposed || message == null || this.signalRBroadcaster == null || !this.signalRBroadcaster.IsConnected)
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
        if (this.signalRBroadcaster == null)
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

            if (!this.signalRBroadcaster.IsConnected)
            {
                this.pendingPieces.Clear();
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
                var ranges = CompressToRanges(kvp.Value);
                this.signalRBroadcaster.BroadcastMessage(new SignalRMessage
                {
                    Name = "pieceMapUpdated",
                    Body = new
                    {
                        torrentId = kvp.Key,
                        pieceIndex = kvp.Value.Last(),
                        ranges = ranges,
                        isVerified = true,
                    },
                });
            }
        }
    }

    public static List<int[]> CompressToRanges(IEnumerable<int> sortedIndices)
    {
        var ranges = new List<int[]>();
        if (sortedIndices == null)
        {
            return ranges;
        }

        using var enumerator = sortedIndices.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return ranges;
        }

        int start = enumerator.Current;
        int end = start;

        while (enumerator.MoveNext())
        {
            int current = enumerator.Current;
            if (current == end + 1)
            {
                end = current;
            }
            else if (current > end + 1)
            {
                ranges.Add(new[] { start, end });
                start = current;
                end = current;
            }
        }

        ranges.Add(new[] { start, end });
        return ranges;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.flushTimer?.Dispose();

        try
        {
            this.flushLock.Wait(TimeSpan.FromSeconds(2));
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            this.Flush();
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error occurred during final piece map SignalR flush on dispose");
        }

        try
        {
            this.flushLock.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
