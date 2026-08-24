using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Ai;

public class AiProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }
    public string NewProvider { get; }

    public AiProviderSwitchedEvent(string previousProvider, string newProvider)
    {
        PreviousProvider = previousProvider;
        NewProvider = newProvider;
    }
}
