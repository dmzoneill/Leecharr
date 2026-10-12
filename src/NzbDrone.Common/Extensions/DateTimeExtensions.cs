// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Common.Extensions;

public static class DateTimeExtensions
{
    /// <summary>
    /// Converts a database-backed <see cref="DateTime"/> to Unix seconds, treating <see cref="DateTimeKind.Unspecified"/> as UTC.
    /// </summary>
    public static long ToUnixTimeSecondsAsUtc(this DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
        };

        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
    }
}
