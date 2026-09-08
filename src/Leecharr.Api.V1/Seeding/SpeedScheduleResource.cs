// Copyright (c) PlaceholderCompany. All rights reserved.

using System.ComponentModel.DataAnnotations;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Seeding;

public class SpeedScheduleResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    [Range(1, 127)]
    public int Days { get; set; } = 127;

    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9])?$", ErrorMessage = "StartTime must be a valid time (HH:mm or HH:mm:ss)")]
    public string StartTime { get; set; } = "00:00:00";

    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9])?$", ErrorMessage = "EndTime must be a valid time (HH:mm or HH:mm:ss)")]
    public string EndTime { get; set; } = "23:59:59";

    [Range(0, int.MaxValue)]
    public int MaxDownloadSpeed { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxUploadSpeed { get; set; }

    public bool IsEnabled { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int Priority { get; set; }
}

public class SpeedLimitsResource
{
    public int MaxDownloadSpeedKbps { get; set; }

    public int MaxUploadSpeedKbps { get; set; }

    public bool IsThrottled { get; set; }

    public bool IsPaused { get; set; }

    public bool IsDownloadPaused { get; set; }

    public bool IsUploadPaused { get; set; }
}
