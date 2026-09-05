// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Blocklist;

public interface IBlocklistUpdateService
{
    Task<int> UpdateRulesAsync(CancellationToken cancellationToken = default);
}
