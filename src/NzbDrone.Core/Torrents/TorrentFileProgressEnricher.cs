// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.BitTorrent;

namespace NzbDrone.Core.Torrents;

public static class TorrentFileProgressEnricher
{
    public static void Enrich(Torrent torrent, IEnumerable<TorrentFile> files, IDownloadTask downloadTask = null)
    {
        if (torrent == null || files == null)
        {
            return;
        }

        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Completed || torrent.Status == TorrentStatus.Seeding)
        {
            foreach (var file in files)
            {
                file.Progress = 1.0;
                file.BytesCompleted = file.Size;
            }

            return;
        }

        if (downloadTask?.PieceBitfield != null && downloadTask.PieceBitfield.Length > 0)
        {
            foreach (var file in files)
            {
                EnrichFromFilePieceBitfield(torrent, file, downloadTask);
            }

            return;
        }

        // Fallback: prorated from torrent progress
        var torrentProgress = Math.Clamp(torrent.Progress, 0.0, 1.0);
        foreach (var file in files)
        {
            if (torrentProgress >= 1.0)
            {
                file.Progress = 1.0;
                file.BytesCompleted = file.Size;
            }
            else
            {
                file.BytesCompleted = (long)Math.Round(file.Size * torrentProgress);
                file.Progress = file.Size > 0 ? Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0) : 1.0;
            }
        }
    }

    private static void EnrichFromFilePieceBitfield(Torrent torrent, TorrentFile file, IDownloadTask downloadTask)
    {
        if (file.Size <= 0)
        {
            file.BytesCompleted = 0;
            file.Progress = 1.0;
            return;
        }

        if (file.PieceCount <= 0 || downloadTask.PieceBitfield == null || downloadTask.PieceBitfield.Length == 0)
        {
            var torrentProgress = Math.Clamp(torrent.Progress, 0.0, 1.0);
            file.BytesCompleted = (long)Math.Round(file.Size * torrentProgress);
            file.Progress = Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0);
            return;
        }

        var pieceLength = downloadTask.PieceLength > 0
            ? downloadTask.PieceLength
            : (torrent.PieceLength > 0
                ? torrent.PieceLength
                : (downloadTask.Picker?.PieceLength ?? 0));

        if (pieceLength <= 0 && torrent.PieceCount > 0 && torrent.TotalSize > 0)
        {
            pieceLength = (int)Math.Ceiling((double)torrent.TotalSize / torrent.PieceCount);
        }

        if (pieceLength <= 0 && downloadTask.PieceBitfield.Length > 0 && torrent.TotalSize > 0)
        {
            pieceLength = (int)Math.Ceiling((double)torrent.TotalSize / downloadTask.PieceBitfield.Length);
        }

        var bitfield = downloadTask.PieceBitfield;
        var completedPieces = 0;
        for (var i = file.PieceOffset; i < file.PieceOffset + file.PieceCount && i < bitfield.Length; i++)
        {
            if (bitfield[i])
            {
                completedPieces++;
            }
        }

        if (completedPieces == file.PieceCount)
        {
            file.BytesCompleted = file.Size;
            file.Progress = 1.0;
        }
        else if (completedPieces == 0)
        {
            file.BytesCompleted = 0;
            file.Progress = 0.0;
        }
        else if (pieceLength > 0)
        {
            var bytes = (long)completedPieces * pieceLength;
            file.BytesCompleted = Math.Clamp(bytes, 0, file.Size);
            file.Progress = Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0);
        }
        else
        {
            var fraction = (double)completedPieces / file.PieceCount;
            file.BytesCompleted = Math.Clamp((long)Math.Round(file.Size * fraction), 0, file.Size);
            file.Progress = Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0);
        }
    }
}
