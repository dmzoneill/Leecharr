// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentAddedEvent : IEvent
{
    public Torrent Torrent { get; set; }
}

public class TorrentUpdatedEvent : IEvent
{
    public Torrent Torrent { get; set; }
}

public class TorrentDeletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public bool DeleteFiles { get; set; }
}

public class TorrentStatusChangedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentStatus OldStatus { get; set; }

    public TorrentStatus NewStatus { get; set; }

    public bool IsQueueManagerInternal { get; set; }
}

public class TorrentDownloadCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentDownloadCompletedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentSeedGoalReachedEvent : IEvent
{
    public Torrent Torrent { get; }

    public TorrentSeedGoalReachedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentRatioReachedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public double Ratio { get; set; }

    public TorrentRatioReachedEvent()
    {
    }

    public TorrentRatioReachedEvent(Torrent torrent, double ratio = 0)
    {
        this.Torrent = torrent;
        this.Ratio = ratio > 0 ? ratio : torrent?.Ratio ?? 0;
    }
}

public class HealthIssueEvent : IEvent
{
    public Torrent Torrent { get; }

    public int TorrentId { get; }

    public string Source { get; }

    public string Message { get; }

    public bool IsResolved { get; }

    public HealthIssueEvent(Torrent torrent, string source, string message, bool isResolved = false)
    {
        this.Torrent = torrent;
        this.TorrentId = torrent?.Id ?? 0;
        this.Source = source;
        this.Message = message;
        this.IsResolved = isResolved;
    }

    public HealthIssueEvent(int torrentId, string source, string message, bool isResolved = false)
    {
        this.TorrentId = torrentId;
        this.Source = source;
        this.Message = message;
        this.IsResolved = isResolved;
    }
}

public class TorrentStartedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentStartedEvent()
    {
    }

    public TorrentStartedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentPausedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentPausedEvent()
    {
    }

    public TorrentPausedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentStalledEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public int StalledMinutes { get; set; }

    public TorrentStalledEvent()
    {
    }

    public TorrentStalledEvent(Torrent torrent, int stalledMinutes = 0)
    {
        this.Torrent = torrent;
        this.StalledMinutes = stalledMinutes;
    }
}

public class TorrentSeedingTimeReachedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public System.TimeSpan SeedingTime { get; set; }

    public TorrentSeedingTimeReachedEvent()
    {
    }

    public TorrentSeedingTimeReachedEvent(Torrent torrent, System.TimeSpan seedingTime)
    {
        this.Torrent = torrent;
        this.SeedingTime = seedingTime;
    }
}

public class TorrentHashCheckCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public bool IsSuccessful { get; set; }

    public TorrentHashCheckCompletedEvent()
    {
    }

    public TorrentHashCheckCompletedEvent(Torrent torrent, bool isSuccessful = true)
    {
        this.Torrent = torrent;
        this.IsSuccessful = isSuccessful;
    }
}

public class TorrentProgressMilestoneEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public double MilestonePercent { get; set; }

    public TorrentProgressMilestoneEvent()
    {
    }

    public TorrentProgressMilestoneEvent(Torrent torrent, double milestonePercent)
    {
        this.Torrent = torrent;
        this.MilestonePercent = milestonePercent;
    }
}

public class SpeedThresholdExceededEvent : IEvent
{
    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public int ActiveTorrents { get; set; }

    public SpeedThresholdExceededEvent()
    {
    }

    public SpeedThresholdExceededEvent(long downloadSpeed, long uploadSpeed, int activeTorrents)
    {
        this.DownloadSpeed = downloadSpeed;
        this.UploadSpeed = uploadSpeed;
        this.ActiveTorrents = activeTorrents;
    }
}

public class SpeedThresholdDroppedEvent : IEvent
{
    public long CurrentSpeed { get; set; }

    public long ExpectedMinimumSpeed { get; set; }

    public int ActiveTorrents { get; set; }

    public SpeedThresholdDroppedEvent()
    {
    }

    public SpeedThresholdDroppedEvent(long currentSpeed, long expectedMinimumSpeed, int activeTorrents)
    {
        this.CurrentSpeed = currentSpeed;
        this.ExpectedMinimumSpeed = expectedMinimumSpeed;
        this.ActiveTorrents = activeTorrents;
    }
}

public class BandwidthQuotaApproachingEvent : IEvent
{
    public long BytesUsed { get; set; }

    public long QuotaLimitBytes { get; set; }

    public double PercentageUsed { get; set; }

    public BandwidthQuotaApproachingEvent()
    {
    }

    public BandwidthQuotaApproachingEvent(long bytesUsed, long quotaLimitBytes, double percentageUsed)
    {
        this.BytesUsed = bytesUsed;
        this.QuotaLimitBytes = quotaLimitBytes;
        this.PercentageUsed = percentageUsed;
    }
}

