// Copyright (c) FeedItOut. All rights reserved.

using System.Security;

namespace NzbDrone.Core.Ai;

internal static class AiUntrustedPromptXml
{
    public static string Element(string tagName, string value)
    {
        return $"<{tagName}>{Escape(value)}</{tagName}>";
    }

    public static string Escape(string value)
    {
        return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }
}
