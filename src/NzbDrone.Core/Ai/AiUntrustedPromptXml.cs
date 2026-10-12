// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Security;

namespace NzbDrone.Core.Ai;

internal static class AiUntrustedPromptXml
{
    public static string Element(string tagName, string value)
    {
        var safeValue = value ?? string.Empty;
        if (safeValue.Contains("]]>", StringComparison.Ordinal))
        {
            return $"<{tagName}>{Escape(safeValue)}</{tagName}>";
        }

        return $"<{tagName}><![CDATA[{safeValue}]]></{tagName}>";
    }

    public static string Escape(string value)
    {
        return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }
}
