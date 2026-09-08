// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Notifications;

public class NotificationResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    [StringLength(100)]
    public string Implementation { get; set; } = "Webhook";

    [StringLength(100)]
    public string ConfigContract { get; set; }

    public string Settings { get; set; }

    public bool Enable { get; set; } = true;

    public bool OnGrab { get; set; } = true;

    public bool OnDownloadComplete { get; set; } = true;

    public bool OnMediaInspected { get; set; }

    public bool OnExtractComplete { get; set; }

    public bool OnSeedGoalReached { get; set; } = true;

    public bool OnTorrentDeleted { get; set; }

    public bool OnHealthIssue { get; set; } = true;

    public bool OnHealthRestored { get; set; } = true;

    public bool OnManualInteractionRequired { get; set; } = true;

    public bool OnApplicationUpdate { get; set; }

    public List<int> Tags { get; set; } = new();
}

public class NotificationTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; }
}
