// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.Torrents;

public static class TorrentFileResourceMapper
{
    public static TorrentFileResource ToResource(TorrentFile model)
    {
        if (model == null)
        {
            return null;
        }

        return new TorrentFileResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Path = model.Path,
            Size = model.Size,
            PieceOffset = model.PieceOffset,
            PieceCount = model.PieceCount,
            Priority = model.Priority,
            Progress = model.Progress,
            BytesCompleted = model.BytesCompleted,
        };
    }

    public static List<TorrentFileResource> ToResource(this IEnumerable<TorrentFile> models)
    {
        return models?.Select(ToResource).ToList() ?? new List<TorrentFileResource>();
    }
}
