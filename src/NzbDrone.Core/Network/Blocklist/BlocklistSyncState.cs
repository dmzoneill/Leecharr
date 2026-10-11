// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network.Blocklist;

public class BlocklistSyncState : IBlocklistSyncState
{
    private readonly IConfigService configService;
    private readonly object gate = new();
    private DateTime? nextScheduledSyncUtc;

    public BlocklistSyncState(IConfigService configService)
    {
        this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public DateTime? LastSuccessfulSyncUtc => this.configService.BlocklistLastSuccessfulSyncUtc;

    public DateTime? NextScheduledSyncUtc
    {
        get
        {
            lock (this.gate)
            {
                return this.nextScheduledSyncUtc;
            }
        }
    }

    public void RecordSuccessfulSync(DateTime utcNow)
    {
        var normalized = NormalizeUtc(utcNow);
        var intervalHours = Math.Max(1, this.configService.BlocklistUpdateIntervalHours);
        lock (this.gate)
        {
            this.configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "BlocklistLastSuccessfulSyncUtc", normalized.ToString("o", CultureInfo.InvariantCulture) },
            });
            this.nextScheduledSyncUtc = normalized.AddHours(intervalHours);
        }
    }

    public void ScheduleNextSyncUtc(DateTime utcNow)
    {
        lock (this.gate)
        {
            this.nextScheduledSyncUtc = NormalizeUtc(utcNow);
        }
    }

    public void ClearScheduledSync()
    {
        lock (this.gate)
        {
            this.nextScheduledSyncUtc = null;
        }
    }

    public DateTime? ResolveNextScheduledSyncUtc(int intervalHours, bool autoUpdateEnabled)
    {
        if (!autoUpdateEnabled)
        {
            return null;
        }

        var scheduled = this.NextScheduledSyncUtc;
        if (scheduled.HasValue)
        {
            return scheduled;
        }

        var last = this.LastSuccessfulSyncUtc;
        if (!last.HasValue)
        {
            return null;
        }

        return last.Value.AddHours(Math.Max(1, intervalHours));
    }

    private static DateTime NormalizeUtc(DateTime utcNow)
    {
        return utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
    }
}
