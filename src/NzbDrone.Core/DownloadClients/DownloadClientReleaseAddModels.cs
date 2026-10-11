// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientReleaseAddRequest
{
    public string MagnetUrl { get; set; }

    public string TorrentUrl { get; set; }

    public byte[] TorrentBytes { get; set; }

    public string Category { get; set; }

    public string SavePath { get; set; }

    public bool StartPaused { get; set; }
}

public class DownloadClientReleaseAddResult
{
    public bool Success { get; set; }

    public string InfoHash { get; set; }

    public string ErrorMessage { get; set; }

    public static DownloadClientReleaseAddResult Succeeded(string infoHash) => new() { Success = true, InfoHash = infoHash };

    public static DownloadClientReleaseAddResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}
