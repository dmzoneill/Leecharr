#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly TimeSpan _cooldownWindow;
    private readonly ConcurrentDictionary<(int ScriptId, int? TorrentId, AutomationTrigger Trigger), DateTime> _lastExecutionTime = new();

    public AutomationEventService(IAutomationService automationService)
        : this(automationService, null)
    {
    }

    public AutomationEventService(IAutomationService automationService, TimeSpan? cooldownWindow)
    {
        _automationService = automationService;
        _logger = LogManager.GetCurrentClassLogger();
        _cooldownWindow = cooldownWindow ?? TimeSpan.FromSeconds(2);
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        _ = DispatchTrigger(AutomationTrigger.TorrentAdded, message.Torrent);
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        _ = DispatchTrigger(AutomationTrigger.TorrentCompleted, message.Torrent);
    }

    public void Handle(TorrentSeedGoalReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        _ = DispatchTrigger(AutomationTrigger.RatioReached, message.Torrent);
    }

    public void Handle(TorrentRatioReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        _ = DispatchTrigger(AutomationTrigger.RatioReached, message.Torrent);
    }

    public void Handle(HealthIssueEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var trigger = message.IsResolved ? AutomationTrigger.HealthRestored : AutomationTrigger.TorrentError;
        _ = DispatchTrigger(trigger, message.Torrent);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        _ = DispatchTrigger(AutomationTrigger.TorrentDeleted, message.Torrent);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        _ = DispatchTrigger(AutomationTrigger.TorrentStatusChanged, message.Torrent);
    }

    public void Handle(MediaEnrichedEvent message)
    {
        if (message?.TorrentId > 0)
        {
            _ = DispatchTrigger(AutomationTrigger.MediaEnriched, null);
        }
    }

    public void Handle(ArchiveExtractionCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.ArchiveExtracted, message.Torrent);
        }
    }

    public void Handle(ArchiveExtractionFailedEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.ExtractionFailed, message.Torrent);
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.VpnDisconnected, null);
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.VpnRestored, null);
    }

    public void Handle(CategoryUpdatedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.CategoryChanged, null);
    }

    public void Handle(ApplicationStartedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.ApplicationStarted, null);
    }

    public void Handle(TorrentStartedEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.TorrentStarted, message.Torrent);
        }
    }

    public void Handle(TorrentPausedEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.TorrentPaused, message.Torrent);
        }
    }

    public void Handle(TorrentStalledEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.TorrentStalled, message.Torrent);
        }
    }

    public void Handle(TorrentSeedingTimeReachedEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.SeedingTimeReached, message.Torrent);
        }
    }

    public void Handle(TorrentHashCheckCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.HashCheckCompleted, message.Torrent);
        }
    }

    public void Handle(TorrentProgressMilestoneEvent message)
    {
        if (message?.Torrent != null)
        {
            _ = DispatchTrigger(AutomationTrigger.ProgressMilestone, message.Torrent);
        }
    }

    public void Handle(SpeedThresholdExceededEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.SpeedThresholdExceeded, null);
    }

    public void Handle(SpeedThresholdDroppedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.SpeedThresholdDropped, null);
    }

    public void Handle(BandwidthQuotaApproachingEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.BandwidthQuotaApproaching, null);
    }

    public void Handle(PortForwardingFailedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.PortForwardingFailed, null);
    }

    public void Handle(PeerBannedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.PeerBanned, null);
    }

    public void Handle(TrackerUnreachableEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.TrackerUnreachable, message?.Torrent);
    }

    public void Handle(TrackerBoostAppliedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.TrackerBoostApplied, message?.Torrent);
    }

    public void Handle(DiskSpaceLowEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.DiskSpaceLow, null);
    }

    public void Handle(DiskSpaceCriticalEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.DiskSpaceCritical, null);
    }

    public void Handle(FileMoveFailedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.FileMoveFailed, message?.Torrent);
    }

    public void Handle(MediaInspectionFailedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.MediaInspectionFailed, message?.Torrent);
    }

    public void Handle(ArrImportCompletedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.ArrImportCompleted, message?.Torrent);
    }

    public void Handle(ApplicationUpdatedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.ApplicationUpdated, null);
    }

    public void Handle(BackupCompletedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.BackupCompleted, null);
    }

    public void Handle(BackupFailedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.BackupFailed, null);
    }

    public void Handle(TaskFailedEvent message)
    {
        _ = DispatchTrigger(AutomationTrigger.TaskFailed, null);
    }

    internal Task DispatchTrigger(AutomationTrigger trigger, Torrent? torrent = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var allScripts = _automationService.GetAll();
                if (allScripts == null || allScripts.Count == 0)
                {
                    return;
                }

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

                    var key = (script.Id, torrent?.Id, trigger);
                    var now = DateTime.UtcNow;
                    var shouldExecute = false;

                    _lastExecutionTime.AddOrUpdate(
                        key,
                        _ =>
                        {
                            shouldExecute = true;
                            return now;
                        },
                        (_, lastRun) =>
                        {
                            if (now - lastRun < _cooldownWindow)
                            {
                                return lastRun;
                            }

                            shouldExecute = true;
                            return now;
                        });

                    if (!shouldExecute)
                    {
                        _logger.Debug("Debouncing execution of script '{0}' for trigger {1}", script.Name, trigger);
                        continue;
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

                if (_lastExecutionTime.Count > 1000)
                {
                    CleanupExecutionCache();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error dispatching automation trigger {0}", trigger);
            }
        });
    }

    private void CleanupExecutionCache()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        foreach (var kvp in _lastExecutionTime)
        {
            if (kvp.Value < cutoff)
            {
                _lastExecutionTime.TryRemove(kvp.Key, out _);
            }
        }
    }
}
