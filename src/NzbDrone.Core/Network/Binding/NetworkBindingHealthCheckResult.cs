// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Network.Binding;

public class NetworkBindingHealthCheckResult
{
    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();
}
