// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandWorker : BackgroundService
{
    public const int DefaultMaxConcurrency = 3;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IManageCommandQueue commandQueue;
    private readonly ICommandExecutor commandExecutor;
    private readonly Logger logger;
    private readonly SemaphoreSlim concurrencySemaphore;
    private readonly ConcurrentDictionary<int, byte> activeCommandIds = new();
    private readonly List<Task> activeTasks = new();

    public CommandWorker(IManageCommandQueue commandQueue, ICommandExecutor commandExecutor)
        : this(commandQueue, commandExecutor, DefaultMaxConcurrency)
    {
    }

    public CommandWorker(IManageCommandQueue commandQueue, ICommandExecutor commandExecutor, int maxConcurrency)
    {
        this.commandQueue = commandQueue;
        this.commandExecutor = commandExecutor;
        this.logger = LogManager.GetCurrentClassLogger();
        this.concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public override void Dispose()
    {
        this.concurrencySemaphore?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.Info("Command worker started");

        try
        {
            this.commandQueue.FailStaleCommands();
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error failing stale running commands on startup");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                this.activeTasks.RemoveAll(t => t.IsCompleted);

                var queuedCommands = this.commandQueue.GetQueued() ?? Enumerable.Empty<CommandModel>();
                foreach (var command in queuedCommands)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (command.Id != 0 && this.activeCommandIds.ContainsKey(command.Id))
                    {
                        continue;
                    }

                    await this.concurrencySemaphore.WaitAsync(stoppingToken);

                    if (command.Id != 0 && !this.activeCommandIds.TryAdd(command.Id, 0))
                    {
                        this.concurrencySemaphore.Release();
                        continue;
                    }

                    var task = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await this.commandExecutor.ExecuteAsync(command, stoppingToken);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                // Expected when CommandWorker is shutting down or cancelled via stoppingToken
                                this.logger.Trace("Command {0} execution cancelled during shutdown", command.Name);
                            }
                            catch (Exception ex)
                            {
                                this.logger.Error(ex, "Error executing command {0}", command.Name);
                            }
                            finally
                            {
                                if (command.Id != 0)
                                {
                                    this.activeCommandIds.TryRemove(command.Id, out _);
                                }

                                this.concurrencySemaphore.Release();
                            }
                        },
                        CancellationToken.None);

                    this.activeTasks.Add(task);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Command worker error");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        try
        {
            await Task.WhenAll(this.activeTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Active tasks cancelled during CommandWorker shutdown");
        }
    }
}
