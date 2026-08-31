// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.BitTorrent;

public class EngineSwitchResult
{
    public bool Success { get; set; }

    public string PreviousEngine { get; set; }

    public string ActiveEngine { get; set; }

    public int TorrentsMigrated { get; set; }

    public string Message { get; set; }

    public string Error { get; set; }
}
