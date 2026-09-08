// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Update;

public class UpdatePackageChanges
{
    public List<string> New { get; set; } = new();

    public List<string> Fixed { get; set; } = new();
}

public class UpdatePackage
{
    public string Version { get; set; }

    public DateTime ReleaseDate { get; set; }

    public string FileName { get; set; }

    public string Url { get; set; }

    public bool Installed { get; set; }

    public bool Latest { get; set; }

    public UpdatePackageChanges Changes { get; set; } = new();
}
