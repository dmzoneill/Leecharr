// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.GeoIp;

public interface IGeoIpManager
{
    string ActiveProviderId { get; }

    IGeoIpProvider ActiveProvider { get; }

    IEnumerable<IGeoIpProvider> GetProviders();

    IGeoIpProvider GetProvider(string providerId);

    Task<GeoIpHealthResult> ProbeProviderAsync(string providerId);

    Task<bool> SwitchProviderAsync(string providerId);
}
