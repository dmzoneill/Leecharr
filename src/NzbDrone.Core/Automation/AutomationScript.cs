#nullable enable
using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Automation;

public class AutomationScript : ModelBase
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.TorrentAdded;

    public AutomationLanguage Language { get; set; } = AutomationLanguage.JavaScript;

    public string Code { get; set; } = string.Empty;

    public string? InputsJson { get; set; }

    public bool IsEnabled { get; set; } = true;

    public List<string> TargetCategories { get; set; } = new();

    public List<int> TargetTagIds { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastExecutedAt { get; set; }

    public string? LastExecutionStatus { get; set; }

    public string? LastExecutionLog { get; set; }
}
