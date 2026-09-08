// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.DownloadClients;

public class DownloadClientResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    [Required]
    [StringLength(100)]
    public string ClientType { get; set; }

    [Required]
    [StringLength(512)]
    public string Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; }

    [StringLength(255)]
    public string Username { get; set; }

    [StringLength(255)]
    public string Password { get; set; }

    public bool UseSsl { get; set; }

    [StringLength(255)]
    public string Category { get; set; }

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

    [Range(1, int.MaxValue)]
    public int Priority { get; set; } = 1;
}

public class DownloadClientTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; }
}

public class DownloadClientRemoteItem
{
    public string Id { get; set; }

    public string InfoHash { get; set; }

    public string Name { get; set; }

    public long Size { get; set; }

    public double Progress { get; set; }

    public string State { get; set; }

    public string SavePath { get; set; }

    public string Category { get; set; }
}

public class ImportRequest
{
    [JsonPropertyName("infoHashes")]
    public List<string> InfoHashes { get; set; } = new();

    [JsonPropertyName("hashes")]
    public List<string> Hashes { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<string> EffectiveHashes =>
        (this.Hashes != null && this.Hashes.Count > 0)
            ? this.Hashes
            : (this.InfoHashes ?? Enumerable.Empty<string>());

    public string Category { get; set; }

    public string SavePath { get; set; }

    public bool StartPaused { get; set; }
}
