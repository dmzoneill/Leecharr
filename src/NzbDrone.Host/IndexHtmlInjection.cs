// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Text.Json;

namespace NzbDrone.Host;

public static class IndexHtmlInjection
{
    public static string BuildHeadInjection(string effectiveUrlBase)
    {
        var urlBase = effectiveUrlBase ?? string.Empty;
        var baseHref = string.IsNullOrEmpty(urlBase)
            ? "/"
            : urlBase.EndsWith('/') ? urlBase : urlBase + "/";
        var encodedBaseHref = WebUtility.HtmlEncode(baseHref);
        var encodedUrlBaseJson = JsonSerializer.Serialize(urlBase);
        return $"<base href=\"{encodedBaseHref}\" /><script>window.Leecharr = {{ urlBase: {encodedUrlBaseJson} }};</script>";
    }

    public static string InjectIntoHtml(string html, string effectiveUrlBase)
    {
        var injection = BuildHeadInjection(effectiveUrlBase);
        if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("<head>", $"<head>{injection}", StringComparison.OrdinalIgnoreCase);
        }

        return injection + html;
    }
}
