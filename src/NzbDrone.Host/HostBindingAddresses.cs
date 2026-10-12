// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using NzbDrone.Core.BitTorrent.Tracker;

namespace NzbDrone.Host;

public static class HostBindingAddresses
{
    public static bool ShouldUseListenLocalhost(string bindAddress) =>
        string.Equals(bindAddress?.Trim(), "localhost", StringComparison.OrdinalIgnoreCase);

    public static IPAddress ResolveListenAddress(string bindAddress) =>
        TrackerBindAddressHelper.ResolveBindAddress(bindAddress);
}
