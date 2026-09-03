// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Authentication;

public class SessionCleanupCommand : Command
{
}

public interface ISessionCleanupTask
{
    Task<int> PruneExpiredSessionsAsync(CancellationToken cancellationToken = default);

    Task ExecuteAsync(CancellationToken cancellationToken = default);

    void StartLoop();
}

public class SessionCleanupTask : ISessionCleanupTask, IHandle<ApplicationStartedEvent>, IExecute<SessionCleanupCommand>, IDisposable
{
    private readonly IUserSessionRepository userSessionRepository;
    private readonly Logger logger;
    private readonly CancellationTokenSource cts = new();
    private Task loopTask;

    public SessionCleanupTask(IUserSessionRepository userSessionRepository)
    {
        this.userSessionRepository = userSessionRepository;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.logger.Info("Starting SessionCleanupTask background loop...");
        this.StartLoop();
    }

    public void Execute(SessionCleanupCommand message)
    {
        this.ExecuteAsync().GetAwaiter().GetResult();
    }

    public void StartLoop()
    {
        if (this.loopTask == null)
        {
            this.loopTask = Task.Run(this.RunCleanupLoopAsync, this.cts.Token);
        }
    }

    public async Task<int> PruneExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (this.userSessionRepository == null)
        {
            return 0;
        }

        return await this.userSessionRepository.PruneExpiredSessionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.cts.Token);
        try
        {
            this.logger.Debug("Purging expired user sessions...");
            var deleted = await this.PruneExpiredSessionsAsync(linkedCts.Token).ConfigureAwait(false);
            if (deleted > 0)
            {
                this.logger.Info("Purged {0} expired user sessions.", deleted);
            }
        }
        catch (OperationCanceledException)
        {
            // Clean cancellation
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error while purging expired user sessions.");
        }
    }

    private async Task RunCleanupLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
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
            this.logger.Error(ex, "Unhandled error in SessionCleanupTask loop.");
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }
}
