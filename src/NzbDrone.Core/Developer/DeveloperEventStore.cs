// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Developer;

public class DeveloperEventStore : IDeveloperEventStore
{
    private const int MaxEntries = 250;
    private readonly ConcurrentQueue<DeveloperEventEntry> events = new();

    public void RecordEvent(object @event)
    {
        if (@event == null)
        {
            return;
        }

        var eventType = @event.GetType();
        string payloadJson;
        try
        {
            payloadJson = @event.ToJson();
        }
        catch (Exception ex)
        {
            payloadJson = $"{{\"error\":\"Serialization failed: {ex.Message.Replace("\"", "\\\"")}\"}}";
        }

        var entry = new DeveloperEventEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            EventName = eventType.Name,
            EventType = eventType.FullName ?? eventType.Name,
            SourceNamespace = eventType.Namespace ?? string.Empty,
            PayloadJson = payloadJson,
        };

        this.events.Enqueue(entry);

        while (this.events.Count > MaxEntries && this.events.TryDequeue(out _))
        {
        }
    }

    public List<DeveloperEventEntry> GetRecentEvents(int limit = 100, string search = null)
    {
        var items = this.events.ToArray().Reverse();

        if (!string.IsNullOrWhiteSpace(search))
        {
            items = items.Where(e =>
                e.EventName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.PayloadJson.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.SourceNamespace.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return items.Take(Math.Clamp(limit, 1, MaxEntries)).ToList();
    }

    public void Clear()
    {
        while (this.events.TryDequeue(out _))
        {
        }
    }
}
