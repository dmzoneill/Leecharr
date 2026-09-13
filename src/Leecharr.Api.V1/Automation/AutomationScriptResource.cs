#nullable enable
using System;
using System.Collections.Generic;
using Leecharr.Http.REST;
using NzbDrone.Core.Automation;

namespace Leecharr.Api.V1.Automation;

public class AutomationScriptResource : RestResource
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

public class AutomationTestRequestResource
{
    public AutomationScriptResource Script { get; set; } = new();

    public int? TorrentId { get; set; }

    public Dictionary<string, object>? CustomInputs { get; set; }
}

public class InstallMarketplaceTemplateRequest
{
    public string TemplateId { get; set; } = string.Empty;

    public string? CustomName { get; set; }

    public Dictionary<string, string>? CustomInputs { get; set; }
}
