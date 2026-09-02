// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Configuration;

public class ConfigModel : ModelBase
{
    public string Key { get; set; }

    public string Value { get; set; }
}
