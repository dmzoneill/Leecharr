// Copyright (c) FeedItOut. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Update;

public interface IUpdateInstallService
{
    Task PrepareUpdateAsync(UpdatePackage package, CancellationToken cancellationToken = default);

    bool ApplyPendingUpdate();
}
