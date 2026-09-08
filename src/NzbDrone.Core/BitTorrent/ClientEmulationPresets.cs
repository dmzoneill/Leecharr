// Copyright (c) PlaceholderCompany. All rights reserved.

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

        if (peerIdPrefix.StartsWith("-qB", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultUserAgent;
        }

        if (peerIdPrefix.StartsWith("-DE", StringComparison.OrdinalIgnoreCase))
        {
            return "Deluge/2.1.1";
        }

        if (peerIdPrefix.StartsWith("-TR", StringComparison.OrdinalIgnoreCase))
        {
            return "Transmission/4.0.5";
        }

        if (peerIdPrefix.StartsWith("-UT", StringComparison.OrdinalIgnoreCase))
        {
            return "uTorrent/3550";
        }

        if (peerIdPrefix.StartsWith("-AZ", StringComparison.OrdinalIgnoreCase))
        {
            return "BiglyBT/3.4.0.0";
        }

        if (peerIdPrefix.StartsWith("-LC", StringComparison.OrdinalIgnoreCase))
        {
            return "Leecharr/1.0.0";
        }

        return DefaultUserAgent;
    }

    public static string CleanClientIdentifier(string peerIdPrefix)
    {
        var cleaned = CleanClientVersion(peerIdPrefix);
        return cleaned.Length >= 2 ? cleaned.Substring(0, 2) : "qB";
    }
}
