// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Messaging.Commands;

public interface IExecute<TCommand>
    where TCommand : Command
{
    void Execute(TCommand message);
}

public interface IExecuteAsync<TCommand>
    where TCommand : Command
{
    Task ExecuteAsync(TCommand message, CancellationToken cancellationToken = default);
}
