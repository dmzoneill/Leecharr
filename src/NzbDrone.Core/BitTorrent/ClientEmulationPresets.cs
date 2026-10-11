// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.BitTorrent;

public static class ClientEmulationPresets
{
    public const string DefaultClient = "qBittorrent";
    public const string DefaultUserAgent = "qBittorrent/4.6.5";
    public const string DefaultPeerIdPrefix = "-qB4650-";

    public static (string UserAgent, string PeerIdPrefix) GetPreset(string client)
    {
        return client?.ToLowerInvariant() switch
        {
            "qbittorrent" => ("qBittorrent/4.6.5", "-qB4650-"),
            "deluge" => ("Deluge/2.1.1", "-DE2110-"),
            "transmission" => ("Transmission/4.0.5", "-TR4050-"),
            "utorrent" => ("uTorrent/3550", "-UT3550-"),
            "biglybt" => ("BiglyBT/3.4.0.0", "-AZ3400-"),
            "leecharr" => ("Leecharr/1.0.0", "-LC1000-"),
            _ => (DefaultUserAgent, DefaultPeerIdPrefix),
        };
    }

    public static string CleanClientVersion(string peerIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(peerIdPrefix))
        {
            return "qB4650";
        }

        var cleaned = peerIdPrefix.Trim('-');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "qB4650";
        }

        return cleaned.Length > 6 ? cleaned.Substring(0, 6) : cleaned;
    }

    public static string GetUserAgentForPrefix(string peerIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(peerIdPrefix))
        {
            return DefaultUserAgent;
        }

        var cleaned = CleanClientVersion(peerIdPrefix);
        if (cleaned.Length < 6 || !AreAllDigits(cleaned, 2, 4))
        {
            return DefaultUserAgent;
        }

        var clientId = cleaned.Substring(0, 2);
        var major = cleaned[2];
        var minor = cleaned[3];
        var patch = cleaned[4];
        var build = cleaned[5];

        if (clientId.Equals("qB", StringComparison.OrdinalIgnoreCase))
        {
            return $"qBittorrent/{major}.{minor}.{patch}";
        }

        if (clientId.Equals("DE", StringComparison.OrdinalIgnoreCase))
        {
            return $"Deluge/{major}.{minor}.{patch}";
        }

        if (clientId.Equals("TR", StringComparison.OrdinalIgnoreCase))
        {
            return $"Transmission/{major}.{minor}.{patch}";
        }

        if (clientId.Equals("UT", StringComparison.OrdinalIgnoreCase))
        {
            return $"uTorrent/{major}{minor}{patch}{build}";
        }

        if (clientId.Equals("AZ", StringComparison.OrdinalIgnoreCase))
        {
            return $"BiglyBT/{major}.{minor}.{patch}.{build}";
        }

        if (clientId.Equals("LC", StringComparison.OrdinalIgnoreCase))
        {
            return $"Leecharr/{major}.{minor}.{patch}";
        }

        return DefaultUserAgent;
    }

    private static bool AreAllDigits(string value, int start, int count)
    {
        for (var i = start; i < start + count && i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static string CleanClientIdentifier(string peerIdPrefix)
    {
        var cleaned = CleanClientVersion(peerIdPrefix);
        return cleaned.Length >= 2 ? cleaned.Substring(0, 2) : "qB";
    }
}
