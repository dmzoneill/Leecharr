// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.BitTorrent.Creation;

public class TorrentCreationRequest
{
    public string Path { get; set; }

    public string Name { get; set; }

    public string Comment { get; set; }

    public string CreatedBy { get; set; }

    public bool IsPrivate { get; set; }

    public int PieceLength { get; set; }

    public List<string> Trackers { get; set; } = new();

    public List<string> WebSeeds { get; set; } = new();

    public string OutputPath { get; set; }
}
