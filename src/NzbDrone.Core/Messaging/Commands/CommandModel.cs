using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandModel : ModelBase
{
    public string Name { get; set; }
    public string Body { get; set; }
    public CommandStatus Status { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Message { get; set; }
    public int Priority { get; set; }
    public int Trigger { get; set; }
}

public enum CommandStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
