// Copyright (c) FeedItOut. All rights reserved.

namespace Leecharr.Http.Security;

public static class JsonpCallbackHelper
{
    public static bool IsValidCallbackName(string callback)
    {
        if (string.IsNullOrWhiteSpace(callback))
        {
            return false;
        }

        var start = 0;
        while (start < callback.Length)
        {
            var dotIndex = callback.IndexOf('.', start);
            var segmentLength = dotIndex < 0 ? callback.Length - start : dotIndex - start;
            if (segmentLength == 0 || !IsValidIdentifierSegment(callback, start, segmentLength))
            {
                return false;
            }

            if (dotIndex < 0)
            {
                return true;
            }

            start = dotIndex + 1;
        }

        return false;
    }

    private static bool IsValidIdentifierSegment(string value, int offset, int length)
    {
        if (length == 0)
        {
            return false;
        }

        if (!IsIdentifierStart(value[offset]))
        {
            return false;
        }

        for (var i = 1; i < length; i++)
        {
            if (!IsIdentifierPart(value[offset + i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char c) =>
        char.IsAsciiLetter(c) || c == '_' || c == '$';

    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c) || char.IsAsciiDigit(c);
}
