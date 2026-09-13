#nullable enable
using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

public class AutomationEventService :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentSeedGoalReachedEvent>,
    IHandle<TorrentRatioReachedEvent>,
    IHandle<HealthIssueEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<MediaEnrichedEvent>,
    IHandle<ArchiveExtractionCompletedEvent>,
    IHandle<ArchiveExtractionFailedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IHandle<CategoryUpdatedEvent>,
    IHandle<ApplicationStartedEvent>,
    IHandle<TorrentStartedEvent>,
    IHandle<TorrentPausedEvent>,
    IHandle<TorrentStalledEvent>,
    IHandle<TorrentSeedingTimeReachedEvent>,
    IHandle<TorrentHashCheckCompletedEvent>,
    IHandle<TorrentProgressMilestoneEvent>,
    IHandle<SpeedThresholdExceededEvent>,
    IHandle<SpeedThresholdDroppedEvent>,
    IHandle<BandwidthQuotaApproachingEvent>,
    IHandle<PortForwardingFailedEvent>,
    IHandle<PeerBannedEvent>,
    IHandle<TrackerUnreachableEvent>,
    IHandle<TrackerBoostAppliedEvent>,
    IHandle<DiskSpaceLowEvent>,
    IHandle<DiskSpaceCriticalEvent>,
    IHandle<FileMoveFailedEvent>,
    IHandle<MediaInspectionFailedEvent>,
    IHandle<ArrImportCompletedEvent>,
    IHandle<ApplicationUpdatedEvent>,
    IHandle<BackupCompletedEvent>,
    IHandle<BackupFailedEvent>,
    IHandle<TaskFailedEvent>
{
    private readonly IAutomationService _automationService;
    private readonly Logger _logger;

    public AutomationEventService(IAutomationService automationService)
    {
        _automationService = automationService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        DispatchTrigger(AutomationTrigger.TorrentAdded, message.Torrent);
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        DispatchTrigger(AutomationTrigger.TorrentCompleted, message.Torrent);
    }

    public void Handle(TorrentSeedGoalReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        DispatchTrigger(AutomationTrigger.RatioReached, message.Torrent);
    }

    public void Handle(TorrentRatioReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        DispatchTrigger(AutomationTrigger.RatioReached, message.Torrent);
    }

    public void Handle(HealthIssueEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var trigger = message.IsResolved ? AutomationTrigger.HealthRestored : AutomationTrigger.TorrentError;
        DispatchTrigger(trigger, message.Torrent);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        DispatchTrigger(AutomationTrigger.TorrentDeleted, message.Torrent);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        DispatchTrigger(AutomationTrigger.TorrentStatusChanged, message.Torrent);
    }

    public void Handle(MediaEnrichedEvent message)
    {
        if (message?.TorrentId > 0)
        {
            DispatchTrigger(AutomationTrigger.MediaEnriched, null);
        }
    }

    public void Handle(ArchiveExtractionCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.ArchiveExtracted, message.Torrent);
        }
    }

    public void Handle(ArchiveExtractionFailedEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.ExtractionFailed, message.Torrent);
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        DispatchTrigger(AutomationTrigger.VpnDisconnected, null);
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        DispatchTrigger(AutomationTrigger.VpnRestored, null);
    }

    public void Handle(CategoryUpdatedEvent message)
    {
        DispatchTrigger(AutomationTrigger.CategoryChanged, null);
    }

    public void Handle(ApplicationStartedEvent message)
    {
        DispatchTrigger(AutomationTrigger.ApplicationStarted, null);
    }

    public void Handle(TorrentStartedEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.TorrentStarted, message.Torrent);
        }
    }

    public void Handle(TorrentPausedEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.TorrentPaused, message.Torrent);
        }
    }

    public void Handle(TorrentStalledEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.TorrentStalled, message.Torrent);
        }
    }

    public void Handle(TorrentSeedingTimeReachedEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.SeedingTimeReached, message.Torrent);
        }
    }

    public void Handle(TorrentHashCheckCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.HashCheckCompleted, message.Torrent);
        }
    }

    public void Handle(TorrentProgressMilestoneEvent message)
    {
        if (message?.Torrent != null)
        {
            DispatchTrigger(AutomationTrigger.ProgressMilestone, message.Torrent);
        }
    }

    public void Handle(SpeedThresholdExceededEvent message)
    {
        DispatchTrigger(AutomationTrigger.SpeedThresholdExceeded, null);
    }

    public void Handle(SpeedThresholdDroppedEvent message)
    {
        DispatchTrigger(AutomationTrigger.SpeedThresholdDropped, null);
    }

    public void Handle(BandwidthQuotaApproachingEvent message)
    {
        DispatchTrigger(AutomationTrigger.BandwidthQuotaApproaching, null);
    }

    public void Handle(PortForwardingFailedEvent message)
    {
        DispatchTrigger(AutomationTrigger.PortForwardingFailed, null);
    }

    public void Handle(PeerBannedEvent message)
    {
        DispatchTrigger(AutomationTrigger.PeerBanned, null);
    }

    public void Handle(TrackerUnreachableEvent message)
    {
        DispatchTrigger(AutomationTrigger.TrackerUnreachable, message?.Torrent);
    }

    public void Handle(TrackerBoostAppliedEvent message)
    {
        DispatchTrigger(AutomationTrigger.TrackerBoostApplied, message?.Torrent);
    }

    public void Handle(DiskSpaceLowEvent message)
    {
        DispatchTrigger(AutomationTrigger.DiskSpaceLow, null);
    }

    public void Handle(DiskSpaceCriticalEvent message)
    {
        DispatchTrigger(AutomationTrigger.DiskSpaceCritical, null);
    }

    public void Handle(FileMoveFailedEvent message)
    {
        DispatchTrigger(AutomationTrigger.FileMoveFailed, message?.Torrent);
    }

    public void Handle(MediaInspectionFailedEvent message)
    {
        DispatchTrigger(AutomationTrigger.MediaInspectionFailed, message?.Torrent);
    }

    public void Handle(ArrImportCompletedEvent message)
    {
        DispatchTrigger(AutomationTrigger.ArrImportCompleted, message?.Torrent);
    }

    public void Handle(ApplicationUpdatedEvent message)
    {
        DispatchTrigger(AutomationTrigger.ApplicationUpdated, null);
    }

    public void Handle(BackupCompletedEvent message)
    {
        DispatchTrigger(AutomationTrigger.BackupCompleted, null);
    }

    public void Handle(BackupFailedEvent message)
    {
        DispatchTrigger(AutomationTrigger.BackupFailed, null);
    }

    public void Handle(TaskFailedEvent message)
    {
        DispatchTrigger(AutomationTrigger.TaskFailed, null);
    }

    private void DispatchTrigger(AutomationTrigger trigger, Torrent? torrent = null)
    {
        try
        {
            var allScripts = _automationService.GetAll();
            var matchingScripts = allScripts
                .Where(s => s.IsEnabled && s.Trigger == trigger)
                .ToList();

            if (matchingScripts.Count == 0)
            {
                return;
            }

            _logger.Debug("Found {0} automation scripts for trigger {1}", matchingScripts.Count, trigger);

            foreach (var script in matchingScripts)
            {
                if (torrent != null)
                {
                    // Category filter
                    if (script.TargetCategories != null && script.TargetCategories.Count > 0)
                    {
                        if (string.IsNullOrEmpty(torrent.Category) || !script.TargetCategories.Contains(torrent.Category, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    // Tag filter
                    if (script.TargetTagIds != null && script.TargetTagIds.Count > 0)
                    {
                        if (torrent.TagIds == null || !script.TargetTagIds.Any(t => torrent.TagIds.Contains(t)))
                        {
                            continue;
                        }
                    }
                }

                try
                {
                    _automationService.ExecuteScript(script, torrent);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to execute automation script '{0}' on trigger {1}", script.Name, trigger);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error dispatching automation trigger {0}", trigger);
        }
    }
}
