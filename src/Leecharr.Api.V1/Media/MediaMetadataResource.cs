// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
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

    public List<string> Genres { get; set; }

    public double Rating { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public string TvdbId { get; set; }

    public string BannerUrl { get; set; }

    public string MusicBrainzId { get; set; }

    public string ArtistName { get; set; }

    public string AlbumTitle { get; set; }

    public List<string> Cast { get; set; }
}

public static class MediaMetadataResourceMapper
{
    public static MediaMetadataResource ToResource(TorrentMediaMetadata model)
    {
        if (model == null)
        {
            return null;
        }

        var posterUrl = model.TorrentId > 0 && !string.IsNullOrEmpty(model.PosterLocalPath)
            ? $"/api/v1/media/artwork/{model.TorrentId}/poster"
            : model.PosterUrl;

        var backdropUrl = model.TorrentId > 0 && !string.IsNullOrEmpty(model.BackdropLocalPath)
            ? $"/api/v1/media/artwork/{model.TorrentId}/backdrop"
            : model.BackdropUrl;

        return new MediaMetadataResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            ArrType = model.ArrType,
            ArrMediaId = model.ArrMediaId,
            Title = model.Title,
            Year = model.Year,
            Overview = model.Overview,
            PosterUrl = posterUrl,
            PosterLocalPath = model.PosterLocalPath,
            BackdropUrl = backdropUrl,
            BackdropLocalPath = model.BackdropLocalPath,
            BannerUrl = model.BannerUrl,
            MediaInfoJson = model.MediaInfoJson,
            Genres = string.IsNullOrWhiteSpace(model.Genres)
                ? new List<string>()
                : model.Genres.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).ToList(),
            Rating = model.Rating,
            ImdbId = model.ImdbId,
            TmdbId = model.TmdbId,
            TvdbId = model.TvdbId,
            MusicBrainzId = model.MusicBrainzId,
            ArtistName = model.ArtistName,
            AlbumTitle = model.AlbumTitle,
            Cast = string.IsNullOrWhiteSpace(model.Cast)
                ? new List<string>()
                : model.Cast.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList(),
        };
    }
}
