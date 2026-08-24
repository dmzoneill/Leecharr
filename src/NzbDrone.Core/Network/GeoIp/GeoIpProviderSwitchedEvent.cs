using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.GeoIp;

public class GeoIpProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }
    public string NewProvider { get; }

    public GeoIpProviderSwitchedEvent(string previousProvider, string newProvider)
    {
        PreviousProvider = previousProvider;
        NewProvider = newProvider;
    }
}
