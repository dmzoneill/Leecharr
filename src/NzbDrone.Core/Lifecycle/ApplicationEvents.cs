// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Lifecycle;

public class ApplicationStartedEvent : IEvent
{
}

public class ApplicationShutdownRequested : IEvent
{
}

public class ApplicationUpdatedEvent : IEvent
{
    public string PreviousVersion { get; set; }

    public string NewVersion { get; set; }
}
