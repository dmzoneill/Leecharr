// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Trackers;

public static class TrackerUrlSanitizer
{
    private const string Mask = "********";

    private static readonly HashSet<string> SensitiveQueryParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "passkey",
        "authkey",
        "torrentpass",
        "torrent_pass",
        "auth",
        "token",
        "pass",
        "key",
    };

    private static readonly HashSet<string> SensitivePathMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "passkey",
        "authkey",
        "torrentpass",
        "torrent_pass",
        "auth",
        "token",
    };

    private static readonly HashSet<string> WellKnownPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "announce",
        "announce.php",
        "scrape",
        "scrape.php",
        "tracker",
        "tracker.php",
        "a",
        "api",
        "v1",
        "v2",
        "torrents",
    };

    public static string Sanitize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return SanitizeQueryStringOnly(trimmed);
        }

        var scheme = uri.Scheme;
        var host = uri.Host;
        var port = uri.Port;
        var userInfo = uri.UserInfo;

        // Sanitize UserInfo
        string sanitizedUserInfo = null;
        if (!string.IsNullOrWhiteSpace(userInfo))
        {
            var userParts = userInfo.Split(':', 2);
            sanitizedUserInfo = userParts.Length == 2
                ? $"{userParts[0]}:{Mask}"
                : Mask;
        }

        // Sanitize Path
        var rawPath = uri.AbsolutePath;
        var sanitizedPath = rawPath;
        if (!string.IsNullOrEmpty(rawPath))
        {
            var segments = rawPath.Split('/');
            var previousWasMarker = false;
            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (string.IsNullOrEmpty(seg))
                {
                    continue;
                }

                if (previousWasMarker)
                {
                    segments[i] = Mask;
                    previousWasMarker = false;
                    continue;
                }

                if (SensitivePathMarkers.Contains(seg))
                {
                    previousWasMarker = true;
                    continue;
                }

                var segClean = Path.GetFileNameWithoutExtension(seg);
                if (WellKnownPathSegments.Contains(segClean))
                {
                    continue;
                }

                if (segClean.Length >= 12 && IsHexString(segClean))
                {
                    segments[i] = Mask;
                }
                else if (segClean.Length >= 16 && segClean.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    segments[i] = Mask;
                }
            }

            sanitizedPath = string.Join('/', segments);
        }

        // Sanitize Query
        var rawQuery = uri.Query;
        var sanitizedQuery = string.Empty;
        if (!string.IsNullOrEmpty(rawQuery))
        {
            var queryParams = rawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var sanitizedParams = new List<string>();
            foreach (var param in queryParams)
            {
                var parts = param.Split('=', 2);
                var key = parts[0];
                var val = parts.Length > 1 ? parts[1] : string.Empty;

                if (SensitiveQueryParams.Contains(key))
                {
                    sanitizedParams.Add($"{key}={Mask}");
                }
                else if (val.Length >= 16 && val.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    sanitizedParams.Add($"{key}={Mask}");
                }
                else
                {
                    sanitizedParams.Add(param);
                }
            }

            if (sanitizedParams.Count > 0)
            {
                sanitizedQuery = "?" + string.Join('&', sanitizedParams);
            }
        }

        // Reconstruct Authority
        var authority = host;
        if (!string.IsNullOrEmpty(sanitizedUserInfo))
        {
            authority = $"{sanitizedUserInfo}@{authority}";
        }

        if (port > 0 && (trimmed.Contains($":{port}") || !uri.IsDefaultPort))
        {
            authority = $"{authority}:{port}";
        }

        var fragment = uri.Fragment;
        return $"{scheme}://{authority}{sanitizedPath}{sanitizedQuery}{fragment}";
    }

    private static bool IsHexString(string s)
    {
        return s.Length > 0 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    private static string SanitizeQueryStringOnly(string url)
    {
        var regex = new Regex(@"([?&](?:passkey|authkey|torrentpass|torrent_pass|auth|token|pass|key)=)[^&#]+", RegexOptions.IgnoreCase);
        return regex.Replace(url, $"$1{Mask}");
    }
}
