// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Organizer;

public class EpisodeNamingContext
{
    public string SeriesTitle { get; set; } = string.Empty;

    public string SeriesCleanTitle { get; set; } = string.Empty;

    public int SeasonNumber { get; set; } = 1;

    public List<int> EpisodeNumbers { get; set; } = new();

    public List<string> EpisodeTitles { get; set; } = new();

    public List<string> EpisodeCleanTitles { get; set; } = new();

    public List<int> AbsoluteEpisodeNumbers { get; set; } = new();

    public DateTime? AirDate { get; set; }

    public string Quality { get; set; } = string.Empty;

    public string QualityFull { get; set; } = string.Empty;

    public string ReleaseGroup { get; set; } = string.Empty;

    public string VideoCodec { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public string AudioChannels { get; set; } = string.Empty;

    public string HdrFormat { get; set; } = string.Empty;

    public string VideoDynamicRange { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string ImdbId { get; set; } = string.Empty;

    public string TmdbId { get; set; } = string.Empty;

    public string TvdbId { get; set; } = string.Empty;

    public int? ReleaseYear { get; set; }

    public string Extension { get; set; } = string.Empty;
}
