// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public static class ApplicationPublicUrlResolver
{
    public static bool TryResolvePublicBaseUrl(IConfigFileProvider configFileProvider, out string baseUrl)
    {
        baseUrl = string.Empty;
        var urlBase = NormalizeUrlBase(configFileProvider?.UrlBase);

        var envUrl = Environment.GetEnvironmentVariable("LEECHARR_URL") ??
                     Environment.GetEnvironmentVariable("SEEDARR_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            baseUrl = AppendUrlBase(envUrl.Trim(), urlBase).TrimEnd('/');
            return IsValidAbsoluteHttpUrl(baseUrl);
        }

        var enableSsl = configFileProvider?.EnableSsl == true;
        var scheme = enableSsl ? "https" : "http";
        var port = enableSsl
            ? (configFileProvider?.SslPort > 0 ? configFileProvider.SslPort : 7890)
            : (configFileProvider?.Port > 0 ? configFileProvider.Port : 7889);

        var envHost = Environment.GetEnvironmentVariable("LEECHARR_HOST") ??
                      Environment.GetEnvironmentVariable("SEEDARR_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            var trimmedHost = envHost.Trim();
            if (trimmedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = AppendUrlBase(trimmedHost, urlBase).TrimEnd('/');
            }
            else
            {
                baseUrl = AppendUrlBase($"{scheme}://{FormatHostWithPort(trimmedHost, port)}", urlBase).TrimEnd('/');
            }

            return IsValidAbsoluteHttpUrl(baseUrl);
        }

        var bindAddress = ResolvePublicHost(configFileProvider?.BindAddress);
        baseUrl = AppendUrlBase($"{scheme}://{FormatHostWithPort(bindAddress, port)}", urlBase).TrimEnd('/');
        return IsValidAbsoluteHttpUrl(baseUrl);
    }

    private static string ResolvePublicHost(string bindAddress)
    {
        var trimmed = bindAddress?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed is "*" or "0.0.0.0" or "::" or "+")
        {
            return "127.0.0.1";
        }

        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        if (IsHexContainerId(trimmed))
        {
            return "leecharr.local";
        }

        return trimmed;
    }

    private static string AppendUrlBase(string baseUrl, string urlBase)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmedBase = baseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(urlBase))
        {
            return trimmedBase;
        }

        if (trimmedBase.EndsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedBase;
        }

        return $"{trimmedBase}{urlBase}";
    }

    private static string FormatHostWithPort(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var trimmedHost = FormatHostAuthority(host.Trim().TrimEnd('/'));

        var hasPort = trimmedHost.StartsWith('[') && trimmedHost.Contains(']')
            ? trimmedHost[(trimmedHost.IndexOf(']') + 1)..].Contains(':')
            : trimmedHost.Contains(':');

        if (hasPort || port is 80 or 443)
        {
            return trimmedHost;
        }

        return $"{trimmedHost}:{port}";
    }

    private static string FormatHostAuthority(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var trimmed = host.Trim();
        if (trimmed.StartsWith('[') && trimmed.Contains(']'))
        {
            return trimmed;
        }

        if (IPAddress.TryParse(trimmed, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{trimmed}]";
        }

        return trimmed;
    }

    private static string NormalizeUrlBase(string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return string.Empty;
        }

        var trimmed = urlBase.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : "/" + trimmed;
    }

    private static bool IsValidAbsoluteHttpUrl(string url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
               (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsHexContainerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length != 12 && value.Length != 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
