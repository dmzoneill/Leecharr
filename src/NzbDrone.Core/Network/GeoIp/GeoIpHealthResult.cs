// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Network.GeoIp;

public class GeoIpHealthResult
{
    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; }

    public List<string> Warnings { get; set; } = new();
}
