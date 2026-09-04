// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Indexers;

public class TorznabCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<TorznabCategory> SubCategories { get; set; } = new();
}

public class TorznabCapabilities
{
    public List<TorznabCategory> Categories { get; set; } = new();

    public bool SupportsSearch { get; set; } = true;

    public bool SupportsTvSearch { get; set; }

    public bool SupportsMovieSearch { get; set; }

    public bool SupportsMusicSearch { get; set; }

    public List<string> SupportedTvParams { get; set; } = new();

    public List<string> SupportedMovieParams { get; set; } = new();

    public List<string> SupportedMusicParams { get; set; } = new();

    public int DefaultPageSize { get; set; } = 50;

    public int MaxPageSize { get; set; } = 100;
}
