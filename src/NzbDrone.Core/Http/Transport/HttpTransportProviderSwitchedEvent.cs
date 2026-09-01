using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Http.Transport;

public class HttpTransportProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }
    public string NewProvider { get; }

    public HttpTransportProviderSwitchedEvent(string previousProvider, string newProvider)
    {
        PreviousProvider = previousProvider;
        NewProvider = newProvider;
    }
}
