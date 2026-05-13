using System.Collections.Generic;

namespace NzbDrone.Core.Messaging.Commands;

public interface IManageCommandQueue
{
    CommandModel Push<TCommand>(TCommand command, CommandTrigger trigger = CommandTrigger.Unspecified)
        where TCommand : Command;

    CommandModel PushRaw(string name, string body, CommandTrigger trigger = CommandTrigger.Manual);

    IEnumerable<CommandModel> GetAll();
    IEnumerable<CommandModel> GetStarted();
    IEnumerable<CommandModel> GetQueued();
}
