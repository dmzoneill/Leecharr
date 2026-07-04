using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Bandwidth;

public interface ISpeedScheduleRepository : IBasicRepository<SpeedSchedule>
{
    IEnumerable<SpeedSchedule> GetEnabled();
}
