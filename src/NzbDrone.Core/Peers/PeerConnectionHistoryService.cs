// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.Core.Network.GeoIp;

namespace NzbDrone.Core.Peers;

public class PeerConnectionHistoryService : IPeerConnectionHistoryService
{
    private const int MaxRecords = 10000;
    private readonly ConcurrentQueue<PeerConnectionEvent> eventQueue = new();
    private readonly IGeoIpService geoIpService;
    private readonly object syncRoot = new();
    private long idSequence;

    public PeerConnectionHistoryService(IGeoIpService geoIpService = null)
    {
        this.geoIpService = geoIpService;
    }

    public void RecordEvent(PeerConnectionEvent connectionEvent)
    {
        if (connectionEvent == null)
        {
            return;
        }

        if (connectionEvent.Id == 0)
        {
            connectionEvent.Id = Interlocked.Increment(ref this.idSequence);
        }

        if (string.IsNullOrEmpty(connectionEvent.CountryCode) &&
            string.IsNullOrEmpty(connectionEvent.CountryName) &&
            !string.IsNullOrEmpty(connectionEvent.RemoteIp) &&
            this.geoIpService != null)
        {
            try
            {
                var geo = this.geoIpService.Lookup(connectionEvent.RemoteIp);
                if (geo != null)
                {
                    connectionEvent.CountryCode = geo.CountryCode ?? string.Empty;
                    connectionEvent.CountryName = geo.CountryName ?? string.Empty;
                    connectionEvent.City = geo.City ?? string.Empty;
                }
            }
            catch
            {
                // Ignore geo lookup errors
            }
        }

        lock (this.syncRoot)
        {
            this.eventQueue.Enqueue(connectionEvent);

            while (this.eventQueue.Count > MaxRecords)
            {
                this.eventQueue.TryDequeue(out _);
            }
        }
    }

    public IReadOnlyList<PeerConnectionEvent> GetRecords(DateTime? start = null, DateTime? end = null, string infoHash = null)
    {
        IEnumerable<PeerConnectionEvent> records;
        lock (this.syncRoot)
        {
            records = this.eventQueue.ToArray();
        }

        if (start.HasValue)
        {
            records = records.Where(r => r.Timestamp >= start.Value);
        }

        if (end.HasValue)
        {
            records = records.Where(r => r.Timestamp <= end.Value);
        }

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            records = records.Where(r => string.Equals(r.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        }

        return records.OrderBy(r => r.Timestamp).ToList();
    }

    public void Purge(DateTime? before = null)
    {
        if (!before.HasValue)
        {
            this.Clear();
            return;
        }

        var threshold = before.Value;
        lock (this.syncRoot)
        {
            var kept = new List<PeerConnectionEvent>();

            while (this.eventQueue.TryDequeue(out var item))
            {
                if (item.Timestamp >= threshold)
                {
                    kept.Add(item);
                }
            }

            foreach (var item in kept)
            {
                this.eventQueue.Enqueue(item);
            }
        }
    }

    public void Clear()
    {
        lock (this.syncRoot)
        {
            while (this.eventQueue.TryDequeue(out _))
            {
            }
        }
    }
}
