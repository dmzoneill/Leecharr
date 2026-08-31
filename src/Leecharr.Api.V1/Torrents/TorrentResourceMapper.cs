using System.Text.Json;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Torrents;

public static class TorrentResourceMapper
{
    public static TorrentResource ToResource(Torrent model, TorrentMediaMetadata metadata = null)
    {
        if (model == null)
        {
            return null;
        }

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
            DownloadSpeed = model.DownloadSpeed,
            UploadSpeed = model.UploadSpeed,
            Eta = model.Eta,
            Seeders = model.Seeders,
            Leechers = model.Leechers,
            SavePath = model.SavePath,
            Category = model.Category,
            Label = model.Label,
            TrackerUrl = model.TrackerUrl,
            Priority = model.Priority,
            QueuePosition = model.QueuePosition,
            DownloadLimit = model.DownloadLimit,
            UploadLimit = model.UploadLimit,
            SequentialDownload = model.SequentialDownload,
            InitialSeeding = model.InitialSeeding,
            ForceStart = model.ForceStart,
            TargetRatio = model.TargetRatio,
            TargetSeedTimeMinutes = model.TargetSeedTimeMinutes,
            DateAdded = model.DateAdded,
            DateCompleted = model.DateCompleted,
            LastActive = model.LastActive,
            TagIds = model.TagIds
        };

        if (metadata != null)
        {
            resource.MediaTitle = metadata.Title;
            resource.MediaYear = metadata.Year > 0 ? metadata.Year : null;
            resource.MediaOverview = metadata.Overview;
            resource.PosterUrl = metadata.PosterUrl;
            resource.BackdropUrl = metadata.BackdropUrl;
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
