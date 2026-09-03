// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Telemetry;

public class SubsystemTelemetryReport
{
    public string SubsystemId { get; set; } = string.Empty;

    public string SubsystemName { get; set; } = string.Empty;

    public string ActiveProvider { get; set; } = string.Empty;

    public string Status { get; set; } = "Healthy";

    public string ResourceLoad { get; set; } = "Nominal";

    public Dictionary<string, object> Metrics { get; set; } = new();
}
