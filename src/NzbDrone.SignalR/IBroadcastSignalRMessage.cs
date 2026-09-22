// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.SignalR;

public interface IBroadcastSignalRMessage
{
    bool IsConnected { get; }

    void BroadcastMessage(SignalRMessage message);

    void BroadcastToGroup(string groupName, SignalRMessage message);

    void BroadcastToTorrent(int torrentId, SignalRMessage message);

    void BroadcastToChannel(string channel, SignalRMessage message);
}
