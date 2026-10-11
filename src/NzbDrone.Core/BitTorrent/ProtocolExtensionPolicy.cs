using System;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public static class ProtocolExtensionPolicy
{
    public static bool IsUtMetadataEnabled(IConfigService config)
    {
        return config?.ExtensionUtMetadata ?? true;
    }

    public static bool IsUtPexEnabled(IConfigService config)
    {
        return config != null && config.EnablePex && config.ExtensionUtPex;
    }

    public static bool AllowPeerExchange(IConfigService config, bool isPrivateTorrent)
    {
        return !isPrivateTorrent && IsUtPexEnabled(config);
    }

    public static TimeSpan GetPeerExchangeInterval(IConfigService config)
    {
        var seconds = config?.PexInterval ?? 60;
        return TimeSpan.FromSeconds(Math.Max(10, seconds));
    }

    public static int GetPexMaxPeersPerMessage(IConfigService config)
    {
        return Math.Max(1, config?.PexMaxPeersPerMessage ?? 50);
    }
}
