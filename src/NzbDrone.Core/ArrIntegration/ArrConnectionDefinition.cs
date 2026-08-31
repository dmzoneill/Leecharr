// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ArrIntegration;

public class ArrConnectionDefinition : ModelBase
{
    public string Name { get; set; }

    public string Implementation { get; set; }

    public string ConfigContract { get; set; }

    public string Settings { get; set; }

    public bool Enable { get; set; } = true;

    public int Priority { get; set; } = 1;

    public string Url { get; set; }

    public string ApiKey { get; set; }

    public string ArrType { get; set; }

    public int SyncIntervalMinutes { get; set; } = 15;

    public bool SyncEnabled { get; set; } = true;

    public bool AutoEnrichMetadata { get; set; } = true;

    public bool SyncCategories { get; set; } = true;
}
