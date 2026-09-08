// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Organizer;

public class MovieNamingContext
{
    public string MovieTitle { get; set; } = string.Empty;

    public string MovieCleanTitle { get; set; } = string.Empty;

    public int ReleaseYear { get; set; }

    public string Quality { get; set; } = string.Empty;

    public string QualityFull { get; set; } = string.Empty;

    public string ReleaseGroup { get; set; } = string.Empty;

    public string VideoCodec { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public string AudioChannels { get; set; } = string.Empty;

    public string HdrFormat { get; set; } = string.Empty;

    public string VideoDynamicRange { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string Edition { get; set; } = string.Empty;

    public string ImdbId { get; set; } = string.Empty;

    public string TmdbId { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;
}
