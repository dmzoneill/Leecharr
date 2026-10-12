// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Trackers;

public static class TrackerUrlSanitizer
{
    private const string Mask = "********";

    private static readonly char[] QueryDelimiters = { '&', ';' };

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
        "password",
        "apikey",
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
                else if (segClean.Length >= 12 && IsOpaqueSecretSegment(segClean))
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
            var sanitizedParams = SanitizeQueryParameters(rawQuery.TrimStart('?'), scheme);
            if (sanitizedParams.Count > 0)
            {
                sanitizedQuery = "?" + string.Join('&', sanitizedParams);
            }
        }

        // Reconstruct Authority
        var authority = FormatHost(host);
        if (!string.IsNullOrEmpty(sanitizedUserInfo))
        {
            authority = $"{sanitizedUserInfo}@{authority}";
        }

        if (port > 0 && !uri.IsDefaultPort)
        {
            authority = $"{authority}:{port}";
        }

        var fragment = uri.Fragment;
        if (!string.IsNullOrEmpty(fragment))
        {
            var sanitizedFragment = SanitizeQueryStringOnly(fragment.TrimStart('#'));
            fragment = string.IsNullOrEmpty(sanitizedFragment) ? string.Empty : "#" + sanitizedFragment;
        }

        return $"{scheme}://{authority}{sanitizedPath}{sanitizedQuery}{fragment}";
    }

    private static List<string> SanitizeQueryParameters(string query, string scheme)
    {
        var queryParams = query.Split(QueryDelimiters, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedParams = new List<string>();
        var isMagnet = scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase);

        foreach (var param in queryParams)
        {
            var parts = param.Split('=', 2);
            var rawKey = parts[0];
            var key = Uri.UnescapeDataString(rawKey);
            var val = parts.Length > 1 ? parts[1] : string.Empty;

            if (SensitiveQueryParams.Contains(key))
            {
                sanitizedParams.Add($"{rawKey}={Mask}");
                continue;
            }

            if (isMagnet && key.Equals("tr", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
            {
                var decodedTr = WebUtility.UrlDecode(val);
                sanitizedParams.Add($"{rawKey}={Sanitize(decodedTr)}");
                continue;
            }

            var sanitizedVal = SanitizeQueryParameterValue(val);
            sanitizedParams.Add(parts.Length > 1 ? $"{rawKey}={sanitizedVal}" : rawKey);
        }

        return sanitizedParams;
    }

    private static string SanitizeQueryParameterValue(string val)
    {
        if (string.IsNullOrEmpty(val))
        {
            return val;
        }

        var decoded = WebUtility.UrlDecode(val);
        if (!string.Equals(decoded, val, StringComparison.Ordinal) || decoded.Contains('&') || decoded.Contains(';'))
        {
            var embeddedSanitized = SanitizeQueryStringOnly(decoded);
            if (!string.Equals(embeddedSanitized, decoded, StringComparison.Ordinal))
            {
                return embeddedSanitized;
            }
        }

        if (decoded.Length >= 16 && IsOpaqueSecretSegment(decoded))
        {
            return Mask;
        }

        return val;
    }

    private static string FormatHost(string host)
    {
        if (IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{address}]";
        }

        return host;
    }

    private static bool IsHexString(string s)
    {
        return s.Length > 0 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    private static bool IsOpaqueSecretSegment(string s)
    {
        return s.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '+');
    }

    private static string SanitizeQueryStringOnly(string url)
    {
        var regex = new Regex(@"(^|[?&;])((?:passkey|authkey|torrentpass|torrent_pass|auth|token|pass|key|password|apikey)=)([^&#;]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        return regex.Replace(url, m => m.Groups[1].Value + m.Groups[2].Value + Mask);
    }
}
