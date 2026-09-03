// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.SignalR;

public class PieceMapSignalREventHandler : IHandle<PieceVerifiedEvent>
{
    private readonly IBroadcastSignalRMessage signalRBroadcaster;

    public PieceMapSignalREventHandler(IBroadcastSignalRMessage signalRBroadcaster)
    {
        this.signalRBroadcaster = signalRBroadcaster;
    }

    public void Handle(PieceVerifiedEvent message)
    {
        if (message == null || this.signalRBroadcaster == null || !this.signalRBroadcaster.IsConnected)
        {
            return;
        }

        this.signalRBroadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "pieceMapUpdated",
            Body = new
            {
                torrentId = message.TorrentId,
                pieceIndex = message.PieceIndex,
                isVerified = true,
            },
        });
    }
}
