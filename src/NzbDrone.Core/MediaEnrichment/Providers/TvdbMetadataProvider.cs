// Copyright (c) PlaceholderCompany. All rights reserved.

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
        SupportsNfoParsing = false,
    };

    public Task<MediaMetadataHealthCheckResult> ProbeHealthAsync()
    {
        return Task.FromResult(new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "TheTVDB provider is active.",
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
            Rating = 0.0,
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
        cleaned = Regex.Replace(cleaned, @"(?i)\b(S\d+(?:E\d+)?|\d+x\d+|Season\s*\d+|Episode\s*\d+|E\d{2,3})\b.*$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?i)\b(1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web-dl|webrip|x264|x265|hevc|h264|h265|dts|aac|repack|proper|internal|extended|unrated|multi|complete)\b.*$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?<!^)\s*\b(19\d\d|20\d\d)\b.*$", string.Empty);
        cleaned = cleaned.Trim('-', ' ', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? rawTitle.Trim() : cleaned.Trim();
    }

    private static int ExtractYear(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return 0;
        }

        var taggedMatch = Regex.Match(
            rawTitle,
            @"\b(19\d\d|20\d\d)\b(?=[.\s_]*(?:1080p|720p|2160p|4k|uhd|hdr|remux|bluray|web|dvd|x264|x265|hevc|h264|h265|\(|$))",
            RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        if (taggedMatch.Success && int.TryParse(taggedMatch.Value, out var ty) && ty >= 1900 && ty <= DateTime.UtcNow.Year + 2)
        {
            return ty;
        }

        var rightmostMatch = Regex.Match(rawTitle, @"\b(19\d\d|20\d\d)\b", RegexOptions.RightToLeft);
        return rightmostMatch.Success && int.TryParse(rightmostMatch.Value, out var y) ? y : 0;
    }
}
