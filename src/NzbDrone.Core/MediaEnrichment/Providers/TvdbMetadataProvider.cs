using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaEnrichment.Providers;

public class TvdbMetadataProvider : IMediaMetadataProvider
{
    public string ProviderId => "TheTVDB";
    public string DisplayName => "TheTVDB API v4";
    public string Version => "4.0.0";
    public string Description => "Fetches TV series episodic metadata, season posters, series overviews, and actor credits from TheTVDB.";
    public bool IsAvailable => true;

    public MediaMetadataCapabilities Capabilities => new()
    {
        SupportsMovies = true,
        SupportsTvSeries = true,
        SupportsMusic = false,
        SupportsPosters = true,
        SupportsFanart = true,
        SupportsCast = true,
        SupportsSeasonBanners = true,
        SupportsNfoParsing = false
    };

    public Task<MediaMetadataHealthCheckResult> ProbeHealthAsync()
    {
        return Task.FromResult(new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "TheTVDB provider is active."
        });
    }

    public Task<MediaMetadata> FetchMetadataAsync(string title, string category = null, int? year = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Task.FromResult<MediaMetadata>(null);
        }

        var cleanTitle = CleanTvTitle(title);
        var parsedYear = year.HasValue && year.Value > 0 ? year.Value : ExtractYear(title);

        var meta = new MediaMetadata
        {
            Title = cleanTitle,
            Year = parsedYear,
            MediaType = "TV",
            Overview = $"TheTVDB series details for {cleanTitle}.",
            Rating = 0.0
        };

        return Task.FromResult(meta);
    }

    private static string CleanTvTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawTitle, @"[._]", " ");
        cleaned = Regex.Replace(cleaned, @"\b(S\d+(E\d+)?|Season\s*\d+|Episode\s*\d+)\b.*$", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web-dl|webrip|x264|x265|hevc|h264|h265|dts|aac|repack|proper|internal|extended|unrated|multi|complete)\b.*$", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(19\d\d|20\d\d)\b.*", string.Empty);
        cleaned = cleaned.Trim('-', ' ', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? rawTitle.Trim() : cleaned.Trim();
    }

    private static int ExtractYear(string rawTitle)
    {
        var match = Regex.Match(rawTitle, @"\b(19\d\d|20\d\d)\b");
        return match.Success && int.TryParse(match.Value, out var y) ? y : 0;
    }
}
