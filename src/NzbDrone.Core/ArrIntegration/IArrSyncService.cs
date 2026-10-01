// Copyright (c) FeedItOut. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrSyncService
{
    Task<int> SyncAsync(string appType = null, int? instanceId = null, CancellationToken cancellationToken = default);

    int Sync(string appType = null, int? instanceId = null);
}
