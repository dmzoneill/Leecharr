// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

internal static class BindInterfaceConfigHelper
{
    public static string GetEffectiveBindInterface(IConfigService model)
    {
        return !string.IsNullOrWhiteSpace(model?.NetworkInterfaceBinding)
            ? model.NetworkInterfaceBinding
            : model?.BindInterface;
    }

    public static string GetEffectiveBindInterface(string bindInterface, string networkInterfaceBinding)
    {
        return !string.IsNullOrWhiteSpace(networkInterfaceBinding)
            ? networkInterfaceBinding
            : bindInterface ?? string.Empty;
    }
}
