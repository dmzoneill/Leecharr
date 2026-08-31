// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Blocklist;

public class BlocklistProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }

    public string NewProvider { get; }

    public int RulesMigrated { get; }

    public BlocklistProviderSwitchedEvent(string previousProvider, string newProvider, int rulesMigrated)
    {
        this.PreviousProvider = previousProvider;
        this.NewProvider = newProvider;
        this.RulesMigrated = rulesMigrated;
    }
}
