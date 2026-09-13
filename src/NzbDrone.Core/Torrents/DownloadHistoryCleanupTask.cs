// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryCleanupCommand : Command
{
}

public interface IDownloadHistoryCleanupTask
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);

    void StartLoop();
}

public class DownloadHistoryCleanupTask : IDownloadHistoryCleanupTask, IHandle<ApplicationStartedEvent>, IExecute<DownloadHistoryCleanupCommand>, IExecuteAsync<DownloadHistoryCleanupCommand>, IDisposable
{
    private readonly IDownloadHistoryService downloadHistoryService;
    private readonly IConfigService configService;
    private readonly Logger logger;
    private readonly CancellationTokenSource cts = new();
    private Task loopTask;

    public DownloadHistoryCleanupTask(IDownloadHistoryService downloadHistoryService, IConfigService configService)
    {
        this.downloadHistoryService = downloadHistoryService;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.logger.Info("Starting DownloadHistoryCleanupTask background loop...");
        this.StartLoop();
    }

    public Task ExecuteAsync(DownloadHistoryCleanupCommand message, CancellationToken cancellationToken = default)
    {
        return this.ExecuteAsync(cancellationToken);
    }

    public void Execute(DownloadHistoryCleanupCommand message)
    {
        this.ExecuteAsync(message).GetAwaiter().GetResult();
    }

    public void StartLoop()
    {
        if (this.loopTask == null)
        {
            this.loopTask = Task.Run(this.RunCleanupLoopAsync, this.cts.Token);
        }
    }

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var retentionDays = this.configService.HistoryRetentionDays;
            if (retentionDays > 0)
            {
                this.logger.Info("Executing download history cleanup task (retention: {0} days)...", retentionDays);
                this.downloadHistoryService.PruneHistory(retentionDays);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to execute download history cleanup task.");
        }

        return Task.CompletedTask;
    }

    private async Task RunCleanupLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);

            while (!this.cts.Token.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(this.cts.Token).ConfigureAwait(false))
            {
                await this.ExecuteAsync(this.cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Clean cancellation
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Unhandled error in DownloadHistoryCleanupTask loop.");
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }
}
