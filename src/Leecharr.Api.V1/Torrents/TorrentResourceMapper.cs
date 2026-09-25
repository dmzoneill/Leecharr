// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Api.V1.Torrents;

public static class TorrentResourceMapper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
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

    public static TorrentResource ToResource(Torrent model, TorrentMediaMetadata metadata = null, string bitfield = null, IDownloadTask task = null)
    {
        if (model == null)
        {
            return null;
        }

        var isInactive = model.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued;

        var progress = model.Progress;
        var downloaded = model.Downloaded;
        var downloadSpeed = isInactive ? 0 : model.DownloadSpeed;
        var uploadSpeed = isInactive ? 0 : model.UploadSpeed;
        var seeders = isInactive ? 0 : model.Seeders;
        var leechers = isInactive ? 0 : model.Leechers;
        var totalSize = model.TotalSize;
        var status = model.Status.ToString().ToLowerInvariant();

        if (task != null)
        {
            if (task.Progress > 0 || model.Progress == 0)
            {
                progress = task.Progress;
            }

            if (task.DownloadedBytes > 0)
            {
                downloaded = task.DownloadedBytes;
            }

            if (totalSize <= 0 && task.TotalBytes > 0)
            {
                totalSize = task.TotalBytes;
            }

            if (!isInactive)
            {
                downloadSpeed = task.DownloadSpeed;
                uploadSpeed = task.UploadSpeed;
                seeders = task.ConnectedSeeders;
                leechers = task.ConnectedLeechers;
            }

            if (task.Status != TorrentStatus.Downloading || model.Status != TorrentStatus.Checking)
            {
                status = task.Status.ToString().ToLowerInvariant();
            }
        }

        var resource = new TorrentResource
        {
            Id = model.Id,
            Name = model.Name,
            InfoHash = model.InfoHash,
            V2InfoHash = model.V2InfoHash,
            TotalSize = totalSize,
            PieceCount = model.PieceCount,
            PieceLength = model.PieceLength,
            Comment = model.Comment,
            CreatedBy = model.CreatedBy,
            CreationDate = model.CreationDate,
            IsPrivate = model.IsPrivate,
            Status = status,
            Downloaded = downloaded,
            Uploaded = model.Uploaded,
            Ratio = model.Ratio,
            Progress = progress,
            DownloadSpeed = downloadSpeed,
            UploadSpeed = uploadSpeed,
            Eta = isInactive ? 0 : model.Eta,
            Seeders = seeders,
            Leechers = leechers,
            SavePath = model.SavePath,
            Category = model.Category,
            Label = model.Label,
            TrackerUrl = TrackerUrlSanitizer.Sanitize(model.TrackerUrl),
            Trackers = !string.IsNullOrWhiteSpace(model.TrackerUrl)
                ? new List<string> { TrackerUrlSanitizer.Sanitize(model.TrackerUrl) }
                : new List<string>(),
            ErrorMessage = model.ErrorMessage,
            Priority = model.Priority,
            QueuePosition = model.QueuePosition,
            DownloadLimit = model.DownloadLimit,
            UploadLimit = model.UploadLimit,
            SequentialDownload = model.SequentialDownload,
            FirstLastPiecePriority = model.FirstLastPiecePriority,
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
            IsImported = model.IsImported,
            ImportedAt = model.ImportedAt,
            ImportedByArr = model.ImportedByArr,
            ImportPath = model.ImportPath,
            Bitfield = bitfield,
        };

        if (metadata != null)
        {
            resource.MediaTitle = metadata.Title;
            resource.MediaYear = metadata.Year > 0 ? metadata.Year : null;
            resource.MediaOverview = metadata.Overview;
            resource.PosterUrl = !string.IsNullOrEmpty(metadata.PosterLocalPath)
                ? $"/api/v1/media/artwork/{model.Id}/poster"
                : metadata.PosterUrl;
            resource.BackdropUrl = !string.IsNullOrEmpty(metadata.BackdropLocalPath)
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
                catch (Exception ex)
                {
                    Logger.Trace(ex, "Failed to deserialize MediaInfoJson for torrent resource mapping");
                }
            }
        }

        return resource;
    }
}
