// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace NzbDrone.Core.Http;

public static class HttpRetryAfterParser
{
    public static TimeSpan? FromResponse(HttpResponseMessage response)
    {
        if (response == null)
        {
            return null;
        }

        try
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    return NormalizeDelay(response.Headers.RetryAfter.Delta.Value);
                }

                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    var delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    return NormalizeDelay(delta);
                }
            }

            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                    {
                        return NormalizeDelay(TimeSpan.FromSeconds(seconds));
                    }

                    if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        var delta = date - DateTimeOffset.UtcNow;
                        return NormalizeDelay(delta);
                    }
                }
            }

            if (response.Headers.TryGetValues("X-Retry-After", out var xRetryValues))
            {
                var val = xRetryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(val) &&
                    double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                {
                    return NormalizeDelay(TimeSpan.FromSeconds(seconds));
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static TimeSpan? NormalizeDelay(TimeSpan delay)
    {
        return delay > TimeSpan.Zero ? delay : null;
    }
}
