using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public class NotificationEventHandler :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NotificationEventHandler(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
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
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnTorrentDeleted, "OnTorrentDeleted", message.Torrent);
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
        }
        else if (message.OldStatus == TorrentStatus.Error && message.NewStatus != TorrentStatus.Error)
        {
            Dispatch(n => n.OnHealthRestored, "OnHealthRestored", message.Torrent);
        }
        else if (message.NewStatus == TorrentStatus.Stopped && message.Torrent.Progress >= 1.0)
        {
            Dispatch(n => n.OnSeedGoalReached, "OnSeedGoalReached", message.Torrent);
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
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
            Timestamp = DateTime.UtcNow
        };

        foreach (var notif in activeNotifications)
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() => _customScriptService.ExecuteScriptAsync(notif.Settings, torrent, eventType));
            }
            else
            {
                Task.Run(() => _webhookDispatcher.DispatchAsync(notif.Settings, payload));
            }
        }
    }
}
