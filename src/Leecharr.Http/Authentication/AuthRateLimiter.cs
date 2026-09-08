// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Leecharr.Http.Authentication;

public class AuthRateLimiter : IDisposable
{
    private const int DefaultMaxFailedAttempts = 5;
    private const int DefaultMaxCapacity = 10000;
    private static readonly TimeSpan DefaultAttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultLockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Failures, DateTime WindowStart, DateTime? LockoutUntil)> attempts = new();
    private readonly int maxFailedAttempts;
    private readonly int maxCapacity;
    private readonly TimeSpan attemptWindow;
    private readonly TimeSpan lockoutDuration;
    private readonly Timer cleanupTimer;
    private DateTime lastSweepUtc = DateTime.UtcNow;

    public static AuthRateLimiter Shared { get; } = new();

    public AuthRateLimiter(
        int maxFailedAttempts = DefaultMaxFailedAttempts,
        TimeSpan? attemptWindow = null,
        TimeSpan? lockoutDuration = null,
        int maxCapacity = DefaultMaxCapacity,
        TimeSpan? cleanupInterval = null)
    {
        this.maxFailedAttempts = maxFailedAttempts;
        this.attemptWindow = attemptWindow ?? DefaultAttemptWindow;
        this.lockoutDuration = lockoutDuration ?? DefaultLockoutDuration;
        this.maxCapacity = maxCapacity > 0 ? maxCapacity : DefaultMaxCapacity;

        var interval = cleanupInterval ?? TimeSpan.FromMinutes(5);
        if (interval > TimeSpan.Zero)
        {
            this.cleanupTimer = new Timer(
                _ =>
                {
                    try
                    {
                        this.SweepExpired();
                    }
                    catch
                    {
                        // Suppress timer exceptions to prevent process crash
                    }
                },
                null,
                interval,
                interval);
        }
    }

    public int TrackedIpCount => this.attempts.Count;

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

        if (this.attempts.Count >= this.maxCapacity || (now - this.lastSweepUtc) > TimeSpan.FromMinutes(5))
        {
            this.lastSweepUtc = now;
            this.SweepExpired();
        }

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

    public void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in this.attempts)
        {
            var record = kvp.Value;
            if (record.LockoutUntil.HasValue)
            {
                if (now >= record.LockoutUntil.Value)
                {
                    this.attempts.TryRemove(kvp.Key, out _);
                }
            }
            else if (now - record.WindowStart > this.attemptWindow)
            {
                this.attempts.TryRemove(kvp.Key, out _);
            }
        }

        if (this.attempts.Count > this.maxCapacity)
        {
            var excess = this.attempts.Count - this.maxCapacity;
            var oldestKeys = this.attempts
                .OrderBy(kvp => kvp.Value.WindowStart)
                .Take(excess)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                this.attempts.TryRemove(key, out _);
            }
        }
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

    public void Dispose()
    {
        this.cleanupTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
