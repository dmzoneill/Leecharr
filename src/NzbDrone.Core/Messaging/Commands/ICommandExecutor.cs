// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandExecutor
{
    void Execute(CommandModel command);

    Task ExecuteAsync(CommandModel command, CancellationToken cancellationToken = default);
}
