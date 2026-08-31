namespace NzbDrone.Core.Messaging.Commands;

public interface IExecute<TCommand>
    where TCommand : Command
{
    void Execute(TCommand message);
}
