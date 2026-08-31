// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaInspection;

public interface IMediaInspectorManager
{
    IMediaInspectorProvider ActiveProvider { get; }

    string ActiveProviderId { get; }

    IEnumerable<IMediaInspectorProvider> GetProviders();

    IMediaInspectorProvider GetProvider(string providerId);

    Task<MediaInspectorHealthCheckResult> ProbeProviderAsync(string providerId, CancellationToken cancellationToken = default);

    Task<MediaInspectorSwitchResult> SwitchProviderAsync(string targetProviderId, CancellationToken cancellationToken = default);
}
