// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Network.Blocklist;

[Flags]
public enum BlocklistCapabilities
{
    None = 0,
    IPv4 = 1 << 0,
    IPv6 = 1 << 1,
    Cidr = 1 << 2,
    P2PDat = 1 << 3,
    LinuxIpSet = 1 << 4,
    LiveAutoRefresh = 1 << 5,
    All = IPv4 | IPv6 | Cidr | P2PDat | LinuxIpSet | LiveAutoRefresh,
}
