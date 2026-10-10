// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Globalization;

namespace NzbDrone.Core.Bandwidth;

public static class SpeedScheduleTimeParser
{
    public static bool TryParse(string time, out TimeOnly result)
    {
        result = default;
        if (string.IsNullOrEmpty(time))
        {
            return false;
        }

        return TimeOnly.TryParse(time, CultureInfo.InvariantCulture, out result);
    }
}
