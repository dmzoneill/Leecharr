// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Jobs;

public interface ITaskManager
{
    IList<ScheduledTask> GetAll();

    ScheduledTask Get(int id);

    ScheduledTask GetByTypeName(string typeName);

    void Update(ScheduledTask task);

    void RecordDispatchStart(ScheduledTask task);

    void EnsureDefaultTasks();
}
