using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Lifecycle;

public class ApplicationStartedEvent : IEvent
{
}

public class ApplicationShutdownRequested : IEvent
{
}
