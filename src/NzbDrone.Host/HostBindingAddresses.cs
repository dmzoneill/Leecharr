// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Host;

public static class HostBindingAddresses
{
    public static bool ShouldUseListenLocalhost(string bindAddress) =>
        string.Equals(bindAddress?.Trim(), "localhost", StringComparison.OrdinalIgnoreCase);
}
