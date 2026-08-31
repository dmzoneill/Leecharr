// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Ai;

public class AiParsedRelease
{
    public string RawTitle { get; set; }

    public string CleanTitle { get; set; }

    public int? Year { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public List<int> Episodes { get; set; } = new();

    public string Resolution { get; set; }

    public string Quality { get; set; }

    public string VideoCodec { get; set; }

    public string AudioCodec { get; set; }

    public string AudioChannels { get; set; }

    public string DynamicRange { get; set; }

    public string ReleaseGroup { get; set; }

    public string Language { get; set; }

    public string Edition { get; set; }

    public bool IsProper { get; set; }

    public bool IsRepack { get; set; }

    public bool IsRemux { get; set; }

    public double ConfidenceScore { get; set; } = 1.0;

    public Dictionary<string, string> AdditionalTags { get; set; } = new();
}
