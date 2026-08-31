// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Ai;

public class AiDiagnosticReport
{
    public int TorrentId { get; set; }

    public string TorrentName { get; set; }

    public string OverallHealth { get; set; }

    public string Severity { get; set; }

    public string Summary { get; set; }

    public List<string> Issues { get; set; } = new();

    public List<string> Recommendations { get; set; } = new();

    public List<string> SuggestedActions { get; set; } = new();

    public string SwarmAnalysis { get; set; }

    public string TrackerAnalysis { get; set; }

    public double HealthScore { get; set; }

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}
