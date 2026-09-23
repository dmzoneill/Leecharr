// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace Leecharr.Api.V1.Blocklist;

public class BlocklistResource
{
    public bool Enabled { get; set; }

    public string Url { get; set; }

    public bool AutoUpdateEnabled { get; set; }

    public int AutoUpdateIntervalDays { get; set; }

    public int Ipv4RuleCount { get; set; }

    public int Ipv6RuleCount { get; set; }

    public int TotalRuleCount { get; set; }

    public int RuleCount { get; set; }

    public DateTime? LastUpdatedUtc { get; set; }

    public string LastSyncStatus { get; set; }

    public DateTime? NextScheduledSyncUtc { get; set; }

    public DateTime? NextAllowedSyncUtc { get; set; }
}

public class BlocklistConfigRequest
{
    public bool? Enabled { get; set; }

    public string Url { get; set; }

    public bool? AutoUpdateEnabled { get; set; }

    public int? AutoUpdateIntervalDays { get; set; }
}

public class BlocklistSyncResponse
{
    public bool Success { get; set; }

    public string Status { get; set; }

    public string Message { get; set; }

    public int RuleCount { get; set; }

    public int TotalRuleCount { get; set; }

    public int Ipv4RuleCount { get; set; }

    public int Ipv6RuleCount { get; set; }

    public DateTime? LastUpdatedUtc { get; set; }

    public DateTime? NextAllowedSyncUtc { get; set; }
}

public class BlocklistTestRequest
{
    public string Ip { get; set; }
}

public class BlocklistTestResponse
{
    public bool IsBlocked { get; set; }

    public string Rule { get; set; }
}
