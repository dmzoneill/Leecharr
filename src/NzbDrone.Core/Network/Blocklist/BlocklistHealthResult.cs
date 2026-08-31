// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Network.Blocklist;

public class BlocklistHealthResult
{
    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; }

    public int LoadedRuleCount { get; set; }

    public List<string> Warnings { get; set; } = new();
}
