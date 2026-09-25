// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NzbDrone.Core.Developer;

public class DeveloperHttpTrafficStore : IDeveloperHttpTrafficStore
{
    private const int MaxEntries = 250;
    private const int MaxBodyLength = 1024;
    private readonly ConcurrentQueue<DeveloperHttpTrafficEntry> traffic = new();

    public void Record(
        HttpRequestMessage request,
        HttpResponseMessage response,
        double durationMs,
        string transportEngine,
        string responseBodyPreview = null,
        Exception error = null)
    {
        if (request == null)
        {
            return;
        }

        var reqHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
        {
            // Mask auth headers for security
            if (string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h.Key, "X-Api-Key", StringComparison.OrdinalIgnoreCase))
            {
                reqHeaders[h.Key] = "***REDACTED***";
            }
            else
            {
                reqHeaders[h.Key] = string.Join(", ", h.Value);
            }
        }

        var respHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response?.Headers != null)
        {
            foreach (var h in response.Headers)
            {
                respHeaders[h.Key] = string.Join(", ", h.Value);
            }
        }

        var uri = request.RequestUri;
        var urlStr = uri?.ToString() ?? string.Empty;
        var hostStr = uri?.Host ?? string.Empty;

        var statusCode = response != null ? (int)response.StatusCode : (error != null ? 500 : 0);
        var isSuccess = response != null && response.IsSuccessStatusCode;

        var preview = responseBodyPreview;
        if (string.IsNullOrEmpty(preview) && error != null)
        {
            preview = error.Message;
        }

        if (preview != null && preview.Length > MaxBodyLength)
        {
            preview = string.Concat(preview.AsSpan(0, MaxBodyLength), "... [truncated]");
        }

        var entry = new DeveloperHttpTrafficEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            Method = request.Method.Method,
            Url = urlStr,
            Host = hostStr,
            StatusCode = statusCode,
            DurationMs = Math.Round(durationMs, 2),
            IsSuccess = isSuccess,
            TransportEngine = transportEngine ?? "Default",
            RequestHeaders = reqHeaders,
            ResponseHeaders = respHeaders,
            ResponseBodyPreview = preview ?? string.Empty,
            ErrorMessage = error?.Message,
        };

        this.traffic.Enqueue(entry);

        while (this.traffic.Count > MaxEntries && this.traffic.TryDequeue(out _))
        {
        }
    }

    public List<DeveloperHttpTrafficEntry> GetRecent(int limit = 100, string search = null)
    {
        var items = this.traffic.ToArray().Reverse();

        if (!string.IsNullOrWhiteSpace(search))
        {
            items = items.Where(t =>
                t.Url.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.Host.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.Method.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.StatusCode.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return items.Take(Math.Clamp(limit, 1, MaxEntries)).ToList();
    }

    public void Clear()
    {
        while (this.traffic.TryDequeue(out _))
        {
        }
    }
}
