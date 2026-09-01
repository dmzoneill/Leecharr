using System.Collections.Generic;

namespace NzbDrone.Core.BitTorrent;

public class EngineHealthCheckResult
{
    public bool IsHealthy { get; set; }
    public string StatusMessage { get; set; }
    public List<string> DependencyChecks { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
