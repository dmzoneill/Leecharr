// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public interface ITorrentMediaMetadataRepository : IBasicRepository<TorrentMediaMetadata>
{
    TorrentMediaMetadata GetByTorrentId(int torrentId);

    List<TorrentMediaMetadata> GetRelated(TorrentMediaMetadata metadata);

    void DeleteByTorrentId(int torrentId);
}
