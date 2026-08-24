using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Blocklist;

public class BlocklistProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }
    public string NewProvider { get; }
    public int RulesMigrated { get; }

    public BlocklistProviderSwitchedEvent(string previousProvider, string newProvider, int rulesMigrated)
    {
        PreviousProvider = previousProvider;
        NewProvider = newProvider;
        RulesMigrated = rulesMigrated;
    }
}
