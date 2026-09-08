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
            long runningByteOffset = 0;
            foreach (var file in files)
            {
                var fileOffset = file.ByteOffset != 0 ? file.ByteOffset : (runningByteOffset > 0 ? runningByteOffset : (long)file.PieceOffset * (downloadTask.PieceLength > 0 ? downloadTask.PieceLength : torrent.PieceLength));
                EnrichFromFilePieceBitfield(torrent, file, fileOffset, downloadTask);
                runningByteOffset = fileOffset + Math.Max(0L, file.Size);
            }

            return;
        }

        // Fallback: prorated from torrent progress
        var torrentProgress = Math.Clamp(torrent.Progress, 0.0, 1.0);
        foreach (var file in files)
        {
            if (file.Priority == 0)
            {
                file.BytesCompleted = 0;
                file.Progress = 0.0;
                continue;
            }

            if (file.Size <= 0)
            {
                file.BytesCompleted = 0;
                file.Progress = 1.0;
                continue;
            }

            if (torrentProgress >= 1.0)
            {
                file.Progress = 1.0;
                file.BytesCompleted = file.Size;
            }
            else
            {
                file.BytesCompleted = (long)Math.Round(file.Size * torrentProgress);
                file.Progress = Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0);
            }
        }
    }

    private static void EnrichFromFilePieceBitfield(Torrent torrent, TorrentFile file, long byteOffset, IDownloadTask downloadTask)
    {
        if (file.Size <= 0)
        {
            file.BytesCompleted = 0;
            file.Progress = 1.0;
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

        if (pieceLength <= 0 && downloadTask.PieceBitfield != null && downloadTask.PieceBitfield.Length > 0 && torrent.TotalSize > 0)
        {
            pieceLength = (int)Math.Ceiling((double)torrent.TotalSize / downloadTask.PieceBitfield.Length);
        }

        if (pieceLength <= 0 || downloadTask.PieceBitfield == null || downloadTask.PieceBitfield.Length == 0)
        {
            if (file.Priority == 0)
            {
                file.BytesCompleted = 0;
                file.Progress = 0.0;
                return;
            }

            var torrentProgress = Math.Clamp(torrent.Progress, 0.0, 1.0);
            file.BytesCompleted = (long)Math.Round(file.Size * torrentProgress);
            file.Progress = Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0);
            return;
        }

        var bitfield = downloadTask.PieceBitfield;
        var fileStart = byteOffset;
        var fileEnd = byteOffset + file.Size;

        var startPiece = (int)(fileStart / pieceLength);
        var endPiece = (int)((fileEnd - 1) / pieceLength);

        long completedBytes = 0;
        for (var i = startPiece; i <= endPiece && i < bitfield.Length; i++)
        {
            if (bitfield[i])
            {
                var pieceStart = (long)i * pieceLength;
                var pieceEnd = pieceStart + pieceLength;
                if (torrent.TotalSize > 0 && pieceEnd > torrent.TotalSize)
                {
                    pieceEnd = torrent.TotalSize;
                }

                var intersectionStart = Math.Max(fileStart, pieceStart);
                var intersectionEnd = Math.Min(fileEnd, pieceEnd);
                if (intersectionEnd > intersectionStart)
                {
                    completedBytes += intersectionEnd - intersectionStart;
                }
            }
        }

        file.BytesCompleted = Math.Clamp(completedBytes, 0, file.Size);
        file.Progress = Math.Clamp((double)file.BytesCompleted / file.Size, 0.0, 1.0);
    }
}
