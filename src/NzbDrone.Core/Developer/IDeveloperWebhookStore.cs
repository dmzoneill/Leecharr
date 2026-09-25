// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Developer;

public class DeveloperWebhookEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string Source { get; set; } = "Arr";

    public string EventType { get; set; } = string.Empty;

    public string SourceIp { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string RawPayload { get; set; } = string.Empty;

    public string ResultMessage { get; set; } = string.Empty;

    public bool Success { get; set; }
}

public interface IDeveloperWebhookStore
{
    void Record(
        string source,
        string eventType,
        string sourceIp,
        string userAgent,
        int statusCode,
        string rawPayload,
        string resultMessage,
        bool success);

    List<DeveloperWebhookEntry> GetRecent(int limit = 50);

    void Clear();
}

public class DeveloperWebhookStore : IDeveloperWebhookStore
{
    private const int MaxEntries = 100;
    private readonly ConcurrentQueue<DeveloperWebhookEntry> webhooks = new();

    public void Record(
        string source,
        string eventType,
        string sourceIp,
        string userAgent,
        int statusCode,
        string rawPayload,
        string resultMessage,
        bool success)
    {
        var entry = new DeveloperWebhookEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            Source = source ?? "Arr",
            EventType = eventType ?? "Unknown",
            SourceIp = sourceIp ?? string.Empty,
            UserAgent = userAgent ?? string.Empty,
            StatusCode = statusCode,
            RawPayload = rawPayload ?? string.Empty,
            ResultMessage = resultMessage ?? string.Empty,
            Success = success,
        };

        this.webhooks.Enqueue(entry);

        while (this.webhooks.Count > MaxEntries && this.webhooks.TryDequeue(out _))
        {
        }
    }

    public List<DeveloperWebhookEntry> GetRecent(int limit = 50)
    {
        return this.webhooks.ToArray().Reverse().Take(Math.Clamp(limit, 1, MaxEntries)).ToList();
    }

    public void Clear()
    {
        while (this.webhooks.TryDequeue(out _))
        {
        }
    }
}
