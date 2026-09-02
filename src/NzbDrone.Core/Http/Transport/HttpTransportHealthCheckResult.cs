// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Http.Transport;

public class HttpTransportHealthCheckResult
{
    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();
}
