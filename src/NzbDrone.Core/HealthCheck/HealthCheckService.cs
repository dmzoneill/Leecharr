// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.HealthCheck;

public interface IHealthCheckService
{
    Task<List<HealthCheckResult>> PerformChecksAsync(CancellationToken cancellationToken = default);
}

public class HealthCheckService : IHealthCheckService, IDisposable
{
    private readonly IEnumerable<IHealthCheck> healthChecks;
    private readonly IEventAggregator eventAggregator;
    private readonly TimeSpan perCheckTimeout;
    private readonly TimeSpan periodicInterval;
    private readonly ConcurrentDictionary<string, HealthCheckResultType> previousStates = new();
    private readonly Logger logger;
    private readonly Timer periodicTimer;
    private bool disposed;

    public HealthCheckService(
        IEnumerable<IHealthCheck> healthChecks,
        IEventAggregator eventAggregator = null)
        : this(healthChecks, eventAggregator, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(15))
    {
    }

    public HealthCheckService(
        IEnumerable<IHealthCheck> healthChecks,
        IEventAggregator eventAggregator,
        TimeSpan perCheckTimeout,
        TimeSpan periodicInterval)
    {
        this.healthChecks = healthChecks ?? Array.Empty<IHealthCheck>();
        this.eventAggregator = eventAggregator;
        this.perCheckTimeout = perCheckTimeout > TimeSpan.Zero ? perCheckTimeout : TimeSpan.FromSeconds(5);
        this.periodicInterval = periodicInterval;
        this.logger = LogManager.GetCurrentClassLogger();

        if (this.periodicInterval > TimeSpan.Zero && this.periodicInterval != Timeout.InfiniteTimeSpan)
        {
            this.periodicTimer = new Timer(this.OnTimerElapsed, null, this.periodicInterval, this.periodicInterval);
        }
    }

    public async Task<List<HealthCheckResult>> PerformChecksAsync(CancellationToken cancellationToken = default)
    {
        if (this.disposed)
        {
            return new List<HealthCheckResult>();
        }

        var tasks = this.healthChecks.Select(check => this.ExecuteCheckWithTimeoutAsync(check, cancellationToken));
        var resultsArray = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new List<HealthCheckResult>(resultsArray);
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                this.periodicTimer?.Dispose();
            }

            this.disposed = true;
        }
    }

    private void OnTimerElapsed(object state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.PerformChecksAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error occurred during periodic health check execution");
            }
        });
    }

    private async Task<HealthCheckResult> ExecuteCheckWithTimeoutAsync(IHealthCheck check, CancellationToken cancellationToken)
    {
        var checkName = check.GetType().Name;
        using var timeoutCts = new CancellationTokenSource(this.perCheckTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var checkTask = check.CheckAsync(linkedCts.Token);
            var timeoutTask = Task.Delay(this.perCheckTimeout, linkedCts.Token);

            var completedTask = await Task.WhenAny(checkTask, timeoutTask).ConfigureAwait(false);
            if (completedTask != checkTask)
            {
                this.logger.Warn("Health check {0} timed out after {1}s", checkName, this.perCheckTimeout.TotalSeconds);
                var timeoutResult = HealthCheckResult.Error(checkName, $"Health check timed out after {this.perCheckTimeout.TotalSeconds:0.#}s");
                this.HandleStateTransition(timeoutResult);
                return timeoutResult;
            }

            var result = await checkTask.ConfigureAwait(false);
            if (result == null)
            {
                result = HealthCheckResult.Ok(checkName);
            }

            if (result.Type != HealthCheckResultType.Ok)
            {
                this.logger.Warn("Health check {0}: {1}", result.Source, result.Message);
            }

            this.HandleStateTransition(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            this.logger.Warn("Health check {0} timed out after {1}s", checkName, this.perCheckTimeout.TotalSeconds);
            var timeoutResult = HealthCheckResult.Error(checkName, $"Health check timed out after {this.perCheckTimeout.TotalSeconds:0.#}s");
            this.HandleStateTransition(timeoutResult);
            return timeoutResult;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Health check {0} threw an unhandled exception", checkName);
            var errorResult = HealthCheckResult.Error(checkName, $"Health check failed with exception: {ex.Message}");
            this.HandleStateTransition(errorResult);
            return errorResult;
        }
    }

    private void HandleStateTransition(HealthCheckResult result)
    {
        if (result == null)
        {
            return;
        }

        var source = !string.IsNullOrWhiteSpace(result.Source) ? result.Source : "System";
        var isCurrentDegraded = result.Type == HealthCheckResultType.Warning || result.Type == HealthCheckResultType.Error;

        var hasPrevious = this.previousStates.TryGetValue(source, out var previousType);
        this.previousStates[source] = result.Type;

        if (!hasPrevious)
        {
            if (isCurrentDegraded)
            {
                this.eventAggregator?.PublishEvent(new HealthIssueEvent(torrent: null, source, result.Message ?? $"{source} health issue detected", isResolved: false));
            }

            return;
        }

        var isPreviousDegraded = previousType == HealthCheckResultType.Warning || previousType == HealthCheckResultType.Error;

        if (!isPreviousDegraded && isCurrentDegraded)
        {
            this.eventAggregator?.PublishEvent(new HealthIssueEvent(torrent: null, source, result.Message ?? $"{source} health issue detected", isResolved: false));
        }
        else if (isPreviousDegraded && !isCurrentDegraded)
        {
            this.eventAggregator?.PublishEvent(new HealthIssueEvent(torrent: null, source, $"{source} health restored", isResolved: true));
        }
    }
}
