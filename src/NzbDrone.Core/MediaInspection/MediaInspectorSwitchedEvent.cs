// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaInspection;

public class MediaInspectorSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }

    public string NewProvider { get; }

    public MediaInspectorSwitchedEvent(string previousProvider, string newProvider)
    {
        this.PreviousProvider = previousProvider;
        this.NewProvider = newProvider;
    }
}
