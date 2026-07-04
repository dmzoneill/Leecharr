using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers;

public interface ITrackerEntryRepository : IBasicRepository<TrackerEntry>
{
    IEnumerable<TrackerEntry> GetByTorrentId(int torrentId);
    void DeleteByTorrentId(int torrentId);
}
