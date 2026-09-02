// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Messaging.Commands;

public abstract class Command
{
    public string Name => this.GetType().Name;

    public DateTime QueuedAt { get; set; }

    public CommandTrigger Trigger { get; set; }

    public bool SuppressMessages { get; set; }
}

public enum CommandTrigger
{
    Unspecified = 0,
    Manual = 1,
    Scheduled = 2,
}
