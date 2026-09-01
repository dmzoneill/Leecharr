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
    private Timer timer;

    public TrackerBoostOptimizationTask(ITrackerBoostService trackerBoostService)
    {
        this.trackerBoostService = trackerBoostService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.logger.Info("Starting TrackerBoost background optimization task...");

        // Initial run shortly after startup
        Task.Run(async () =>
        {
            await Task.Delay(5000);
            try
            {
                await this.trackerBoostService.RunOptimizationCycleAsync();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Initial TrackerBoost optimization cycle encountered an error");
            }
        });

        var settings = this.trackerBoostService.GetSettings();
        var intervalMs = Math.Max(1, settings.IntervalMinutes) * 60 * 1000;

        this.timer = new Timer(
            _ => this.Execute(),
            null,
            TimeSpan.FromMinutes(settings.IntervalMinutes),
            TimeSpan.FromMinutes(settings.IntervalMinutes));
    }

    public void Execute()
    {
        try
        {
            this.trackerBoostService.RunOptimizationCycleAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "TrackerBoost background optimization cycle encountered an issue");
        }
    }

    public void Dispose()
    {
        this.timer?.Dispose();
    }
}
