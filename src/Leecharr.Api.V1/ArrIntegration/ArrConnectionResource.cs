using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.ArrIntegration;

public class ArrConnectionResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    [Required]
    [StringLength(100)]
    public string ArrType { get; set; }

    [Required]
    [StringLength(2048)]
    public string Url { get; set; }

    [JsonPropertyName("externalUrl")]
    [StringLength(2048)]
    public string ExternalUrl { get; set; }

    [JsonPropertyName("publicUrl")]
    [StringLength(2048)]
    public string PublicUrl
    {
        get => this.ExternalUrl;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                this.ExternalUrl = value;
            }
        }
    }

    [StringLength(1024)]
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

    [Range(1, int.MaxValue)]
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

    public int TotalCount { get; set; }

    public int FailedCount { get; set; }

    public int Added { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string Message { get; set; }
}
