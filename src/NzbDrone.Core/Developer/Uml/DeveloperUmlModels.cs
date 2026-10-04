// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Developer.Uml;

public class DeveloperUmlOptions
{
    public string DiagramType { get; set; } = "class";

    public string Subsystem { get; set; } = "all";

    public bool IncludeInterfaces { get; set; } = true;

    public bool IncludeMethods { get; set; } = true;
}

public class DeveloperUmlDiagramResult
{
    public string DiagramType { get; set; }

    public string Subsystem { get; set; }

    public string Title { get; set; }

    public string MermaidCode { get; set; }

    public int NodeCount { get; set; }

    public int EdgeCount { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public List<string> AvailableSubsystems { get; set; } = new();

    public List<string> AvailableDiagramTypes { get; set; } = new();
}
