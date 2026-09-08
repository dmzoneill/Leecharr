// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Update;

public interface IUpdateCheckService
{
    Task<List<UpdatePackage>> GetAvailableUpdatesAsync(CancellationToken cancellationToken = default);
}
