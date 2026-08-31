// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class SchedulerConfigResource : RestResource
{
    public bool SchedulerEnabled { get; set; }

    public int SchedulerStartHour { get; set; }

    public int SchedulerStartMinute { get; set; }

    public int SchedulerEndHour { get; set; }

    public int SchedulerEndMinute { get; set; }

    public bool SchedulerMonday { get; set; }

    public bool SchedulerTuesday { get; set; }

    public bool SchedulerWednesday { get; set; }

    public bool SchedulerThursday { get; set; }

    public bool SchedulerFriday { get; set; }

    public bool SchedulerSaturday { get; set; }

    public bool SchedulerSunday { get; set; }
}

public static class SchedulerConfigResourceMapper
{
    public static SchedulerConfigResource ToResource(IConfigService model)
    {
        return new SchedulerConfigResource
        {
            SchedulerEnabled = model.SchedulerEnabled,
            SchedulerStartHour = model.SchedulerStartHour,
            SchedulerStartMinute = model.SchedulerStartMinute,
            SchedulerEndHour = model.SchedulerEndHour,
            SchedulerEndMinute = model.SchedulerEndMinute,
            SchedulerMonday = model.SchedulerMonday,
            SchedulerTuesday = model.SchedulerTuesday,
            SchedulerWednesday = model.SchedulerWednesday,
            SchedulerThursday = model.SchedulerThursday,
            SchedulerFriday = model.SchedulerFriday,
            SchedulerSaturday = model.SchedulerSaturday,
            SchedulerSunday = model.SchedulerSunday,
        };
    }
}
