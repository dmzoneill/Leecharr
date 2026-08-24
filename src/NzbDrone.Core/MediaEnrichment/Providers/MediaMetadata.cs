using System.Collections.Generic;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class MediaMetadata
{
    public string Title { get; set; }
    public int Year { get; set; }
    public string Overview { get; set; }
    public string PosterUrl { get; set; }
    public string BackdropUrl { get; set; }
    public string BannerUrl { get; set; }
    public string Genres { get; set; }
    public double Rating { get; set; }
    public string ImdbId { get; set; }
    public string TmdbId { get; set; }
    public string TvdbId { get; set; }
    public string MediaType { get; set; }
    public List<string> Cast { get; set; } = new();
    public Dictionary<string, string> ExtraData { get; set; } = new();
}
