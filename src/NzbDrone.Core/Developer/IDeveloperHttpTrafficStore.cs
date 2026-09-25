// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;

namespace NzbDrone.Core.Developer;

public class DeveloperHttpTrafficEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string Method { get; set; } = "GET";

    public string Url { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public double DurationMs { get; set; }

    public bool IsSuccess { get; set; }

    public string TransportEngine { get; set; } = "Default";

    public Dictionary<string, string> RequestHeaders { get; set; } = new();

    public Dictionary<string, string> ResponseHeaders { get; set; } = new();

    public string ResponseBodyPreview { get; set; } = string.Empty;

    public string ErrorMessage { get; set; }
}

public interface IDeveloperHttpTrafficStore
{
    void Record(
        HttpRequestMessage request,
        HttpResponseMessage response,
        double durationMs,
        string transportEngine,
        string responseBodyPreview = null,
        Exception error = null);

    List<DeveloperHttpTrafficEntry> GetRecent(int limit = 100, string search = null);

    void Clear();
}
