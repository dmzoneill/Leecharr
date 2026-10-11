// Copyright (c) FeedItOut. All rights reserved.
using System;

namespace Leecharr.Api.V1.System;

internal static class SystemRunDurationFormatter
{
    /// <summary>
    /// Formats a run duration as HH:mm:ss using total elapsed hours (not modulo 24).
    /// Negative spans keep a leading minus sign.
    /// </summary>
    public static string Format(TimeSpan duration)
    {
        var negative = duration < TimeSpan.Zero;
        var magnitude = negative ? duration.Negate() : duration;
        var hours = (int)magnitude.TotalHours;
        var prefix = negative ? "-" : string.Empty;
        return $"{prefix}{hours:D2}:{magnitude.Minutes:D2}:{magnitude.Seconds:D2}";
    }

    public static string Format(DateTime startedAt, DateTime endedAt)
    {
        return Format(endedAt - startedAt);
    }
}
