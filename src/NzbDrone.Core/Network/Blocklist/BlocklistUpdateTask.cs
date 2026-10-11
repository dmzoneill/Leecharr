// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Blocklist;

public class BlocklistUpdateCommand : Command
{
}

public interface IBlocklistUpdateTask
{
    Task<int> ExecuteAsync(CancellationToken cancellationToken = default);

    void StartLoop();
}

public class BlocklistUpdateTask : IBlocklistUpdateTask, IHandle<ApplicationStartedEvent>, IHandle<ConfigSavedEvent>, IExecute<BlocklistUpdateCommand>, IExecuteAsync<BlocklistUpdateCommand>, IDisposable
{
    private readonly IBlocklistUpdateService blocklistUpdateService;
    private readonly IConfigService configService;
    private readonly IBlocklistSyncState syncState;
    private readonly Logger logger;
    private readonly CancellationTokenSource cts = new();
    private Task loopTask;

    public BlocklistUpdateTask(
        IBlocklistUpdateService blocklistUpdateService,
        IConfigService configService,
        IBlocklistSyncState syncState)
    {
        this.blocklistUpdateService = blocklistUpdateService;
        this.configService = configService;
        this.syncState = syncState;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        if (this.configService.BlocklistEnabled)
        {
            this.StartLoop();
        }
    }

    public void Handle(ConfigSavedEvent message)
    {
        if (this.configService.BlocklistEnabled)
        {
            this.StartLoop();
        }
    }

    public Task ExecuteAsync(BlocklistUpdateCommand message, CancellationToken cancellationToken = default)
    {
        if (message != null && message.Trigger == CommandTrigger.Scheduled && !this.configService.BlocklistAutoUpdateEnabled)
        {
            this.logger.Debug("Blocklist auto-update is disabled; skipping scheduled execution.");
            return Task.CompletedTask;
        }

        return this.ExecuteAsync(cancellationToken);
    }

    public void Execute(BlocklistUpdateCommand message)
    {
        this.ExecuteAsync(message).GetAwaiter().GetResult();
    }

    public void StartLoop()
    {
        if (this.loopTask == null)
        {
            this.loopTask = Task.Run(this.RunUpdateLoopAsync, this.cts.Token);
        }
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await this.blocklistUpdateService.UpdateRulesAsync(cancellationToken);
            if (loaded > 0)
            {
                this.syncState.RecordSuccessfulSync(DateTime.UtcNow);
            }

            return loaded;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to execute blocklist update task.");
            return 0;
        }
    }

    private async Task RunUpdateLoopAsync()
    {
        // Initial run on startup
        if (this.configService.BlocklistAutoUpdateEnabled)
        {
            await this.ExecuteAsync(this.cts.Token);
        }

        while (!this.cts.Token.IsCancellationRequested)
        {
            try
            {
                var hours = Math.Max(1, this.configService.BlocklistUpdateIntervalHours);
                if (this.configService.BlocklistAutoUpdateEnabled)
                {
                    this.syncState.ScheduleNextSyncUtc(DateTime.UtcNow.AddHours(hours));
                }
                else
                {
                    this.syncState.ClearScheduledSync();
                }

                await Task.Delay(TimeSpan.FromHours(hours), this.cts.Token);
                if (this.configService.BlocklistAutoUpdateEnabled)
                {
                    await this.ExecuteAsync(this.cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error occurred during scheduled blocklist update loop.");
            }
        }
    }

    private bool disposed;

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.cts.Cancel();
        this.cts.Dispose();
    }
}
