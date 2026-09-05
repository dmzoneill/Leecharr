// Copyright (c) PlaceholderCompany. All rights reserved.

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

public class BlocklistUpdateTask : IBlocklistUpdateTask, IHandle<ApplicationStartedEvent>, IExecute<BlocklistUpdateCommand>, IDisposable
{
    private readonly IBlocklistUpdateService blocklistUpdateService;
    private readonly IConfigService configService;
    private readonly Logger logger;
    private readonly CancellationTokenSource cts = new();
    private Task loopTask;

    public BlocklistUpdateTask(IBlocklistUpdateService blocklistUpdateService, IConfigService configService)
    {
        this.blocklistUpdateService = blocklistUpdateService;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        if (this.configService.BlocklistEnabled)
        {
            this.StartLoop();
        }
    }

    public void Execute(BlocklistUpdateCommand message)
    {
        this.ExecuteAsync().GetAwaiter().GetResult();
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
            return await this.blocklistUpdateService.UpdateRulesAsync(cancellationToken);
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
        await this.ExecuteAsync(this.cts.Token);

        while (!this.cts.Token.IsCancellationRequested)
        {
            try
            {
                var hours = Math.Max(1, this.configService.BlocklistUpdateIntervalHours);
                await Task.Delay(TimeSpan.FromHours(hours), this.cts.Token);
                await this.ExecuteAsync(this.cts.Token);
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

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }
}
