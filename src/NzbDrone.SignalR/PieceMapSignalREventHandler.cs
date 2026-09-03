// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.SignalR;

public class PieceMapSignalREventHandler : IHandle<PieceVerifiedEvent>, IDisposable
{
    private readonly IBroadcastSignalRMessage signalRBroadcaster;
    private readonly ConcurrentDictionary<int, ConcurrentBag<int>> pendingPieces = new();
    private readonly Timer flushTimer;

    public PieceMapSignalREventHandler(IBroadcastSignalRMessage signalRBroadcaster, int flushIntervalMs = 250)
    {
        this.signalRBroadcaster = signalRBroadcaster;
        this.flushTimer = new Timer(_ => this.Flush(), null, flushIntervalMs, flushIntervalMs);
    }

    public void Handle(PieceVerifiedEvent message)
    {
        if (message == null || this.signalRBroadcaster == null || !this.signalRBroadcaster.IsConnected)
        {
            return;
        }

        var bag = this.pendingPieces.GetOrAdd(message.TorrentId, _ => new ConcurrentBag<int>());
        bag.Add(message.PieceIndex);
    }

    public void Flush()
    {
        if (this.signalRBroadcaster == null || !this.signalRBroadcaster.IsConnected || this.pendingPieces.IsEmpty)
        {
            return;
        }

        foreach (var torrentId in this.pendingPieces.Keys.ToList())
        {
            if (this.pendingPieces.TryRemove(torrentId, out var bag) && !bag.IsEmpty)
            {
                var pieceList = bag.Distinct().OrderBy(x => x).ToList();
                if (pieceList.Count > 0)
                {
                    this.signalRBroadcaster.BroadcastMessage(new SignalRMessage
                    {
                        Name = "pieceMapUpdated",
                        Body = new
                        {
                            torrentId = torrentId,
                            pieceIndex = pieceList.Last(),
                            pieceIndices = pieceList,
                            isVerified = true,
                        },
                    });
                }
            }
        }
    }

    public void Dispose()
    {
        this.flushTimer?.Dispose();
        this.Flush();
    }
}
