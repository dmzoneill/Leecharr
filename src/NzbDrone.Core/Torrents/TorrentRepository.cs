// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentRepository : BasicRepository<Torrent>, ITorrentRepository
{
    private readonly IEventAggregator eventAggregator;

    public TorrentRepository(IDatabase database, IEventAggregator eventAggregator = null)
        : base(database, eventAggregator)
    {
        this.eventAggregator = eventAggregator;
    }

    public override Torrent Insert(Torrent model)
    {
        NormalizeTorrent(model);
        return base.Insert(model);
    }

    public override Torrent Update(Torrent model)
    {
        NormalizeTorrent(model);
        return base.Update(model);
    }

    public override void UpsertMany(IEnumerable<Torrent> toInsert, IEnumerable<Torrent> toUpdate)
    {
        if (toInsert != null)
        {
            foreach (var item in toInsert)
            {
                NormalizeTorrent(item);
            }
        }

        if (toUpdate != null)
        {
            foreach (var item in toUpdate)
            {
                NormalizeTorrent(item);
            }
        }

        base.UpsertMany(toInsert, toUpdate);
    }

    public override void Delete(int id)
    {
        var existing = this.Get(id);
        this.ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                connection.Execute("DELETE FROM \"TorrentFiles\" WHERE \"TorrentId\" = @TorrentId", new { TorrentId = id }, transaction);
                connection.Execute("DELETE FROM \"TrackerEntries\" WHERE \"TorrentId\" = @TorrentId", new { TorrentId = id }, transaction);
                connection.Execute("DELETE FROM \"TorrentMediaMetadata\" WHERE \"TorrentId\" = @TorrentId", new { TorrentId = id }, transaction);
                connection.Execute($"DELETE FROM \"{this.table}\" WHERE \"Id\" = @Id", new { Id = id }, transaction);

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Ignore rollback exceptions if transaction is already completed
                }

                throw;
            }
        });

        this.eventAggregator?.PublishEvent(new ModelEvent<Torrent>(existing ?? new Torrent { Id = id }, ModelAction.Deleted));
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var normalized = infoHash.Trim().ToLowerInvariant();
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<Torrent>(
                $"SELECT * FROM \"{this.table}\" WHERE \"InfoHash\" = @InfoHash",
                new { InfoHash = normalized }));
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        var normalized = infoHash.Trim().ToLowerInvariant();
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<int>(
                $"SELECT COUNT(1) FROM \"{this.table}\" WHERE \"InfoHash\" = @InfoHash",
                new { InfoHash = normalized }) > 0);
    }

    public IEnumerable<Torrent> GetByCategory(string category)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<Torrent>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Category\" = @Category",
                new { Category = category }));
    }

    public IEnumerable<Torrent> GetByStatus(TorrentStatus status)
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<Torrent>(
                $"SELECT * FROM \"{this.table}\" WHERE \"Status\" = @Status",
                new { Status = (int)status }));
    }

    public int GetNextQueuePosition()
    {
        return this.ExecuteWithRetry(connection =>
            connection.ExecuteScalar<int>($"SELECT COALESCE(MAX(\"QueuePosition\"), 0) + 1 FROM \"{this.table}\""));
    }

    private static void NormalizeTorrent(Torrent model)
    {
        if (model?.InfoHash != null)
        {
            model.InfoHash = model.InfoHash.Trim().ToLowerInvariant();
        }
    }
}
