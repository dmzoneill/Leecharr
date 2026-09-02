// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public class IndexerDefinition : ModelBase
{
    public string Name { get; set; }

    public string Implementation { get; set; } = "Torznab";

    public string ConfigContract { get; set; }

    public string Settings { get; set; }

    public bool Enable { get; set; } = true;

    public int Priority { get; set; } = 1;

    public string Url { get; set; }

    public string ApiKey { get; set; } = string.Empty;

    public List<int> Categories { get; set; } = new();

    public bool EnableRss { get; set; } = true;

    public bool EnableSearch { get; set; } = true;

    public bool FreeleechOnly { get; set; }

    public int MinSeeders { get; set; } = 1;

    public int DownloadClientId { get; set; }

    public List<int> Tags { get; set; } = new();
}
