// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using NLog;

namespace NzbDrone.Core.BitTorrent.Creation;

public class TorrentCreationService : ITorrentCreationService
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public async Task<TorrentCreationResult> CreateTorrentAsync(TorrentCreationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Path))
        {
            return new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = "A valid source file or directory path is required.",
            };
        }

        if (!File.Exists(request.Path) && !Directory.Exists(request.Path))
        {
            return new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = $"Source path does not exist: {request.Path}",
            };
        }

        try
        {
            var fileSource = new TorrentFileSource(request.Path, ignoreHidden: true);
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                fileSource.TorrentName = request.Name;
            }

            var creator = new TorrentCreator
            {
                Comment = request.Comment ?? "Created by Leecharr",
                CreatedBy = request.CreatedBy ?? "Leecharr",
                Private = request.IsPrivate,
            };

            if (request.PieceLength > 0)
            {
                creator.PieceLength = request.PieceLength;
            }
            else
            {
                creator.PieceLength = TorrentCreator.RecommendedPieceSize(fileSource.Files);
            }

            if (request.Trackers != null && request.Trackers.Count > 0)
            {
                var cleanTrackers = request.Trackers
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .ToList();

                if (cleanTrackers.Count > 0)
                {
                    creator.Announce = cleanTrackers[0];
                    creator.Announces.Add(cleanTrackers);
                }
            }

            if (request.WebSeeds != null && request.WebSeeds.Count > 0)
            {
                creator.GetrightHttpSeeds.AddRange(request.WebSeeds.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()));
            }

            using var ms = new MemoryStream();
            await creator.CreateAsync(fileSource, ms, cancellationToken);
            var bytes = ms.ToArray();
            var parsed = Torrent.Load(bytes);

            string outputPath = null;
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                outputPath = request.OutputPath;
                if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith('/'))
                {
                    var fileName = $"{parsed.Name}.torrent";
                    outputPath = Path.Combine(outputPath, fileName);
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken);
                this.logger.Info("Saved generated .torrent file to '{0}'", outputPath);
            }

            this.logger.Info(
                "Successfully created torrent '{0}' (InfoHash: {1}, Size: {2} bytes, Pieces: {3})",
                parsed.Name,
                parsed.InfoHashes.V1OrV2.ToHex(),
                parsed.Size,
                parsed.PieceCount);

            return new TorrentCreationResult
            {
                Success = true,
                TorrentFileBytes = bytes,
                OutputPath = outputPath,
                InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
                TotalSize = parsed.Size,
                PieceCount = parsed.PieceCount,
                PieceLength = parsed.PieceLength,
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to create torrent for path '{0}'", request.Path);
            return new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }
}
