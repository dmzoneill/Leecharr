// Copyright (c) FeedItOut. All rights reserved.
using System;

namespace NzbDrone.Core.ArrIntegration;

public static class ArrConnectionUrlNormalizer
{
    /// <summary>
    /// Removes a trailing /api, /api/v1, or /api/v3 segment from an Arr service root URL.
    /// </summary>
    public static string StripApiSuffix(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var baseUrl = url.TrimEnd('/');
        if (baseUrl.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl.Substring(0, baseUrl.Length - 7).TrimEnd('/');
        }

        if (baseUrl.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl.Substring(0, baseUrl.Length - 7).TrimEnd('/');
        }

        if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl.Substring(0, baseUrl.Length - 4).TrimEnd('/');
        }

        return baseUrl;
    }
}
