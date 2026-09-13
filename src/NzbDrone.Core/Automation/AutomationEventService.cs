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
    IHandle<HealthIssueEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<MediaEnrichedEvent>,
    IHandle<ArchiveExtractionCompletedEvent>,
    IHandle<ArchiveExtractionFailedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IHandle<CategoryUpdatedEvent>,
    IHandle<ApplicationStartedEvent>
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
