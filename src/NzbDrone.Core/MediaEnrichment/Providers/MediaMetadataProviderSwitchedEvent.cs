// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class MediaMetadataProviderSwitchedEvent : IEvent
{
    public string PreviousProvider { get; }

    public string NewProvider { get; }

    public MediaMetadataProviderSwitchedEvent(string previousProvider, string newProvider)
    {
        this.PreviousProvider = previousProvider;
        this.NewProvider = newProvider;
    }
}
