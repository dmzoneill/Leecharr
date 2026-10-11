// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.Trackers;

public static class TrackerAnnounceUrlCanonicalizer
{
    public static string Canonicalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();

        var path = uri.AbsolutePath;
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }
        else if (path == "/")
        {
            path = string.Empty;
        }

        var authority = host;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            authority = $"{uri.UserInfo}@{authority}";
        }

        var portPart = string.Empty;
        if (!uri.IsDefaultPort && uri.Port > 0)
        {
            portPart = $":{uri.Port}";
        }

        return $"{scheme}://{authority}{portPart}{path}{uri.Query}{uri.Fragment}";
    }

    public static bool AreEquivalent(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        return string.Equals(
            Canonicalize(left),
            Canonicalize(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
