// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.TrackerBoost;

public class TrackerBoostOptimizationTask : IHandle<ApplicationStartedEvent>, IDisposable
{
    private const int DefaultIntervalMinutes = 120;
    private static readonly TimeSpan SettingsReadFailureRetryDelay = TimeSpan.FromMinutes(1);

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
        var existing = this.loopTask;
        if (existing != null && !existing.IsCompleted)
        {
            return;
        }

        this.loopTask = Task.Run(this.RunOptimizationLoopAsync, this.cts.Token);
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
        catch (OperationCanceledException ex)
        {
            this.logger.Trace(ex, "TrackerBoost background optimization task was cancelled");
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
        _ = Task.Run(async () =>
        {
            try
            {
                await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Unhandled exception in TrackerBoost optimization task");
            }
        });
    }

    public void Dispose()
    {
        try
        {
            this.cts.Cancel();
            this.cts.Dispose();
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Error cancelling TrackerBoost token on dispose");
        }

        this.executionLock.Dispose();
    }

    private async Task RunOptimizationLoopAsync()
    {
        try
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), this.cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await this.RunScheduledCycleAsync().ConfigureAwait(false);

            while (!this.cts.IsCancellationRequested)
            {
                try
                {
                    var settings = this.trackerBoostService.GetSettings();
                    var intervalMinutes = Math.Max(1, settings?.IntervalMinutes ?? DefaultIntervalMinutes);

                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), this.cts.Token).ConfigureAwait(false);
                    await this.RunScheduledCycleAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.logger.Warn(
                        ex,
                        "TrackerBoost scheduled optimization loop hit an error; retrying after {0} minute(s)",
                        SettingsReadFailureRetryDelay.TotalMinutes);

                    try
                    {
                        await Task.Delay(SettingsReadFailureRetryDelay, this.cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            this.logger.Trace(ex, "TrackerBoost background optimization loop was cancelled");
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unexpected error in TrackerBoost background optimization loop");
        }
    }

    private async Task RunScheduledCycleAsync()
    {
        try
        {
            await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // ExecuteAsync already logs cycle failures; keep the periodic loop running.
        }
    }
}
