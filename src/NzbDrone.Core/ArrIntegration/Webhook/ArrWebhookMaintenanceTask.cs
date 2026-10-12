// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public class ArrWebhookMaintenanceTask : IHandle<ApplicationStartedEvent>
{
    public static readonly TimeSpan[] DefaultRetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    };

    private readonly IArrConnectionRepository repository;
    private readonly IArrWebhookRegistration webhookRegistration;
    private readonly Logger logger;

    public TimeSpan[] RetryDelays { get; set; } = DefaultRetryDelays;

    public Task LastStartupTask { get; internal set; }

    internal Func<TimeSpan, Task> DelayAsync = Task.Delay;

    public ArrWebhookMaintenanceTask(
        IArrConnectionRepository repository,
        IArrWebhookRegistration webhookRegistration)
    {
        this.repository = repository;
        this.webhookRegistration = webhookRegistration;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        this.LastStartupTask = Task.Run(async () =>
        {
            var failedIds = this.RegisterAllConnections();
            if (failedIds.Count > 0)
            {
                await this.RetryFailedConnectionsAsync(failedIds);
            }
        }, CancellationToken.None);
    }

    public List<int> RegisterAllConnections()
    {
        var connections = this.repository.All()
            .Where(c => c.Enable && (c.WebhookEnabled || c.EnableAutomaticAdd))
            .ToList();

        if (connections.Count == 0)
        {
            return new List<int>();
        }

        this.logger.Info("Arr connection maintenance: checking {0} connection(s)", connections.Count);

        var registered = 0;
        var failed = 0;
        var failedIds = new List<int>();

        foreach (var connection in connections)
        {
            try
            {
                if (this.webhookRegistration.Register(connection))
                {
                    registered++;
                }
                else
                {
                    failed++;
                    failedIds.Add(connection.Id);
                    this.logger.Warn("Arr connection maintenance: failed to register in {0} at {1}", connection.ArrType, connection.Url);
                }
            }
            catch (Exception ex)
            {
                failed++;
                failedIds.Add(connection.Id);
                this.logger.Error(ex, "Arr connection maintenance: error registering in {0}", connection.ArrType);
            }
        }

        this.logger.Info("Arr connection maintenance complete: {0} registered, {1} failed", registered, failed);
        return failedIds;
    }

    private async Task RetryFailedConnectionsAsync(List<int> failedConnectionIds)
    {
        var currentFailed = failedConnectionIds;
        foreach (var delay in this.RetryDelays)
        {
            try
            {
                await this.DelayAsync(delay);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Arr connection maintenance retry delay interrupted");
                break;
            }

            this.logger.Info("Arr connection maintenance retry: re-checking {0} failed connection(s)", currentFailed.Count);

            var nextFailed = new List<int>();
            foreach (var connectionId in currentFailed)
            {
                var connection = this.GetConnectionForRetry(connectionId);
                if (connection == null)
                {
                    continue;
                }

                try
                {
                    if (this.webhookRegistration.Register(connection))
                    {
                        this.logger.Info("Arr connection registered on retry for {0} at {1}", connection.ArrType, connection.Url);
                    }
                    else
                    {
                        nextFailed.Add(connectionId);
                        this.logger.Warn("Arr connection maintenance retry: still failing for {0} at {1}", connection.ArrType, connection.Url);
                    }
                }
                catch (Exception ex)
                {
                    nextFailed.Add(connectionId);
                    this.logger.Error(ex, "Arr connection maintenance retry: error registering in {0}", connection.ArrType);
                }
            }

            currentFailed = nextFailed;
            if (currentFailed.Count == 0)
            {
                this.logger.Info("Arr connection maintenance retry: all connections registered successfully");
                break;
            }
        }
    }

    private ArrConnectionDefinition GetConnectionForRetry(int connectionId)
    {
        var connection = this.repository.Get(connectionId);
        if (connection == null)
        {
            return null;
        }

        if (!connection.Enable || (!connection.WebhookEnabled && !connection.EnableAutomaticAdd))
        {
            return null;
        }

        return connection;
    }
}
