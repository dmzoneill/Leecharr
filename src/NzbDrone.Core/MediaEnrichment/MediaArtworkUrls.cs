// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;

namespace NzbDrone.Core.MediaEnrichment;

public static class MediaArtworkUrls
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    public static string FindCachedFile(string appDataFolder, int torrentId, string artworkType)
    {
        if (string.IsNullOrWhiteSpace(appDataFolder) || torrentId <= 0 || string.IsNullOrWhiteSpace(artworkType))
        {
            return null;
        }

        var cacheDir = Path.Combine(appDataFolder, "MediaCache", torrentId.ToString());
        if (!Directory.Exists(cacheDir))
        {
            return null;
        }

        foreach (var ext in ImageExtensions)
        {
            var candidate = Path.Combine(cacheDir, artworkType + ext);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string ToPublicUrl(int torrentId, string localPath, string remoteUrl, string artworkType)
    {
        if (torrentId > 0 && !string.IsNullOrEmpty(localPath))
        {
            return $"/api/v1/media/artwork/{torrentId}/{artworkType}";
        }

        return RedactApiKeyQuery(remoteUrl);
    }

    public static string RedactApiKeyQuery(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !url.Contains("apikey=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (string.IsNullOrEmpty(uri.Query))
        {
            return url;
        }

        var filtered = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("apikey=", StringComparison.OrdinalIgnoreCase));

        var newQuery = string.Join("&", filtered);
        var builder = new UriBuilder(uri)
        {
            Query = newQuery,
        };

        return builder.Uri.ToString();
    }
}
