// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;

namespace NzbDrone.Core.Indexers;

public class IndexerStatusService : IIndexerStatusService
{
    private readonly ConcurrentDictionary<int, IndexerStatus> statuses = new();
    private readonly Logger logger;

    public IndexerStatusService()
    {
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void RecordSuccess(int indexerId)
    {
        if (this.statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                status.ConsecutiveFailures = 0;
                status.DisabledTill = null;
                status.InitialFailure = null;
                status.LastFailureMessage = null;
                status.LastStatusCode = null;
            }
        }
    }

    public void RecordFailure(int indexerId, int? statusCode = null, string errorMessage = null, Exception ex = null)
    {
        var status = this.statuses.GetOrAdd(indexerId, id => new IndexerStatus { IndexerId = id });
        lock (status)
        {
            var now = DateTime.UtcNow;
            if (!status.InitialFailure.HasValue)
            {
                status.InitialFailure = now;
            }

            status.MostRecentFailure = now;
            status.ConsecutiveFailures++;
            status.LastStatusCode = statusCode;
            status.LastFailureMessage = errorMessage ?? ex?.Message ?? "Indexer operation failed without error details";

            var backoff = this.CalculateBackoff(status.ConsecutiveFailures, statusCode);
            status.DisabledTill = now.Add(backoff);
            this.logger.Warn(
                "Indexer {0} failed ({1}). Consecutive failures: {2}. Disabled until {3}",
                indexerId,
                status.LastFailureMessage,
                status.ConsecutiveFailures,
                status.DisabledTill);
        }
    }

    public bool IsDisabled(int indexerId)
    {
        if (this.statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                if (status.DisabledTill.HasValue)
                {
                    if (status.DisabledTill.Value <= DateTime.UtcNow)
                    {
                        // Automatic recovery: backoff window has elapsed
                        status.DisabledTill = null;
                        return false;
                    }

                    return true;
                }
            }
        }

        return false;
    }

    public IndexerStatus GetStatus(int indexerId)
    {
        if (this.statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                return new IndexerStatus
                {
                    IndexerId = status.IndexerId,
                    InitialFailure = status.InitialFailure,
                    MostRecentFailure = status.MostRecentFailure,
                    DisabledTill = status.DisabledTill,
                    ConsecutiveFailures = status.ConsecutiveFailures,
                    LastFailureMessage = status.LastFailureMessage,
                    LastStatusCode = status.LastStatusCode,
                };
            }
        }

        return new IndexerStatus { IndexerId = indexerId };
    }

    public IReadOnlyDictionary<int, IndexerStatus> GetAllStatuses()
    {
        var dict = new Dictionary<int, IndexerStatus>();
        foreach (var kvp in this.statuses)
        {
            lock (kvp.Value)
            {
                dict[kvp.Key] = new IndexerStatus
                {
                    IndexerId = kvp.Value.IndexerId,
                    InitialFailure = kvp.Value.InitialFailure,
                    MostRecentFailure = kvp.Value.MostRecentFailure,
                    DisabledTill = kvp.Value.DisabledTill,
                    ConsecutiveFailures = kvp.Value.ConsecutiveFailures,
                    LastFailureMessage = kvp.Value.LastFailureMessage,
                    LastStatusCode = kvp.Value.LastStatusCode,
                };
            }
        }

        return dict;
    }

    public void Reset(int indexerId)
    {
        if (this.statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                status.ConsecutiveFailures = 0;
                status.DisabledTill = null;
                status.InitialFailure = null;
                status.LastFailureMessage = null;
                status.LastStatusCode = null;
            }
        }
    }

    public void ResetAll()
    {
        this.statuses.Clear();
    }

    public TimeSpan CalculateBackoff(int consecutiveFailures, int? statusCode = null)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        return consecutiveFailures switch
        {
            1 => TimeSpan.FromMinutes(5),
            2 => TimeSpan.FromMinutes(15),
            3 => TimeSpan.FromMinutes(30),
            4 => TimeSpan.FromHours(1),
            5 => TimeSpan.FromHours(2),
            6 => TimeSpan.FromHours(4),
            7 => TimeSpan.FromHours(8),
            _ => TimeSpan.FromHours(24),
        };
    }
}
