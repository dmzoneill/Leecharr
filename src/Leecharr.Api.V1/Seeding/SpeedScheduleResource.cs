// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Seeding;

public class SpeedScheduleResource : RestResource
{
    public string Name { get; set; }

    public int Days { get; set; } = 127;

    public string StartTime { get; set; } = "00:00:00";

    public string EndTime { get; set; } = "23:59:59";

    public int MaxDownloadSpeed { get; set; }

    public int MaxUploadSpeed { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int Priority { get; set; }
}

public class SpeedLimitsResource
{
    public int MaxDownloadSpeedKbps { get; set; }

    public int MaxUploadSpeedKbps { get; set; }

    public bool IsThrottled { get; set; }

    public bool IsPaused { get; set; }
}
