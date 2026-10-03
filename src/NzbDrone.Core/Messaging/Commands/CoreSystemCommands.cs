// Copyright (c) FeedItOut. All rights reserved.
#nullable enable

namespace NzbDrone.Core.Messaging.Commands;

public class RescanTorrentsCommand : Command
{
    public int? TorrentId { get; set; }

    public bool ForceRecheck { get; set; }
}

public class PurgeCompletedTorrentsCommand : Command
{
    public int RetentionDays { get; set; } = 30;

    public bool RemoveData { get; set; }
}

public class ForceGarbageCollectionCommand : Command
{
    public int Generation { get; set; } = 2;

    public bool CompactLargeObjectHeap { get; set; } = true;
}

public class VacuumDatabaseCommand : Command
{
    public bool AnalyzeAfterVacuum { get; set; } = true;
}

public class UpdateTrackersCommand : Command
{
    public int? TorrentId { get; set; }

    public bool AnnounceImmediately { get; set; } = true;
}

public class TestNotificationEndpointsCommand : Command
{
    public int? NotificationId { get; set; }

    public bool SendTestPayload { get; set; } = true;
}

public class RecheckNetworkInterfacesCommand : Command
{
    public bool ForceRebind { get; set; }
}

public class SyncSpeedLimitsCommand : Command
{
    public bool ForceApplyCurrentSchedule { get; set; } = true;
}

public class CheckDiskSpaceCommand : Command
{
    public long MinimumFreeBytesThreshold { get; set; } = 1073741824L; // 1 GB
}

public class RotateLogFilesCommand : Command
{
    public int RetentionDays { get; set; } = 7;
}

public class ClearDhtCacheCommand : Command
{
    public bool BootstrapNodes { get; set; } = true;
}

public class ClearWebSessionCacheCommand : Command
{
    public bool InvalidateAll { get; set; }
}

public class RebuildSearchIndexCommand : Command
{
    public bool FullReindex { get; set; } = true;
}

public class ExportDiagnosticsBundleCommand : Command
{
    public bool IncludeDatabaseSchema { get; set; } = true;

    public bool AnonymizeSecrets { get; set; } = true;
}

public class RefreshAllMetadataCommand : Command
{
    public int? TorrentId { get; set; }

    public bool OverwriteExisting { get; set; }
}
