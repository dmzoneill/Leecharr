// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.BitTorrent.Creation;

public class TorrentCreationResult
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; }

    public string OutputPath { get; set; }

    public byte[] TorrentFileBytes { get; set; }

    public string InfoHash { get; set; }

    public long TotalSize { get; set; }

    public int PieceCount { get; set; }

    public int PieceLength { get; set; }
}
