// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http.Transport;

public interface IHttpTransportManager
{
    IHttpTransportProvider ActiveProvider { get; }

    string ActiveProviderId { get; }

    IEnumerable<IHttpTransportProvider> GetProviders();

    IHttpTransportProvider GetProvider(string providerId);

    Task<HttpTransportHealthCheckResult> ProbeProviderAsync(string providerId);

    Task<HttpTransportSwitchResult> SwitchProviderAsync(string targetProviderId);
}
