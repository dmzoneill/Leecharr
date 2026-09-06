// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text.Json;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Torrents;

public static class TorrentResourceMapper
{
    public static string EncodeBitfield(bool[] pieces)
    {
        if (pieces == null || pieces.Length == 0)
        {
            return null;
        }

        var byteCount = (pieces.Length + 7) / 8;
        var bytes = new byte[byteCount];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i])
            {
                bytes[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
        }

        return Convert.ToBase64String(bytes);
    }

    public static TorrentResource ToResource(Torrent model, TorrentMediaMetadata metadata = null, string bitfield = null)
    {
        if (model == null)
        {
            return null;
        }

        var isInactive = model.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued;
        var resource = new TorrentResource
        {
            Id = model.Id,
            Name = model.Name,
            InfoHash = model.InfoHash,
            TotalSize = model.TotalSize,
            PieceCount = model.PieceCount,
            PieceLength = model.PieceLength,
            Comment = model.Comment,
            CreatedBy = model.CreatedBy,
            CreationDate = model.CreationDate,
            IsPrivate = model.IsPrivate,
            Status = model.Status.ToString().ToLowerInvariant(),
            Downloaded = model.Downloaded,
            Uploaded = model.Uploaded,
            Ratio = model.Ratio,
            Progress = model.Progress,
            DownloadSpeed = isInactive ? 0 : model.DownloadSpeed,
            UploadSpeed = isInactive ? 0 : model.UploadSpeed,
            Eta = isInactive ? 0 : model.Eta,
            Seeders = isInactive ? 0 : model.Seeders,
            Leechers = isInactive ? 0 : model.Leechers,
            SavePath = model.SavePath,
            Category = model.Category,
            Label = model.Label,
            TrackerUrl = model.TrackerUrl,
            ErrorMessage = model.ErrorMessage,
            Priority = model.Priority,
            QueuePosition = model.QueuePosition,
            DownloadLimit = model.DownloadLimit,
            UploadLimit = model.UploadLimit,
            SequentialDownload = model.SequentialDownload,
            InitialSeeding = model.InitialSeeding,
            ForceStart = model.ForceStart,
            TargetRatio = model.TargetRatio,
            TargetSeedTimeMinutes = model.TargetSeedTimeMinutes,
            ShareLimitAction = model.ShareLimitAction,
            DateAdded = model.DateAdded,
            DateCompleted = model.DateCompleted,
            LastActive = model.LastActive,
            TagIds = model.TagIds,
            SeedingTime = model.SeedingTimeSeconds,
            Bitfield = bitfield,
        };

        if (metadata != null)
        {
            resource.MediaTitle = metadata.Title;
            resource.MediaYear = metadata.Year > 0 ? metadata.Year : null;
            resource.MediaOverview = metadata.Overview;
            resource.PosterUrl = !string.IsNullOrEmpty(metadata.PosterLocalPath) && global::System.IO.File.Exists(metadata.PosterLocalPath)
                ? $"/api/v1/media/artwork/{model.Id}/poster"
                : metadata.PosterUrl;
            resource.BackdropUrl = !string.IsNullOrEmpty(metadata.BackdropLocalPath) && global::System.IO.File.Exists(metadata.BackdropLocalPath)
                ? $"/api/v1/media/artwork/{model.Id}/backdrop"
                : metadata.BackdropUrl;
            resource.MediaRating = metadata.Rating > 0 ? metadata.Rating : null;

            if (!string.IsNullOrEmpty(metadata.MediaInfoJson))
            {
                try
                {
                    var info = JsonSerializer.Deserialize<MediaContainerInfo>(metadata.MediaInfoJson);
                    if (info != null)
                    {
                        resource.Resolution = info.Resolution;
                        resource.VideoCodec = info.VideoCodec;
                        resource.AudioCodec = info.AudioCodec;
                        resource.AudioChannels = info.AudioChannels;
                        resource.HdrFormat = info.HdrFormat;
                    }
                }
                catch
                {
                    // Ignore parse errors
                }
            }
        }

        return resource;
    }
}
