// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Leecharr.Http.Security;

public static class NzbVortexAuthenticationHelper
{
    private static readonly ConcurrentDictionary<string, DateTime> PendingNonces = new();
    private static readonly TimeSpan NonceLifetime = TimeSpan.FromSeconds(30);

    public static string IssueNonce()
    {
        PruneExpiredNonces();
        var nonce = Guid.NewGuid().ToString("D").ToUpperInvariant();
        PendingNonces[nonce] = DateTime.UtcNow.Add(NonceLifetime);
        return nonce;
    }

    public static bool TryValidateLogin(string nonce, string cnonce, string hash, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(nonce)
            || string.IsNullOrWhiteSpace(cnonce)
            || string.IsNullOrWhiteSpace(hash)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        if (!PendingNonces.TryGetValue(nonce, out var expiry) || expiry <= DateTime.UtcNow)
        {
            PendingNonces.TryRemove(nonce, out _);
            return false;
        }

        var expected = ComputeLoginHash(nonce, cnonce, apiKey);
        if (!HashesMatch(hash, expected))
        {
            return false;
        }

        PendingNonces.TryRemove(nonce, out _);
        return true;
    }

    public static string ComputeLoginHash(string nonce, string cnonce, string apiKey)
    {
        var compiled = string.Concat(nonce, ":", cnonce, ":", apiKey);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(compiled));
        return Convert.ToBase64String(hashBytes);
    }

    private static bool HashesMatch(string provided, string expected)
    {
        if (RpcAuthenticationHelper.FixedTimeEquals(provided, expected))
        {
            return true;
        }

        var normalizedProvided = NormalizeBase64(provided);
        var normalizedExpected = NormalizeBase64(expected);
        return RpcAuthenticationHelper.FixedTimeEquals(normalizedProvided, normalizedExpected);
    }

    private static string NormalizeBase64(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        var padding = trimmed.Length % 4;
        if (padding == 0)
        {
            return trimmed;
        }

        return trimmed + new string('=', 4 - padding);
    }

    private static void PruneExpiredNonces()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in PendingNonces)
        {
            if (entry.Value <= now)
            {
                PendingNonces.TryRemove(entry.Key, out _);
            }
        }
    }
}
