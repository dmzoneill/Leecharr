// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentLogService : ITorrentLogService,
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<HealthIssueEvent>,
    IHandle<TorrentDeletedEvent>
{
    private static readonly ConcurrentDictionary<int, ConcurrentQueue<TorrentEventLog>> LogsByTorrent = new();
    private static int nextLogId;
    private readonly Logger logger;

    public TorrentLogService()
    {
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public void Log(int torrentId, string level, string source, string message, DateTime? timestamp = null)
    {
        if (torrentId <= 0 || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new TorrentEventLog
        {
            Id = Interlocked.Increment(ref nextLogId),
            TorrentId = torrentId,
            Level = level ?? "Info",
            Source = source ?? "Engine",
            Message = message,
            Timestamp = timestamp ?? DateTime.UtcNow,
        };

        var queue = LogsByTorrent.GetOrAdd(torrentId, _ => new ConcurrentQueue<TorrentEventLog>());
        queue.Enqueue(entry);

        while (queue.Count > 250 && queue.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<TorrentEventLog> GetLogs(int torrentId, int limit = 100)
    {
        if (torrentId <= 0)
        {
            return Array.Empty<TorrentEventLog>();
        }

        if (LogsByTorrent.TryGetValue(torrentId, out var queue))
        {
            return queue.Reverse().Take(Math.Max(1, limit)).ToList();
        }

        return Array.Empty<TorrentEventLog>();
    }

    public void ClearLogs(int torrentId)
    {
        if (torrentId > 0)
        {
            LogsByTorrent.TryRemove(torrentId, out _);
        }
    }

    public void ClearAll()
    {
        LogsByTorrent.Clear();
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent != null)
        {
            this.Log(
                message.Torrent.Id,
                "Info",
                "Engine",
                $"Torrent '{message.Torrent.Name}' added to queue in category '{message.Torrent.Category ?? "Default"}'",
                message.Torrent.DateAdded);

            if (!string.IsNullOrWhiteSpace(message.Torrent.SavePath))
            {
                this.Log(
                    message.Torrent.Id,
                    "Info",
                    "Storage",
                    $"Storage allocation configured at '{message.Torrent.SavePath}'",
                    message.Torrent.DateAdded.AddSeconds(1));
            }
        }
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent != null)
        {
            var level = message.NewStatus == TorrentStatus.Error ? "Error" : "Info";
            var source = message.NewStatus switch
            {
                TorrentStatus.Checking => "Engine",
                TorrentStatus.Downloading => "Download",
                TorrentStatus.Seeding => "Seeding",
                TorrentStatus.Paused => "Engine",
                TorrentStatus.Stopped => "Engine",
                TorrentStatus.Stalled => "Tracker",
                TorrentStatus.Error => "Engine",
                _ => "Engine",
            };

            this.Log(
                message.Torrent.Id,
                level,
                source,
                $"Torrent state changed: {message.OldStatus} -> {message.NewStatus}");
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            this.Log(
                message.Torrent.Id,
                "Info",
                "Download",
                "Torrent download completed (100% verified)",
                message.Torrent.DateCompleted ?? DateTime.UtcNow);
        }
    }

    public void Handle(HealthIssueEvent message)
    {
        var tid = message?.TorrentId > 0 ? message.TorrentId : message?.Torrent?.Id ?? 0;
        if (tid > 0 && !string.IsNullOrWhiteSpace(message.Message))
        {
            var level = message.IsResolved ? "Info" : "Warn";
            var prefix = message.IsResolved ? "Health issue resolved: " : "Health issue: ";
            this.Log(
                tid,
                level,
                message.Source ?? "Tracker",
                $"{prefix}{message.Message}");
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent != null && message.Torrent.Id > 0)
        {
            this.ClearLogs(message.Torrent.Id);
        }
    }
}
