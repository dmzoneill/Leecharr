using Leecharr.Http.REST;
using NzbDrone.Core.MediaEnrichment;

namespace Leecharr.Api.V1.Media;

public class MediaMetadataResource : RestResource
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

public static class MediaMetadataResourceMapper
{
    public static MediaMetadataResource ToResource(TorrentMediaMetadata model)
    {
        if (model == null)
        {
            return null;
        }

        return new MediaMetadataResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            ArrType = model.ArrType,
            ArrMediaId = model.ArrMediaId,
            Title = model.Title,
            Year = model.Year,
            Overview = model.Overview,
            PosterUrl = model.PosterUrl,
            PosterLocalPath = model.PosterLocalPath,
            BackdropUrl = model.BackdropUrl,
            BackdropLocalPath = model.BackdropLocalPath,
            MediaInfoJson = model.MediaInfoJson,
            Genres = model.Genres,
            Rating = model.Rating,
            ImdbId = model.ImdbId,
            TmdbId = model.TmdbId,
            TvdbId = model.TvdbId
        };
    }
}
