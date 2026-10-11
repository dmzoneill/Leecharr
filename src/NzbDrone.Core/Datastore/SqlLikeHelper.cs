// Copyright (c) FeedItOut. All rights reserved.

using System.Text;

namespace NzbDrone.Core.Datastore;

public static class SqlLikeHelper
{
    public const char EscapeCharacter = '\\';

    public static string EscapeLikeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (var character in value)
        {
            if (character == '%' || character == '_' || character == EscapeCharacter)
            {
                builder.Append(EscapeCharacter);
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string BuildContainsPattern(string query, bool lowercaseQuery)
    {
        var normalized = query.Trim();
        if (lowercaseQuery)
        {
            normalized = normalized.ToLowerInvariant();
        }

        return $"%{EscapeLikeValue(normalized)}%";
    }
}
