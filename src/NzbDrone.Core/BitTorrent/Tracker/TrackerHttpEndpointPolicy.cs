// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent.Tracker;

public static class TrackerHttpEndpointPolicy
{
    public static bool IsHttpTrackerConfigured(IConfigService configService)
    {
        return configService != null
               && configService.TrackerServerEnabled
               && configService.TrackerHttpEnabled;
    }

    public static bool ShouldAcceptOnLocalPort(IConfigService configService, int localPort)
    {
        if (configService == null)
        {
            return true;
        }

        if (!IsHttpTrackerConfigured(configService))
        {
            return false;
        }

        return localPort == configService.TrackerHttpPort;
    }

    public static bool ShouldRunDedicatedListener(IConfigService configService, IConfigFileProvider configFileProvider)
    {
        if (!IsHttpTrackerConfigured(configService) || configFileProvider == null)
        {
            return false;
        }

        return configService.TrackerHttpPort != configFileProvider.Port;
    }
}
