// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Indexers;

public class TorznabSearchCriteria
{
    public string Query { get; set; }

    public int? CategoryId { get; set; }

    public int Limit { get; set; } = 50;

    public int Offset { get; set; }

    public int? Season { get; set; }

    public int? Ep { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public string SearchType { get; set; }

    public string TvdbId { get; set; }

    public string Rid { get; set; }

    public int? Year { get; set; }

    public string Artist { get; set; }

    public string Album { get; set; }

    public string Author { get; set; }

    public string Isbn { get; set; }
}
