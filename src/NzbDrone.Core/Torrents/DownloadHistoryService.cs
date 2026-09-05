// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Http;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryService : IDownloadHistoryService, IHandle<TorrentAddedEvent>, IHandle<TorrentDeletedEvent>
{
    private readonly IDownloadHistoryRepository historyRepository;
    private readonly ITorrentRepository torrentRepository;
    private readonly ITrackerEntryRepository trackerEntryRepository;
    private readonly IDownloadEngine downloadEngine;
    private readonly IEventAggregator eventAggregator;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;

    public DownloadHistoryService(
        IDownloadHistoryRepository historyRepository,
        ITorrentRepository torrentRepository,
        ITrackerEntryRepository trackerEntryRepository,
        IDownloadEngine downloadEngine,
        IEventAggregator eventAggregator,
        ISafeHttpClientService safeHttpClientService = null,
        IAppFolderInfo appFolderInfo = null)
    {
        this.historyRepository = historyRepository;
        this.torrentRepository = torrentRepository;
        this.trackerEntryRepository = trackerEntryRepository;
        this.downloadEngine = downloadEngine;
        this.eventAggregator = eventAggregator;
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public List<DownloadHistory> GetAll(string query = null, string status = null, int limit = 500)
    {
        return this.historyRepository.GetHistory(query, status, limit);
    }

    public DownloadHistory Get(int id)
    {
        return this.historyRepository.Get(id);
    }

    public DownloadHistory GetByInfoHash(string infoHash)
    {
        return this.historyRepository.FindByInfoHash(infoHash);
    }

    public void Delete(int id)
    {
        this.logger.Info("Deleting download history entry {0}", id);
        this.historyRepository.Delete(id);
    }

    public void ClearAll()
    {
        this.logger.Info("Clearing all download history entries");
        this.historyRepository.DeleteAll();
    }

    public DownloadHistory RecordTorrentAdded(
        Torrent torrent,
        string source = null,
        string magnetUrl = null,
        string downloadUrl = null,
        string indexerName = null)
    {
        if (torrent == null)
        {
            return null;
        }

        var existing = !string.IsNullOrEmpty(torrent.InfoHash)
            ? this.historyRepository.FindByInfoHash(torrent.InfoHash)
            : null;

        if (existing != null)
        {
            existing.TorrentId = torrent.Id;
            existing.Title = torrent.Name ?? existing.Title;
            existing.TotalSize = torrent.TotalSize > 0 ? torrent.TotalSize : existing.TotalSize;
            existing.Status = "Active";
            existing.DateRemoved = null;

            if (!string.IsNullOrWhiteSpace(source))
            {
                existing.Source = source;
            }

            if (!string.IsNullOrWhiteSpace(magnetUrl))
            {
                existing.MagnetUrl = magnetUrl;
            }

            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                existing.DownloadUrl = downloadUrl;
            }

            if (!string.IsNullOrWhiteSpace(indexerName))
            {
                existing.IndexerName = indexerName;
            }

            if (string.IsNullOrWhiteSpace(existing.PrimaryTracker))
            {
                existing.PrimaryTracker = this.trackerEntryRepository?.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;
            }

            this.historyRepository.Update(existing);
            return existing;
        }

        var tracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;

        var entry = new DownloadHistory
        {
            TorrentId = torrent.Id,
            Title = torrent.Name ?? "Unknown Release",
            InfoHash = torrent.InfoHash ?? string.Empty,
            TotalSize = torrent.TotalSize,
            DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
            DateCompleted = torrent.Progress >= 1.0 ? DateTime.UtcNow : null,
            DateRemoved = null,
            Uploaded = torrent.Uploaded,
            Downloaded = torrent.Downloaded,
            Ratio = torrent.Ratio,
            SeedingTime = 0,
            PrimaryTracker = tracker,
            IndexerName = indexerName,
            Source = source ?? (torrent.Category ?? "Manual"),
            MagnetUrl = magnetUrl,
            DownloadUrl = downloadUrl,
            Status = "Active",
        };

        return this.historyRepository.Insert(entry);
    }

    public void RecordTorrentUpdated(Torrent torrent)
    {
        if (torrent == null)
        {
            return;
        }

        var entry = this.historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? this.historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry == null)
        {
            return;
        }

        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        if (torrent.Progress >= 1.0 && entry.DateCompleted == null)
        {
            entry.DateCompleted = DateTime.UtcNow;
            entry.Status = "Completed";
        }

        this.historyRepository.Update(entry);
    }

    public void RecordTorrentRemoved(Torrent torrent, string reason = "Deleted from library")
    {
        if (torrent == null)
        {
            return;
        }

        var entry = this.historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? this.historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry == null)
        {
            var tracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;

            entry = new DownloadHistory
            {
                Title = torrent.Name ?? "Unknown Release",
                InfoHash = torrent.InfoHash ?? string.Empty,
                TotalSize = torrent.TotalSize,
                DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
                DateCompleted = torrent.Progress >= 1.0 ? DateTime.UtcNow : null,
                DateRemoved = DateTime.UtcNow,
                Uploaded = torrent.Uploaded,
                Downloaded = torrent.Downloaded,
                Ratio = torrent.Ratio,
                SeedingTime = torrent.DateAdded != default ? (long)(DateTime.UtcNow - torrent.DateAdded).TotalSeconds : 0,
                PrimaryTracker = tracker,
                Source = "Library",
                Status = "Removed",
                RemovalReason = reason,
            };
            this.historyRepository.Insert(entry);
            return;
        }

        entry.TorrentId = null;
        entry.DateRemoved = DateTime.UtcNow;
        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        entry.Status = "Removed";
        entry.RemovalReason = reason;

        this.historyRepository.Update(entry);
    }

    public Torrent ReAdd(int historyId)
    {
        return this.ReAddAsync(historyId).GetAwaiter().GetResult();
    }

    public async Task<Torrent> ReAddAsync(int historyId)
    {
        var entry = this.historyRepository.Get(historyId);
        if (entry == null)
        {
            throw new ArgumentException($"History entry {historyId} not found");
        }

        if (this.torrentRepository.ExistsByInfoHash(entry.InfoHash))
        {
            throw new InvalidOperationException($"Torrent with info hash {entry.InfoHash} is already in the active library");
        }

        var torrent = new Torrent
        {
            Name = entry.Title,
            InfoHash = entry.InfoHash,
            TotalSize = entry.TotalSize,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow,
            Uploaded = entry.Uploaded,
            Downloaded = entry.Downloaded,
            Ratio = entry.Ratio,
            Category = entry.Source,
            TrackerUrl = entry.PrimaryTracker,
        };

        var all = this.torrentRepository.All().ToList();
        torrent.Priority = all.Count > 0 ? all.Max(t => t.Priority) + 1 : 0;

        var added = this.torrentRepository.Insert(torrent);

        if (!string.IsNullOrWhiteSpace(entry.PrimaryTracker))
        {
            this.trackerEntryRepository.Insert(new TrackerEntry
            {
                TorrentId = added.Id,
                Url = entry.PrimaryTracker,
                Tier = 0,
                Enabled = true,
            });
        }

        entry.TorrentId = added.Id;
        entry.Status = "Active";
        entry.DateRemoved = null;
        this.historyRepository.Update(entry);

        byte[] torrentBytes = null;
        var magnetUri = entry.MagnetUrl;

        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            if (!string.IsNullOrWhiteSpace(entry.DownloadUrl))
            {
                if (entry.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    magnetUri = entry.DownloadUrl;
                }
                else
                {
                    try
                    {
                        torrentBytes = await this.safeHttpClientService.DownloadBytesAsync(entry.DownloadUrl);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to download .torrent from {0} for re-added historical torrent {1}", entry.DownloadUrl, added.Id);
                    }
                }
            }

            if ((torrentBytes == null || torrentBytes.Length == 0) && string.IsNullOrWhiteSpace(magnetUri) && !string.IsNullOrWhiteSpace(added.InfoHash))
            {
                try
                {
                    var hash = added.InfoHash.ToLowerInvariant();
                    var pathsToTry = new List<string>();

                    if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
                    {
                        pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Torrents", $"{hash}.torrent"));
                        pathsToTry.Add(Path.Combine(this.appFolderInfo.AppDataFolder, "Leecharr", "Torrents", $"{hash}.torrent"));
                    }

                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrWhiteSpace(appData))
                    {
                        pathsToTry.Add(Path.Combine(appData, "Torrents", $"{hash}.torrent"));
                        pathsToTry.Add(Path.Combine(appData, "Leecharr", "Torrents", $"{hash}.torrent"));
                    }

                    foreach (var path in pathsToTry)
                    {
                        if (File.Exists(path))
                        {
                            torrentBytes = await File.ReadAllBytesAsync(path);
                            if (torrentBytes != null && torrentBytes.Length > 0)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to read cached .torrent file for {0}", added.InfoHash);
                }
            }

            if ((torrentBytes == null || torrentBytes.Length == 0) && string.IsNullOrWhiteSpace(magnetUri))
            {
                if (!string.IsNullOrWhiteSpace(entry.InfoHash))
                {
                    var magnetBuilder = new StringBuilder($"magnet:?xt=urn:btih:{entry.InfoHash}");
                    if (!string.IsNullOrWhiteSpace(entry.Title))
                    {
                        magnetBuilder.Append($"&dn={Uri.EscapeDataString(entry.Title)}");
                    }

                    var trackers = (this.trackerEntryRepository?.GetByTorrentId(added.Id) ?? Enumerable.Empty<TrackerEntry>())
                        .Select(t => t.Url)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct()
                        .ToList();

                    if (trackers.Count == 0 && !string.IsNullOrWhiteSpace(entry.PrimaryTracker))
                    {
                        trackers.Add(entry.PrimaryTracker);
                    }

                    foreach (var tracker in trackers)
                    {
                        magnetBuilder.Append($"&tr={Uri.EscapeDataString(tracker)}");
                    }

                    magnetUri = magnetBuilder.ToString();
                }
            }
        }

        if (torrentBytes != null && torrentBytes.Length > 0 && !string.IsNullOrWhiteSpace(added.InfoHash))
        {
            try
            {
                var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                    ? this.appFolderInfo.AppDataFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var torrentsDir = Path.Combine(appData, "Torrents");
                Directory.CreateDirectory(torrentsDir);
                var filePath = Path.Combine(torrentsDir, $"{added.InfoHash.ToLowerInvariant()}.torrent");
                await File.WriteAllBytesAsync(filePath, torrentBytes);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to save re-added .torrent file for {0}", added.InfoHash);
            }
        }

        try
        {
            await this.downloadEngine.AddTorrentAsync(added, torrentBytes, magnetUri);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to start engine for re-added historical torrent {0}", added.Id);
        }

        this.eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = added });

        this.logger.Info("Re-added historical torrent '{0}' (InfoHash: {1}) with ID {2}", entry.Title, entry.InfoHash, added.Id);
        return added;
    }

    public void Update(DownloadHistory history)
    {
        if (history != null)
        {
            this.historyRepository.Update(history);
        }
    }

    public int ReconcileAllTorrents()
    {
        var allTorrents = this.torrentRepository.All().ToList();
        var backfilled = 0;

        foreach (var torrent in allTorrents)
        {
            if (string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                continue;
            }

            var existing = this.historyRepository.FindByInfoHash(torrent.InfoHash);
            if (existing == null)
            {
                var tracker = this.trackerEntryRepository.GetByTorrentId(torrent.Id).FirstOrDefault()?.Url;

                var entry = new DownloadHistory
                {
                    TorrentId = torrent.Id,
                    Title = torrent.Name ?? torrent.InfoHash,
                    InfoHash = torrent.InfoHash.ToLowerInvariant(),
                    TotalSize = torrent.TotalSize,
                    DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
                    Uploaded = torrent.Uploaded,
                    Downloaded = torrent.Downloaded,
                    Ratio = torrent.Ratio,
                    PrimaryTracker = tracker,
                    Status = "Active",
                    SeedingTime = 0,
                    Source = torrent.IsPrivate ? "Private Tracker" : "Public Tracker",
                };

                this.historyRepository.Insert(entry);
                backfilled++;
            }
            else if (existing.TorrentId == null || existing.TorrentId == 0)
            {
                existing.TorrentId = torrent.Id;
                existing.Status = "Active";
                this.historyRepository.Update(existing);
            }
        }

        if (backfilled > 0)
        {
            this.logger.Info("Reconciled and backfilled {0} missing torrents into Download History", backfilled);
        }

        return backfilled;
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        this.RecordTorrentAdded(message.Torrent);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent != null)
        {
            this.RecordTorrentRemoved(message.Torrent, "Deleted from active library");
            return;
        }
    }
}
