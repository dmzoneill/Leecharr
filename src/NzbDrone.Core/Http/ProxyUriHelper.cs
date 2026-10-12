using System;

namespace NzbDrone.Core.Http;

internal static class ProxyUriHelper
{
    internal static string FormatHostForProxyUri(string proxyHost)
    {
        if (string.IsNullOrEmpty(proxyHost))
        {
            return proxyHost;
        }

        if (proxyHost.StartsWith('[') && proxyHost.EndsWith(']'))
        {
            return proxyHost;
        }

        if (proxyHost.Contains(':') && !proxyHost.StartsWith('['))
        {
            return $"[{proxyHost}]";
        }

        return proxyHost;
    }

    internal static Uri BuildProxyUri(string scheme, string proxyHost, int proxyPort)
    {
        var formattedHost = FormatHostForProxyUri(proxyHost);
        return new Uri($"{scheme}://{formattedHost}:{proxyPort}");
    }
}
