using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IManageCommandQueue _commandQueue;
    private readonly ICommandExecutor _commandExecutor;
    private readonly Logger _logger;

    public CommandWorker(IManageCommandQueue commandQueue, ICommandExecutor commandExecutor)
    {
        _commandQueue = commandQueue;
        _commandExecutor = commandExecutor;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Command worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var command in _commandQueue.GetQueued())
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _commandExecutor.Execute(command);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Command worker error");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
