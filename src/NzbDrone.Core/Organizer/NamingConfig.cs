// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Organizer;

public class NamingConfig
{
    public bool RenameEpisodes { get; set; } = true;

    public bool ReplaceIllegalCharacters { get; set; } = true;

    public ColonReplacementFormat ColonReplacementFormat { get; set; } = ColonReplacementFormat.SpaceDashSpace;

    public string CustomColonReplacementFormat { get; set; } = string.Empty;

    public MultiEpisodeStyle MultiEpisodeStyle { get; set; } = MultiEpisodeStyle.Extend;

    public string StandardEpisodeFormat { get; set; } = "{Series Title} - S{season:00}E{episode:00} - {Episode CleanTitle} [{Quality Full}]";

    public string DailyEpisodeFormat { get; set; } = "{Series Title} - {Air-Date} - {Episode CleanTitle} [{Quality Full}]";

    public string AnimeEpisodeFormat { get; set; } = "{Series Title} - S{season:00}E{episode:00} - {absolute:000} - {Episode CleanTitle} [{Quality Full}]";

    public string SeriesFolderFormat { get; set; } = "{Series Title}";

    public string SeasonFolderFormat { get; set; } = "Season {season}";

    public string StandardMovieFormat { get; set; } = "{Movie Title} ({Release Year}) [{Quality Full}]";

    public string MovieFolderFormat { get; set; } = "{Movie Title} ({Release Year})";
}
