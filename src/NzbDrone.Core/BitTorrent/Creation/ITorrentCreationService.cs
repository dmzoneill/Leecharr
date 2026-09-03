// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.BitTorrent.Creation;

public interface ITorrentCreationService
{
    Task<TorrentCreationResult> CreateTorrentAsync(TorrentCreationRequest request, CancellationToken cancellationToken = default);
}
