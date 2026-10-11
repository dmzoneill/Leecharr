// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;

namespace NzbDrone.Core.MediaEnrichment;

public static class MediaArtworkUrls
{
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
