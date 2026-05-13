namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandExecutor
{
    void Execute(CommandModel command);
}
