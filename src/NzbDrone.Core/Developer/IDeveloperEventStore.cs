// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Developer;

public class DeveloperEventEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string EventName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string SourceNamespace { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;
}

public interface IDeveloperEventStore
{
    void RecordEvent(object @event);

    List<DeveloperEventEntry> GetRecentEvents(int limit = 100, string search = null);

    void Clear();
}
