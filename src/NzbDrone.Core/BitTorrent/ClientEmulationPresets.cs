// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.BitTorrent;

public static class ClientEmulationPresets
{
    public const string DefaultClient = "qBittorrent";
    public const string DefaultUserAgent = "qBittorrent/4.4.2";
    public const string DefaultPeerIdPrefix = "-qB4420-";

    public static (string UserAgent, string PeerIdPrefix) GetPreset(string client)
    {
        return client?.ToLowerInvariant() switch
        {
            "qbittorrent" => ("qBittorrent/4.4.2", "-qB4420-"),
            "deluge" => ("Deluge/2.0.5 libtorrent/1.2.14.0", "-DE2050-"),
            "transmission" => ("Transmission/3.00", "-TR3000-"),
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
            return "qB4420";
        }

        var cleaned = peerIdPrefix.Trim('-');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "qB4420";
        }

        return cleaned.Length > 6 ? cleaned.Substring(0, 6) : cleaned;
    }

    public static string CleanClientIdentifier(string peerIdPrefix)
    {
        var cleaned = CleanClientVersion(peerIdPrefix);
        return cleaned.Length >= 2 ? cleaned.Substring(0, 2) : "qB";
    }
}
