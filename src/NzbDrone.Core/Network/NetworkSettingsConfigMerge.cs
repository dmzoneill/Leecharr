// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network;

internal static class NetworkSettingsConfigMerge
{
    public static void ApplyConfig(IConfigService configService, NetworkSettings settings)
    {
        if (configService == null || settings == null)
        {
            return;
        }

        settings.EnableVpnKillSwitch = configService.EnableVpnKillSwitch;

        if (settings.BindInterface != null)
        {
            settings.BindInterface = settings.BindInterface.Trim();
            return;
        }

        if (!string.IsNullOrWhiteSpace(configService.BindInterface))
        {
            settings.BindInterface = configService.BindInterface.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(configService.NetworkInterfaceBinding))
        {
            settings.BindInterface = configService.NetworkInterfaceBinding.Trim();
        }
    }
}
