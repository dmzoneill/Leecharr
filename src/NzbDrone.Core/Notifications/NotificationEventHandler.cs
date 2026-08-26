using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public class NotificationEventHandler :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<MediaEnrichedEvent>,
    IHandle<ArchiveExtractionCompletedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;
    private readonly IConfigService _configService;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileRepository _torrentFileRepository;
    private readonly IDownloadEngine _downloadEngine;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NotificationEventHandler(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService,
        IConfigService configService,
        IMediaEnrichmentService mediaEnrichmentService = null,
        ITorrentRepository torrentRepository = null,
        ITorrentFileRepository torrentFileRepository = null,
        IDownloadEngine downloadEngine = null)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
        _configService = configService;
        _mediaEnrichmentService = mediaEnrichmentService;
        _torrentRepository = torrentRepository;
        _torrentFileRepository = torrentFileRepository;
        _downloadEngine = downloadEngine;
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnGrab, "OnGrab", message.Torrent);
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnDownloadComplete, "OnDownloadComplete", message.Torrent);

        if (!string.IsNullOrWhiteSpace(_configService?.OnDownloadCompleteScript))
        {
            Task.Run(() => _customScriptService.ExecuteScriptAsync(_configService.OnDownloadCompleteScript, message.Torrent, "OnDownloadComplete"));
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnTorrentDeleted, "OnTorrentDeleted", message.Torrent);
    }

    public void Handle(MediaEnrichedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrent = _torrentRepository?.Get(message.TorrentId);
        if (torrent != null)
        {
            Dispatch(n => n.OnMediaInspected, "OnMediaInspected", torrent);
        }
    }

    public void Handle(ArchiveExtractionCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnExtractComplete, "OnExtractComplete", message.Torrent);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus == TorrentStatus.Error)
        {
            Dispatch(n => n.OnHealthIssue, "OnHealthIssue", message.Torrent);
            Dispatch(n => n.OnManualInteractionRequired, "OnManualInteractionRequired", message.Torrent);
        }
        else if (message.OldStatus == TorrentStatus.Error && message.NewStatus != TorrentStatus.Error)
        {
            Dispatch(n => n.OnHealthRestored, "OnHealthRestored", message.Torrent);
        }
        else if (message.NewStatus == TorrentStatus.Stopped && message.Torrent.Progress >= 1.0)
        {
            Dispatch(n => n.OnSeedGoalReached, "OnSeedGoalReached", message.Torrent);

            if (!string.IsNullOrWhiteSpace(_configService?.OnSeedGoalReachedScript))
            {
                Task.Run(() => _customScriptService.ExecuteScriptAsync(_configService.OnSeedGoalReachedScript, message.Torrent, "OnSeedGoalReached"));
            }
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        if (_downloadEngine != null)
        {
            _logger.Warn("Halting download engine due to VPN Kill Switch event on interface: {0}", message.InterfaceName);
            Task.Run(async () =>
            {
                try
                {
                    await _downloadEngine.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error halting download engine after VPN kill switch trigger");
                }
            });
        }

        var activeNotifications = _notificationRepository.GetEnabled().Where(n => n.OnHealthIssue).ToList();
        var payload = new
        {
            EventType = "OnHealthIssue",
            Message = "VPN Kill Switch triggered: VPN interface disconnected. BitTorrent traffic halted.",
            Timestamp = DateTime.UtcNow
        };

        foreach (var notif in activeNotifications)
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() => _customScriptService.ExecuteScriptAsync(notif.Settings, null, "OnHealthIssue"));
            }
            else
            {
                Task.Run(() => _webhookDispatcher.DispatchAsync(notif.Settings, payload));
            }
        }
    }

    private void Dispatch(Func<NotificationDefinition, bool> predicate, string eventType, Torrent torrent)
    {
        var activeNotifications = _notificationRepository.GetEnabled().Where(predicate).ToList();
        if (activeNotifications.Count == 0)
        {
            return;
        }

        var meta = _mediaEnrichmentService?.GetMetadata(torrent.Id);
        var files = _torrentFileRepository?.GetByTorrentId(torrent.Id)?.Select(f => new
        {
            Path = f.Path,
            Size = f.Size,
            Progress = f.Progress
        }).ToList();

        var payload = new
        {
            EventType = eventType,
            TorrentId = torrent.Id,
            Name = torrent.Name,
            InfoHash = torrent.InfoHash,
            Category = torrent.Category,
            SavePath = torrent.SavePath,
            TotalSize = torrent.TotalSize,
            Progress = torrent.Progress,
            Status = torrent.Status.ToString(),
            MediaTitle = meta?.Title,
            MediaYear = meta?.Year,
            PosterUrl = meta?.PosterUrl,
            BackdropUrl = meta?.BackdropUrl,
            Overview = meta?.Overview,
            Genres = meta?.Genres,
            Rating = meta?.Rating,
            ImdbId = meta?.ImdbId,
            Files = files,
            Timestamp = DateTime.UtcNow
        };

        foreach (var notif in activeNotifications)
        {
            // Tag filtering
            if (notif.Tags != null && notif.Tags.Count > 0)
            {
                if (torrent.TagIds == null || !notif.Tags.Any(t => torrent.TagIds.Contains(t)))
                {
                    continue;
                }
            }

            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() => _customScriptService.ExecuteScriptAsync(notif.Settings, torrent, eventType));
            }
            else
            {
                var providerPayload = BuildProviderPayload(notif.Implementation, eventType, torrent, meta, payload);
                Task.Run(() => _webhookDispatcher.DispatchAsync(notif.Settings, providerPayload));
            }
        }
    }

    private static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload)
    {
        if (string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            var title = $"[{eventType}] {torrent.Name}";
            var desc = meta?.Overview ?? $"Category: {torrent.Category ?? "None"} | Progress: {torrent.Progress * 100:F1}% | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB";
            return new
            {
                username = "Leecharr",
                embeds = new object[]
                {
                    new
                    {
                        title,
                        description = desc,
                        color = 16765286, // Gold
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };
        }

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var text = $"*Leecharr [{eventType}]*\n*{torrent.Name}*\nCategory: {torrent.Category ?? "None"}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}";
            return new
            {
                text,
                parse_mode = "Markdown"
            };
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                title = $"Leecharr: {eventType}",
                message = $"{torrent.Name} ({torrent.Category ?? "Default"}) - {torrent.Status}",
                priority = 5
            };
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                title = $"Leecharr: {eventType}",
                message = $"{torrent.Name} ({torrent.Category ?? "Default"}) - {torrent.Status}"
            };
        }

        return genericPayload;
    }
}
