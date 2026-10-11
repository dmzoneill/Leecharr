// Copyright (c) FeedItOut. All rights reserved.

using System;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

public static class DownloadClientRemoteImportHelper
{
    public static bool TryBuildMagnetUri(string hash, string displayName, out string magnetUri, out string error)
    {
        magnetUri = null;
        error = null;
        if (string.IsNullOrWhiteSpace(hash))
        {
            error = "Info hash is required.";
            return false;
        }

        try
        {
            magnetUri = MagnetLinkParser.BuildMagnetUri(hash, displayName);
            MagnetLinkParser.Parse(magnetUri);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ShouldStartPaused(string remoteState)
    {
        if (string.IsNullOrWhiteSpace(remoteState))
        {
            return false;
        }

        var state = remoteState.Trim();
        if (state.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (state.Contains("paused", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("queued", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (state.Equals("Paused", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Queued", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (state.Equals("stopped", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    internal static string MapTransmissionStatusToState(int status, double progress)
    {
        return status switch
        {
            0 => progress >= 1.0 ? "pausedUP" : "pausedDL",
            1 or 2 => progress >= 1.0 ? "checkingUP" : "checkingDL",
            3 => "queuedDL",
            5 => "queuedUP",
            4 => "downloading",
            6 => "uploading",
            _ => "unknown",
        };
    }
}
