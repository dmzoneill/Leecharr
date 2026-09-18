// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Update;

public class UpdateChangesResource
{
    public List<string> New { get; set; } = new();

    public List<string> Fixed { get; set; } = new();
}

public class UpdateResource : RestResource
{
    public string Version { get; set; }

    public DateTime ReleaseDate { get; set; }

    public string FileName { get; set; }

    public string Url { get; set; }

    public bool Installed { get; set; }

    public bool Latest { get; set; }

    public bool IsContainer { get; set; }

    public UpdateChangesResource Changes { get; set; } = new();
}
