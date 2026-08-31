// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractorSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }

    public string NewProvider { get; }

    public ArchiveExtractorSwitchedEvent(string previousProvider, string newProvider)
    {
        this.PreviousProvider = previousProvider;
        this.NewProvider = newProvider;
    }
}
