#nullable enable

using System;
using System.Diagnostics;
using System.Threading;

namespace NzbDrone.Core.Automation;

/// <summary>
/// Tracks remaining wall time for a script run so blocking CLR calls stay inside the Jint timeout.
/// </summary>
public sealed class ScriptExecutionBudget
{
    private const int SafetyMarginMs = 50;

    private readonly TimeSpan _limit;
    private readonly Stopwatch _stopwatch;

    public ScriptExecutionBudget(TimeSpan limit)
    {
        _limit = limit;
        _stopwatch = Stopwatch.StartNew();
    }

    public TimeSpan Remaining
    {
        get
        {
            var remaining = _limit - _stopwatch.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public int GetRemainingMillisecondsForBlocking()
    {
        var ms = (int)Remaining.TotalMilliseconds - SafetyMarginMs;
        return ms > 0 ? ms : 0;
    }

    public int CapTimeoutMilliseconds(int requestedSeconds)
    {
        var requestedMs = requestedSeconds * 1000;
        var remainingMs = GetRemainingMillisecondsForBlocking();
        if (remainingMs <= 0)
        {
            return 0;
        }

        return Math.Min(requestedMs, remainingMs);
    }

    public void SleepMilliseconds(int requestedMilliseconds)
    {
        if (requestedMilliseconds <= 0)
        {
            return;
        }

        var sleepMs = Math.Min(requestedMilliseconds, GetRemainingMillisecondsForBlocking());
        if (sleepMs > 0)
        {
            Thread.Sleep(sleepMs);
        }
    }

    public void SleepSeconds(int requestedSeconds)
    {
        if (requestedSeconds <= 0)
        {
            return;
        }

        SleepMilliseconds(requestedSeconds * 1000);
    }
}
