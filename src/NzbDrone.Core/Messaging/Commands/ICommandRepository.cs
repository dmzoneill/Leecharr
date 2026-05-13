using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandRepository : IBasicRepository<CommandModel>
{
    IEnumerable<CommandModel> GetByStatus(CommandStatus status);
    void DeleteOldTerminalCommands(DateTime cutoff);
}
