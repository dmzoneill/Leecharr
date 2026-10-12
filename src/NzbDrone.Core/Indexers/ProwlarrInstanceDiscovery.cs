// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.Indexers;

public static class ProwlarrInstanceDiscovery
{
    public const int DefaultPort = 9696;

    public static bool IsSyncTarget(IndexerDefinition indexer)
    {
        if (indexer == null || string.IsNullOrWhiteSpace(indexer.Url) || string.IsNullOrWhiteSpace(indexer.ApiKey))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(indexer.Implementation)
            && indexer.Implementation.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(indexer.ConfigContract, "ProwlarrSettings", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return UsesDefaultProwlarrPort(indexer.Url);
    }

    public static bool UsesDefaultProwlarrPort(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Port == DefaultPort;
    }
}
