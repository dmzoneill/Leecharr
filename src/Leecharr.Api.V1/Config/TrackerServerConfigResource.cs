// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class TrackerServerConfigResource : RestResource
{
    public bool TrackerServerEnabled { get; set; }

    public bool TrackerHttpEnabled { get; set; }

    public int TrackerHttpPort { get; set; }

    public bool TrackerUdpEnabled { get; set; }

    public int TrackerUdpPort { get; set; }

    public string TrackerBindAddress { get; set; }

    public int TrackerAnnounceInterval { get; set; }

    public int TrackerMaxPeersPerAnnounce { get; set; }

    public bool TrackerEnableScrape { get; set; }

    public bool TrackerPrivateMode { get; set; }

    public bool TrackerLogAnnounces { get; set; }

    public int TrackerRateLimitPerMinute { get; set; }
}

public static class TrackerServerConfigResourceMapper
{
    public static TrackerServerConfigResource ToResource(IConfigService model)
    {
        return new TrackerServerConfigResource
        {
            TrackerServerEnabled = model.TrackerServerEnabled,
            TrackerHttpEnabled = model.TrackerHttpEnabled,
            TrackerHttpPort = model.TrackerHttpPort,
            TrackerUdpEnabled = model.TrackerUdpEnabled,
            TrackerUdpPort = model.TrackerUdpPort,
            TrackerBindAddress = model.TrackerBindAddress,
            TrackerAnnounceInterval = model.TrackerAnnounceInterval,
            TrackerMaxPeersPerAnnounce = model.TrackerMaxPeersPerAnnounce,
            TrackerEnableScrape = model.TrackerEnableScrape,
            TrackerPrivateMode = model.TrackerPrivateMode,
            TrackerLogAnnounces = model.TrackerLogAnnounces,
            TrackerRateLimitPerMinute = model.TrackerRateLimitPerMinute,
        };
    }
}
