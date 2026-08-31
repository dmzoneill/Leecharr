// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IManageCommandQueue commandQueue;
    private readonly ICommandExecutor commandExecutor;
    private readonly Logger logger;

    public CommandWorker(IManageCommandQueue commandQueue, ICommandExecutor commandExecutor)
    {
        this.commandQueue = commandQueue;
        this.commandExecutor = commandExecutor;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.Info("Command worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var command in this.commandQueue.GetQueued())
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    this.commandExecutor.Execute(command);
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Command worker error");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
