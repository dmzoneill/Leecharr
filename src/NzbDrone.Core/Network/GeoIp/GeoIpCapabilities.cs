// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Network.GeoIp;

[Flags]
public enum GeoIpCapabilities
{
    None = 0,
    Country = 1 << 0,
    City = 1 << 1,
    Asn = 1 << 2,
    Isp = 1 << 3,
    OfflineDatabase = 1 << 4,
    InMemoryCache = 1 << 5,
    All = Country | City | Asn | Isp | OfflineDatabase | InMemoryCache,
}
