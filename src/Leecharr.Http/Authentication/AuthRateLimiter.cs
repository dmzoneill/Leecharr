// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;

namespace Leecharr.Http.Authentication;

public class AuthRateLimiter
{
    private const int DefaultMaxFailedAttempts = 5;
    private static readonly TimeSpan DefaultAttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultLockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Failures, DateTime WindowStart, DateTime? LockoutUntil)> attempts = new();
    private readonly int maxFailedAttempts;
    private readonly TimeSpan attemptWindow;
    private readonly TimeSpan lockoutDuration;

    public static AuthRateLimiter Shared { get; } = new();

    public AuthRateLimiter(int maxFailedAttempts = DefaultMaxFailedAttempts, TimeSpan? attemptWindow = null, TimeSpan? lockoutDuration = null)
    {
        this.maxFailedAttempts = maxFailedAttempts;
        this.attemptWindow = attemptWindow ?? DefaultAttemptWindow;
        this.lockoutDuration = lockoutDuration ?? DefaultLockoutDuration;
    }

    public bool IsThrottled(string clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (!this.attempts.TryGetValue(clientIp, out var record))
        {
            return false;
        }

        if (record.LockoutUntil.HasValue)
        {
            if (now < record.LockoutUntil.Value)
            {
                return true;
            }

            this.attempts.TryRemove(clientIp, out _);
            return false;
        }

        if (now - record.WindowStart > this.attemptWindow)
        {
            this.attempts.TryRemove(clientIp, out _);
            return false;
        }

        return record.Failures >= this.maxFailedAttempts;
    }

    public void RecordFailure(string clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return;
        }

        var now = DateTime.UtcNow;
        this.attempts.AddOrUpdate(
            clientIp,
            _ => (1, now, null),
            (_, existing) =>
            {
                if (now - existing.WindowStart > this.attemptWindow)
                {
                    return (1, now, null);
                }

                var newFailures = existing.Failures + 1;
                var lockout = newFailures >= this.maxFailedAttempts ? (DateTime?)now.Add(this.lockoutDuration) : null;
                return (newFailures, existing.WindowStart, lockout);
            });
    }

    public void Reset(string clientIp)
    {
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            this.attempts.TryRemove(clientIp, out _);
        }
    }

    public void ResetAll()
    {
        this.attempts.Clear();
    }
}
