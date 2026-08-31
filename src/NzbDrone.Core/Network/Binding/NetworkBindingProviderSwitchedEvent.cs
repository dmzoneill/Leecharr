// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Binding;

public class NetworkBindingProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }

    public string NewProvider { get; }

    public NetworkBindingProviderSwitchedEvent(string previousProvider, string newProvider)
    {
        this.PreviousProvider = previousProvider;
        this.NewProvider = newProvider;
    }
}
