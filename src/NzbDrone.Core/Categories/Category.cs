// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public class Category : ModelBase
{
    public string Name { get; set; }

    public string SavePath { get; set; }

    public int DefaultUploadLimit { get; set; }

    public int DefaultDownloadLimit { get; set; }

    public double TargetRatio { get; set; }

    public int TargetSeedTimeMinutes { get; set; }

    public bool AutoStop { get; set; }

    public bool IsDefault { get; set; }
}
