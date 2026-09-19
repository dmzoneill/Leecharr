// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandExecutedEvent : IEvent
{
    public CommandExecutedEvent(CommandModel command)
    {
        this.Command = command;
    }

    public CommandModel Command { get; }
}
