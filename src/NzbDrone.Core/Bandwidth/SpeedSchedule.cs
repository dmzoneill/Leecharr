// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Bandwidth;

public class SpeedSchedule : ModelBase
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
