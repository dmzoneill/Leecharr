// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandExecutor
{
    void Execute(CommandModel command);
}
