// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace Leecharr.Api.V1.System;

// --- Event Bus Models ---

public class DeveloperEventItem
{
    public string Id { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public string EventName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string SourceNamespace { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;
}

public class DeveloperPublishEventRequest
{
    public string EventName { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";
}

public class DeveloperEventsResponse
{
    public List<DeveloperEventItem> Events { get; set; } = new();

    public int TotalRecorded { get; set; }
}

// --- Command Console Models ---

public class DeveloperCommandProperty
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsNullable { get; set; }

    public string DefaultValue { get; set; }
}

public class DeveloperCommandDescriptor
{
    public string Name { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<DeveloperCommandProperty> Properties { get; set; } = new();
}

public class DeveloperCommandExecuteRequest
{
    public string CommandName { get; set; } = string.Empty;

    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class DeveloperCommandHistoryItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime QueuedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public double DurationMs { get; set; }

    public string Message { get; set; }

    public int Trigger { get; set; }
}

public class DeveloperCommandsResponse
{
    public List<DeveloperCommandDescriptor> Commands { get; set; } = new();

    public List<DeveloperCommandHistoryItem> RecentHistory { get; set; } = new();
}

// --- Network / HTTP Traffic Models ---

public class DeveloperHttpTrafficItem
{
    public string Id { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public string Method { get; set; } = "GET";

    public string Url { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public double DurationMs { get; set; }

    public bool IsSuccess { get; set; }

    public string TransportEngine { get; set; } = "Default";

    public Dictionary<string, string> RequestHeaders { get; set; } = new();

    public Dictionary<string, string> ResponseHeaders { get; set; } = new();

    public string ResponseBodyPreview { get; set; } = string.Empty;

    public string ErrorMessage { get; set; }
}

public class DeveloperHttpTrafficResponse
{
    public List<DeveloperHttpTrafficItem> Items { get; set; } = new();

    public int TotalRecorded { get; set; }
}

// --- Arr Webhook Models ---

public class DeveloperWebhookTemplate
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = "Sonarr";

    public string EventType { get; set; } = "Grab";

    public string Description { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;
}

public class DeveloperWebhookHistoryItem
{
    public string Id { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public string Source { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string SourceIp { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string RawPayload { get; set; } = string.Empty;

    public string ResultMessage { get; set; } = string.Empty;

    public bool Success { get; set; }
}

public class DeveloperWebhookSimulateRequest
{
    public string EventType { get; set; } = "Grab";

    public string PayloadJson { get; set; } = "{}";
}

public class DeveloperWebhookSimulateResponse
{
    public bool Success { get; set; }

    public int StatusCode { get; set; }

    public string Message { get; set; } = string.Empty;

    public double ExecutionTimeMs { get; set; }

    public List<string> TraceLogs { get; set; } = new();
}

// --- Configuration & Host Environment Models ---

public class DeveloperConfigEntry
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Source { get; set; } = "Default";

    public bool IsSecret { get; set; }
}

public class DeveloperHostEnvironment
{
    public string OperatingSystem { get; set; } = string.Empty;

    public string OsArchitecture { get; set; } = string.Empty;

    public string ProcessArchitecture { get; set; } = string.Empty;

    public string FrameworkDescription { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public DateTime ProcessStartTimeUtc { get; set; }

    public double ProcessUptimeSeconds { get; set; }

    public long WorkingSetBytes { get; set; }

    public string AppDataDirectory { get; set; } = string.Empty;

    public string TempDirectory { get; set; } = string.Empty;

    public string CurrentDirectory { get; set; } = string.Empty;

    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
}

public class DeveloperConfigResponse
{
    public List<DeveloperConfigEntry> Entries { get; set; } = new();

    public DeveloperHostEnvironment Environment { get; set; } = new();
}
