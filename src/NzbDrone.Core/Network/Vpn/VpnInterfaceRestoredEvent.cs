// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Vpn;

public class VpnInterfaceRestoredEvent : IEvent
{
    public string InterfaceName { get; }

    public VpnInterfaceRestoredEvent(string interfaceName)
    {
        this.InterfaceName = interfaceName;
    }
}
