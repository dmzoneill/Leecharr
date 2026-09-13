namespace NzbDrone.Core.Automation;

public enum AutomationTrigger
{
    TorrentAdded = 0,
    TorrentCompleted = 1,
    RatioReached = 2,
    TorrentError = 3,
    Manual = 4,
    Scheduled = 5,
    TorrentDeleted = 6,
    TorrentStatusChanged = 7,
    MediaEnriched = 8,
    ArchiveExtracted = 9,
    ExtractionFailed = 10,
    VpnDisconnected = 11,
    VpnRestored = 12,
    HealthRestored = 13,
    CategoryChanged = 14,
    ApplicationStarted = 15,
}
