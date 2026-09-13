#nullable enable
using System.Collections.Generic;

namespace NzbDrone.Core.Automation;

public class AutomationExecutionResult
{
    public bool Success { get; set; }

    public string? OutputLog { get; set; }

    public string? Error { get; set; }

    public long ExecutionTimeMs { get; set; }

    public List<string> TagsToAdd { get; set; } = new();

    public List<string> TagsToRemove { get; set; } = new();

    public string? NewCategory { get; set; }

    public bool ShouldPause { get; set; }

    public bool ShouldResume { get; set; }

    public bool ShouldRemove { get; set; }

    public bool DeleteDataOnRemove { get; set; }

    public int? NewUploadLimitKbps { get; set; }

    public int? NewDownloadLimitKbps { get; set; }

    public bool ShouldRecheck { get; set; }

    public bool ShouldReannounce { get; set; }
}
