// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.SignalR;

public interface IBroadcastSignalRMessage
{
    bool IsConnected { get; }

    void BroadcastMessage(SignalRMessage message);
}
