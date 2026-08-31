// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public class TorrentMediaMetadata : ModelBase
{
    public int TorrentId { get; set; }

    public string ArrType { get; set; }

    public int ArrMediaId { get; set; }

    public string Title { get; set; }

    public int Year { get; set; }

    public string Overview { get; set; }

    public string PosterUrl { get; set; }

    public string PosterLocalPath { get; set; }

    public string BackdropUrl { get; set; }

    public string BackdropLocalPath { get; set; }

    public string MediaInfoJson { get; set; }

    public string Genres { get; set; }

    public double Rating { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public string TvdbId { get; set; }
}