public class PortForwardingFailedEvent : IEvent
{
    public int Port { get; set; }

    public string Protocol { get; set; }

    public string ErrorMessage { get; set; }

    public PortForwardingFailedEvent()
    {
    }

    public PortForwardingFailedEvent(int port, string protocol, string errorMessage)
    {
        this.Port = port;
        this.Protocol = protocol;
        this.ErrorMessage = errorMessage;
    }
}

public class PeerBannedEvent : IEvent
{
    public string PeerIp { get; set; }

    public string Reason { get; set; }

    public string InfoHash { get; set; }

    public PeerBannedEvent()
    {
    }

    public PeerBannedEvent(string peerIp, string reason, string infoHash = "")
    {
        this.PeerIp = peerIp;
        this.Reason = reason;
        this.InfoHash = infoHash;
    }
}

public class TrackerUnreachableEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string TrackerUrl { get; set; }

    public string ErrorMessage { get; set; }

    public TrackerUnreachableEvent()
    {
    }

    public TrackerUnreachableEvent(Torrent torrent, string trackerUrl, string errorMessage)
    {
        this.Torrent = torrent;
        this.TrackerUrl = trackerUrl;
        this.ErrorMessage = errorMessage;
    }
}

public class TrackerBoostAppliedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public int AddedTrackersCount { get; set; }

    public TrackerBoostAppliedEvent()
    {
    }

    public TrackerBoostAppliedEvent(Torrent torrent, int addedTrackersCount)
    {
        this.Torrent = torrent;
        this.AddedTrackersCount = addedTrackersCount;
    }
}

public class DiskSpaceLowEvent : IEvent
{
    public string DrivePath { get; set; }

    public long FreeBytes { get; set; }

    public long TotalBytes { get; set; }

    public double FreePercentage { get; set; }

    public DiskSpaceLowEvent()
    {
    }

    public DiskSpaceLowEvent(string drivePath, long freeBytes, long totalBytes, double freePercentage)
    {
        this.DrivePath = drivePath;
        this.FreeBytes = freeBytes;
        this.TotalBytes = totalBytes;
        this.FreePercentage = freePercentage;
    }
}

public class DiskSpaceCriticalEvent : IEvent
{
    public string DrivePath { get; set; }

    public long FreeBytes { get; set; }

    public DiskSpaceCriticalEvent()
    {
    }

    public DiskSpaceCriticalEvent(string drivePath, long freeBytes)
    {
        this.DrivePath = drivePath;
        this.FreeBytes = freeBytes;
    }
}

public class FileMoveFailedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string SourcePath { get; set; }

    public string DestinationPath { get; set; }

    public string ErrorMessage { get; set; }

    public FileMoveFailedEvent()
    {
    }

    public FileMoveFailedEvent(Torrent torrent, string sourcePath, string destinationPath, string errorMessage)
    {
        this.Torrent = torrent;
        this.SourcePath = sourcePath;
        this.DestinationPath = destinationPath;
        this.ErrorMessage = errorMessage;
    }
}

public class MediaInspectionFailedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string FilePath { get; set; }

    public string Reason { get; set; }

    public MediaInspectionFailedEvent()
    {
    }

    public MediaInspectionFailedEvent(Torrent torrent, string filePath, string reason)
    {
        this.Torrent = torrent;
        this.FilePath = filePath;
        this.Reason = reason;
    }
}

public class ArrImportCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string ArrInstance { get; set; }

    public int ImportedFilesCount { get; set; }

    public ArrImportCompletedEvent()
    {
    }

    public ArrImportCompletedEvent(Torrent torrent, string arrInstance, int importedFilesCount)
    {
        this.Torrent = torrent;
        this.ArrInstance = arrInstance;
        this.ImportedFilesCount = importedFilesCount;
    }
}

public class BackupCompletedEvent : IEvent
{
    public string BackupFileName { get; set; }

    public long SizeBytes { get; set; }

    public long DurationMs { get; set; }

    public BackupCompletedEvent()
    {
    }

    public BackupCompletedEvent(string backupFileName, long sizeBytes, long durationMs)
    {
        this.BackupFileName = backupFileName;
        this.SizeBytes = sizeBytes;
        this.DurationMs = durationMs;
    }
}

public class BackupFailedEvent : IEvent
{
    public string BackupType { get; set; }

    public string ErrorMessage { get; set; }

    public BackupFailedEvent()
    {
    }

    public BackupFailedEvent(string backupType, string errorMessage)
    {
        this.BackupType = backupType;
        this.ErrorMessage = errorMessage;
    }
}

public class TaskFailedEvent : IEvent
{
    public string TaskName { get; set; }

    public string ErrorMessage { get; set; }

    public TaskFailedEvent()
    {
    }

    public TaskFailedEvent(string taskName, string errorMessage)
    {
        this.TaskName = taskName;
        this.ErrorMessage = errorMessage;
    }
}
