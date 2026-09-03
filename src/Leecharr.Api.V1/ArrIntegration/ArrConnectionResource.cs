// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.ArrIntegration;

public class ArrConnectionResource : RestResource
{
    public string Name { get; set; }

    public string ArrType { get; set; }

    public string Url { get; set; }

    public string ApiKey { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonPropertyName("enable")]
    public bool? Enable
    {
        get => this.Enabled;
        set
        {
            if (value.HasValue)
            {
                this.Enabled = value.Value;
            }
        }
    }

    public bool SyncCategories { get; set; } = true;

    public bool AutoTag { get; set; } = true;

    public int RefreshIntervalMinutes { get; set; } = 60;

    public DateTime? LastSync { get; set; }
}

public class ArrTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; }

    public string Version { get; set; }
}

public class SyncResultResource
{
    public bool Success { get; set; }

    public int SyncedCount { get; set; }

    public string Message { get; set; }
}
