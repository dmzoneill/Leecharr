// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Leecharr.Http.Security;

public class RpcSessionStore
{
    private readonly ConcurrentDictionary<string, DateTime> sessions = new();
    private readonly int maxCapacity;
    private readonly object pruneLock = new();

    public RpcSessionStore(int maxCapacity = 10000)
    {
        this.maxCapacity = Math.Max(10, maxCapacity);
    }

    public int Count => this.sessions.Count;

    public bool IsValid(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (this.sessions.TryGetValue(token, out var expiry))
        {
            if (expiry > DateTime.UtcNow)
            {
                return true;
            }

            this.sessions.TryRemove(token, out _);
        }

        return false;
    }

    public void SetSession(string token, DateTime expiry)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        this.PruneExpired();

        if (this.sessions.Count >= this.maxCapacity)
        {
            lock (this.pruneLock)
            {
                if (this.sessions.Count >= this.maxCapacity)
                {
                    var excess = this.sessions.Count - this.maxCapacity + 1;
                    var oldestKeys = this.sessions
                        .OrderBy(kvp => kvp.Value)
                        .Take(excess)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in oldestKeys)
                    {
                        this.sessions.TryRemove(key, out _);
                    }
                }
            }
        }

        this.sessions[token] = expiry;
    }

    public void SetSession(string token, TimeSpan lifetime)
    {
        this.SetSession(token, DateTime.UtcNow.Add(lifetime));
    }

    public bool RemoveSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return this.sessions.TryRemove(token, out _);
    }

    public bool TryGetValue(string token, out DateTime expiry)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            expiry = default;
            return false;
        }

        if (this.sessions.TryGetValue(token, out expiry))
        {
            if (expiry > DateTime.UtcNow)
            {
                return true;
            }

            this.sessions.TryRemove(token, out _);
        }

        expiry = default;
        return false;
    }

    public void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in this.sessions)
        {
            if (kvp.Value <= now)
            {
                this.sessions.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Clear()
    {
        this.sessions.Clear();
    }
}
