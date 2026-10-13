// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Developer;

public class DeveloperHttpTrafficStore : IDeveloperHttpTrafficStore
{
    private const int MaxEntries = 250;
    private const int MaxBodyLength = 1024;
    private const string RedactedValue = "***REDACTED***";

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
    };

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
        CopyHeaders(request.Headers, reqHeaders);
        if (request.Content != null)
        {
            CopyHeaders(request.Content.Headers, reqHeaders);
        }

        var respHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response != null)
        {
            CopyHeaders(response.Headers, respHeaders);
            if (response.Content != null)
            {
                CopyHeaders(response.Content.Headers, respHeaders);
            }
        }

        var uri = request.RequestUri;
        var urlStr = uri == null ? string.Empty : TrackerUrlSanitizer.Sanitize(uri.ToString());
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
        return this.EnumerateMatching(search)
            .Take(Math.Clamp(limit, 1, MaxEntries))
            .ToList();
    }

    public int GetRecordedCount(string search = null)
    {
        return this.EnumerateMatching(search).Count();
    }

    private IEnumerable<DeveloperHttpTrafficEntry> EnumerateMatching(string search)
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

        return items;
    }

    public void Clear()
    {
        while (this.traffic.TryDequeue(out _))
        {
        }
    }

    private static void CopyHeaders(HttpHeaders headers, Dictionary<string, string> target)
    {
        foreach (var h in headers)
        {
            target[h.Key] = ShouldRedactHeader(h.Key) ? RedactedValue : string.Join(", ", h.Value);
        }
    }

    private static bool ShouldRedactHeader(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (SensitiveHeaderNames.Contains(name))
        {
            return true;
        }

        return name.Contains("api-key", StringComparison.OrdinalIgnoreCase);
    }
}
