using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public interface ITorrentMediaMetadataRepository : IBasicRepository<TorrentMediaMetadata>
{
    TorrentMediaMetadata GetByTorrentId(int torrentId);
    void DeleteByTorrentId(int torrentId);
}
