// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.Network.Blocklist;

public interface IBlocklistSyncState
{
    DateTime? LastSuccessfulSyncUtc { get; }

    DateTime? NextScheduledSyncUtc { get; }

    void RecordSuccessfulSync(DateTime utcNow);

    void ScheduleNextSyncUtc(DateTime utcNow);

    void ClearScheduledSync();

    DateTime? ResolveNextScheduledSyncUtc(int intervalHours, bool autoUpdateEnabled);
}
