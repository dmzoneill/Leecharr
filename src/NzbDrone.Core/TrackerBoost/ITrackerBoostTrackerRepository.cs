// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.TrackerBoost;

public interface ITrackerBoostTrackerRepository : IBasicRepository<TrackerBoostTracker>
{
    TrackerBoostTracker FindByUrl(string url);

    List<TrackerBoostTracker> GetAliveTrackers();

    List<TrackerBoostTracker> GetBySource(TrackerSourceType source);
}
