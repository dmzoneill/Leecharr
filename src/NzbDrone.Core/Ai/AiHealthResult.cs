using System.Collections.Generic;

namespace NzbDrone.Core.Ai;

public class AiHealthResult
{
    public bool IsHealthy { get; set; }
    public string StatusMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    public long LatencyMs { get; set; }
    public string ModelName { get; set; }
    public string Version { get; set; }
}
