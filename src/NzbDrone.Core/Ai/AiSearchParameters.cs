// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Ai;

public class AiSearchParameters
{
    public string RawQuery { get; set; }

    public string CleanQuery { get; set; }

    public string CleanTitle { get; set; }

    public string Category { get; set; }

    public int? Year { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public string Resolution { get; set; }

    public string Quality { get; set; }

    public string Codec { get; set; }

    public string ReleaseGroup { get; set; }

    public int MinSeeders { get; set; }

    public int? MaxAgeDays { get; set; }

    public bool FreeleechOnly { get; set; }

    public List<string> Tags { get; set; } = new();

    public double ConfidenceScore { get; set; } = 1.0;
}
