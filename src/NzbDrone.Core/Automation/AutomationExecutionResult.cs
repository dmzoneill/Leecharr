#nullable enable
using System.Collections.Generic;

namespace NzbDrone.Core.Automation;

public class NotificationActionPayload
{
    public string? Provider { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public class ArrSyncPayload
{
    public string? AppType { get; set; }

    public int? InstanceId { get; set; }
}

public class CustomScriptPayload
{
    public string Path { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 60;
}

public class AutomationExecutionResult
{
    public bool Success { get; set; }

    public string? OutputLog { get; set; }

    public string? Error { get; set; }

    public long ExecutionTimeMs { get; set; }

    public List<string> TagsToAdd { get; set; } = new();

    public List<string> TagsToRemove { get; set; } = new();

    public string? NewCategory { get; set; }

    public bool ShouldPause { get; set; }

    public bool ShouldResume { get; set; }

    public bool ShouldRemove { get; set; }

    public bool DeleteDataOnRemove { get; set; }

    public int? NewUploadLimitKbps { get; set; }

    public int? NewDownloadLimitKbps { get; set; }

    public double? NewRatioLimit { get; set; }

    public int? NewSeedingTimeLimitMinutes { get; set; }

    public int? NewPriority { get; set; }

    public bool? NewSequentialDownload { get; set; }

    public bool? NewSuperSeeding { get; set; }

    public string? NewSavePath { get; set; }

    public bool ShouldRecheck { get; set; }

    public bool ShouldReannounce { get; set; }

    public bool ShouldBoostTracker { get; set; }

    public List<string> TrackersToAdd { get; set; } = new();

    public List<string> TrackersToRemove { get; set; } = new();

    public List<string> PeersToBan { get; set; } = new();

    public List<NotificationActionPayload> NotificationsToSend { get; set; } = new();

    public List<ArrSyncPayload> ArrSyncsToSend { get; set; } = new();

    public List<CustomScriptPayload> ScriptsToRun { get; set; } = new();

    public bool ShouldExtractArchive { get; set; }

    public string? ExtractDestination { get; set; }

    public bool DeleteArchiveOnExtract { get; set; }

    public List<string> CleanFilePatterns { get; set; } = new();

    public bool ShouldStopPipeline { get; set; }

    public string? StopReason { get; set; }
}
