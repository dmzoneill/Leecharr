// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.TrackerBoost;

public class TrackerBoostOptimizationTask : IHandle<ApplicationStartedEvent>, IDisposable
{
    private readonly ITrackerBoostService trackerBoostService;
    private readonly Logger logger;
    private readonly SemaphoreSlim executionLock = new(1, 1);
    private readonly CancellationTokenSource cts = new();
    private Task loopTask;

    public TrackerBoostOptimizationTask(ITrackerBoostService trackerBoostService)
    {
        this.trackerBoostService = trackerBoostService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.logger.Info("Starting TrackerBoost background optimization task...");
        this.StartLoop();
    }

    public void StartLoop()
    {
        if (this.loopTask == null)
        {
            this.loopTask = Task.Run(this.RunOptimizationLoopAsync, this.cts.Token);
        }
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.cts.Token);
        try
        {
            if (!await this.executionLock.WaitAsync(0, linkedCts.Token).ConfigureAwait(false))
            {
                this.logger.Debug("TrackerBoost optimization cycle is already in progress. Skipping overlapping execution.");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await this.trackerBoostService.RunOptimizationCycleAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "TrackerBoost background optimization cycle encountered an issue");
        }
        finally
        {
            this.executionLock.Release();
        }
    }

    public void Execute()
    {
        _ = Task.Run(async () => await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false));
    }

    public void Dispose()
    {
        try
        {
            this.cts.Cancel();
            this.cts.Dispose();
        }
        catch
        {
        }

        this.executionLock.Dispose();
    }

    private async Task RunOptimizationLoopAsync()
    {
        // Initial delay shortly after startup
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), this.cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Run initial cycle
        await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);

        while (!this.cts.IsCancellationRequested)
        {
            var settings = this.trackerBoostService.GetSettings();
            var intervalMinutes = Math.Max(1, settings?.IntervalMinutes ?? 120);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), this.cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);
        }
    }
}
