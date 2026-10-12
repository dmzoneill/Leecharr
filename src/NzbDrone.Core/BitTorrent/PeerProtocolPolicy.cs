using System;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public static class PeerProtocolPolicy
{
    public static TimeSpan GetHandshakeTimeout(IConfigService config)
    {
        var seconds = config?.HandshakeTimeoutSeconds ?? 30;
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }
}
