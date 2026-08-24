namespace NzbDrone.Core.MediaEnrichment.Providers;

public class MediaMetadataCapabilities
{
    public bool SupportsMovies { get; set; }
    public bool SupportsTvSeries { get; set; }
    public bool SupportsMusic { get; set; }
    public bool SupportsPosters { get; set; }
    public bool SupportsFanart { get; set; }
    public bool SupportsCast { get; set; }
    public bool SupportsSeasonBanners { get; set; }
    public bool SupportsNfoParsing { get; set; }
}
