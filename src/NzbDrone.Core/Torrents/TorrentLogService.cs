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
    private const int MaxLogsPerTorrent = 250;
    private const int MaxTrackedTorrents = 1000;

    private static readonly ConcurrentDictionary<int, TorrentLogBuffer> LogsByTorrent = new();
    private static int nextLogId;

    private readonly ITorrentEventLogRepository repository;
    private readonly Logger logger;

    public TorrentLogService(ITorrentEventLogRepository repository = null)
    {
        this.repository = repository;
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
            TorrentId = torrentId,
            Level = level ?? "Info",
            Source = source ?? "Engine",
            Message = message,
            Timestamp = timestamp ?? DateTime.UtcNow,
        };

        if (this.repository != null)
        {
            try
            {
                this.repository.Insert(entry);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to persist torrent event log to database for torrent {0}", torrentId);
                if (entry.Id == 0)
                {
                    entry.Id = Interlocked.Increment(ref nextLogId);
                }
            }
        }
        else
        {
            entry.Id = Interlocked.Increment(ref nextLogId);
        }

        var buffer = LogsByTorrent.GetOrAdd(torrentId, static _ => new TorrentLogBuffer());
        buffer.Touch();

        buffer.Queue.Enqueue(entry);
        buffer.Increment();

        while (buffer.Count > MaxLogsPerTorrent)
        {
            if (buffer.Queue.TryDequeue(out _))
            {
                buffer.Decrement();
            }
            else
            {
                break;
            }
        }

        this.EvictOldestTorrentsIfNeeded();
    }

    public IReadOnlyList<TorrentEventLog> GetLogs(int torrentId, int limit = 100)
    {
        if (torrentId <= 0)
        {
            return Array.Empty<TorrentEventLog>();
        }

        var effectiveLimit = Math.Max(1, limit);

        if (LogsByTorrent.TryGetValue(torrentId, out var buffer))
        {
            buffer.Touch();

            var snapshot = buffer.Queue.ToArray();
            if (snapshot.Length > 0)
            {
                var count = Math.Min(effectiveLimit, snapshot.Length);
                var result = new List<TorrentEventLog>(count);
                for (var i = snapshot.Length - 1; i >= snapshot.Length - count; i--)
                {
                    result.Add(snapshot[i]);
                }

                return result;
            }
        }

        if (this.repository != null)
        {
            try
            {
                var dbLogs = this.repository.GetLogsForTorrent(torrentId, effectiveLimit);
                if (dbLogs != null && dbLogs.Count > 0)
                {
                    return dbLogs;
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to load torrent event logs from database for torrent {0}", torrentId);
            }
        }

        return Array.Empty<TorrentEventLog>();
    }

    public void ClearLogs(int torrentId)
    {
        if (torrentId > 0)
        {
            LogsByTorrent.TryRemove(torrentId, out _);

            if (this.repository != null)
            {
                try
                {
                    this.repository.DeleteForTorrent(torrentId);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to delete torrent event logs from database for torrent {0}", torrentId);
                }
            }
        }
    }

    public void ClearAll()
    {
        LogsByTorrent.Clear();

        if (this.repository != null)
        {
            try
            {
                this.repository.DeleteAll();
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to clear all torrent event logs from database");
            }
        }
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

            var progressStr = message.Torrent.Progress > 0 ? $" ({message.Torrent.Progress:P1} verified)" : string.Empty;
            this.Log(
                message.Torrent.Id,
                level,
                source,
                $"[State Machine] Torrent state changed: {message.OldStatus} -> {message.NewStatus}{progressStr}");
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

    private void EvictOldestTorrentsIfNeeded()
    {
        if (LogsByTorrent.Count <= MaxTrackedTorrents)
        {
            return;
        }

        var excess = LogsByTorrent.Count - MaxTrackedTorrents;
        if (excess <= 0)
        {
            return;
        }

        var oldest = LogsByTorrent
            .OrderBy(kvp => kvp.Value.LastAccessedTicks)
            .Take(excess + 50)
            .ToList();

        foreach (var entry in oldest)
        {
            LogsByTorrent.TryRemove(entry.Key, out _);
            if (LogsByTorrent.Count <= MaxTrackedTorrents)
            {
                break;
            }
        }
    }

    private sealed class TorrentLogBuffer
    {
        private int count;
        private long lastAccessedTicks = DateTime.UtcNow.Ticks;

        public ConcurrentQueue<TorrentEventLog> Queue { get; } = new();

        public int Count => Volatile.Read(ref this.count);

        public long LastAccessedTicks => Interlocked.Read(ref this.lastAccessedTicks);

        public int Increment() => Interlocked.Increment(ref this.count);

        public int Decrement() => Interlocked.Decrement(ref this.count);

        public void Touch() => Interlocked.Exchange(ref this.lastAccessedTicks, DateTime.UtcNow.Ticks);
    }
}
