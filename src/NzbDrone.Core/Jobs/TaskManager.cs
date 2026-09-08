// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Jobs;

public class TaskManager : ITaskManager
{
    private readonly IScheduledTaskRepository scheduledTaskRepository;

    public TaskManager(IScheduledTaskRepository scheduledTaskRepository)
    {
        this.scheduledTaskRepository = scheduledTaskRepository;
    }

    public IList<ScheduledTask> GetAll()
    {
        return this.scheduledTaskRepository.All().ToList();
    }

    public ScheduledTask Get(int id)
    {
        return this.scheduledTaskRepository.Get(id);
    }

    public ScheduledTask GetByTypeName(string typeName)
    {
        return this.scheduledTaskRepository.GetByTypeName(typeName);
    }

    public void Update(ScheduledTask task)
    {
        this.scheduledTaskRepository.Update(task);
    }
}
