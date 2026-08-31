// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace Leecharr.Api.V1.Subsystems;

public class SubsystemOverviewResource
{
    public string Id { get; set; }

    public string Name { get; set; }

    public string Category { get; set; }

    public string Description { get; set; }

    public string ActiveProviderId { get; set; }

    public List<SubsystemProviderResource> Providers { get; set; } = new();
}

public class SubsystemProviderResource
{
    public string ProviderId { get; set; }

    public string DisplayName { get; set; }

    public string Version { get; set; }

    public string Description { get; set; }

    public bool IsActive { get; set; }

    public bool IsAvailable { get; set; }

    public string Status { get; set; }

    public Dictionary<string, object> Capabilities { get; set; } = new();
}

public class SwitchSubsystemProviderRequest
{
    public string ProviderId { get; set; }
}

public class SwitchSubsystemProviderResult
{
    public bool Success { get; set; }

    public string SubsystemId { get; set; }

    public string PreviousProvider { get; set; }

    public string ActiveProvider { get; set; }

    public string Message { get; set; }

    public string Error { get; set; }
}

public class SubsystemProbeResult
{
    public string SubsystemId { get; set; }

    public string ProviderId { get; set; }

    public bool IsHealthy { get; set; }

    public string StatusMessage { get; set; }

    public List<string> DependencyChecks { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}
