// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Packages;

public class PackageExportService : IPackageExportService
{
    public const int BoundedBufferSize = 65536; // 64 KiB

    private readonly ITorrentService torrentService;
    private readonly ITagRepository tagRepository;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;

    public PackageExportService(
        ITorrentService torrentService,
        ITagRepository tagRepository = null,
        IAppFolderInfo appFolderInfo = null)
    {
        this.torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        this.tagRepository = tagRepository;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ExportPackageAsync(
        Stream outputStream,
        IEnumerable<int> torrentIds,
        bool includePayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(torrentIds);

        var idList = torrentIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            throw new ArgumentException("At least one torrent ID must be provided.", nameof(torrentIds));
        }

        var torrents = new List<Torrent>();
        foreach (var id in idList)
        {
            Torrent torrent = null;
            try
            {
                torrent = this.torrentService.Get(id);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error retrieving torrent with ID {0} for export.", id);
            }

            if (torrent != null)
            {
                torrents.Add(torrent);
            }
            else
            {
                this.logger.Warn("Torrent with ID {0} not found for export.", id);
            }
        }

        if (torrents.Count == 0)
        {
            throw new ArgumentException("None of the specified torrents were found.", nameof(torrentIds));
        }

        await using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true))
        {
            await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
            {
                // 1. Write manifest.json
                await this.WriteManifestEntryAsync(tarWriter, torrents, cancellationToken);

                // 2. Export each torrent
                foreach (var torrent in torrents)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await this.WriteMetainfoEntryAsync(tarWriter, torrent, cancellationToken);
                    await this.WriteFastResumeEntryAsync(tarWriter, torrent, cancellationToken);

                    if (includePayload)
                    {
                        await this.WritePayloadEntriesAsync(tarWriter, torrent, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task WriteManifestEntryAsync(TarWriter tarWriter, List<Torrent> torrents, CancellationToken cancellationToken)
    {
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            ExportedAt = DateTime.UtcNow,
            Client = "Leecharr",
            Torrents = torrents.Select(t => new PackageTorrentItem
            {
                Id = t.Id,
                Name = t.Name,
                InfoHash = t.InfoHash,
                Category = t.Category,
                Tags = this.ResolveTags(t),
                Trackers = this.ResolveTrackers(t),
                TotalSize = t.TotalSize,
                PieceCount = t.PieceCount,
                PieceLength = t.PieceLength,
                IsPrivate = t.IsPrivate,
            }).ToList(),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(manifest);
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream(bytes);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
        {
            DataStream = ms,
        };

        await tarWriter.WriteEntryAsync(entry, cancellationToken);
    }

    private List<string> ResolveTags(Torrent torrent)
    {
        if (torrent.TagIds == null || torrent.TagIds.Count == 0)
        {
            return new List<string>();
        }

        if (this.tagRepository != null)
        {
            try
            {
                var allTags = this.tagRepository.All();
                var labels = allTags.Where(t => torrent.TagIds.Contains(t.Id)).Select(t => t.Label).ToList();
                if (labels.Count > 0)
                {
                    return labels;
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to resolve tag labels for torrent {0}", torrent.Id);
            }
        }

        return torrent.TagIds.Select(t => t.ToString()).ToList();
    }

    private List<PackageTrackerItem> ResolveTrackers(Torrent torrent)
    {
        var list = new List<PackageTrackerItem>();
        if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            list.Add(new PackageTrackerItem
            {
                Url = torrent.TrackerUrl,
                Tier = 0,
                Enabled = true,
                Status = "Working",
            });
        }

        return list;
    }

    private async Task WriteMetainfoEntryAsync(TarWriter tarWriter, Torrent torrent, CancellationToken cancellationToken)
    {
        var infoHash = !string.IsNullOrWhiteSpace(torrent.InfoHash) ? torrent.InfoHash.ToLowerInvariant() : "unknown";
        var entryName = $"metainfo/{infoHash}.torrent";

        var torrentFilePath = this.FindTorrentFilePath(infoHash);
        if (!string.IsNullOrWhiteSpace(torrentFilePath) && File.Exists(torrentFilePath))
        {
            try
            {
                await using var fs = new FileStream(torrentFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BoundedBufferSize, useAsync: true);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                {
                    DataStream = fs,
                };
                await tarWriter.WriteEntryAsync(entry, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to read .torrent file from {0} for {1}", torrentFilePath, infoHash);
            }
        }
    }

    private string FindTorrentFilePath(string infoHash)
    {
        if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
        {
            var p = Path.Combine(this.appFolderInfo.AppDataFolder, "torrents", $"{infoHash}.torrent");
            if (File.Exists(p))
            {
                return p;
            }
        }

        return null;
    }

    private async Task WriteFastResumeEntryAsync(TarWriter tarWriter, Torrent torrent, CancellationToken cancellationToken)
    {
        var infoHash = !string.IsNullOrWhiteSpace(torrent.InfoHash) ? torrent.InfoHash.ToLowerInvariant() : "unknown";
        var entryName = $"fastresume/{infoHash}.json";

        var resumeData = new
        {
            Name = torrent.Name,
            InfoHash = torrent.InfoHash,
            Category = torrent.Category,
            SavePath = torrent.SavePath,
            TotalSize = torrent.TotalSize,
            Downloaded = torrent.Downloaded,
            Uploaded = torrent.Uploaded,
            Status = torrent.Status.ToString(),
            SequentialDownload = torrent.SequentialDownload,
            FirstLastPiecePriority = torrent.FirstLastPiecePriority,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(resumeData);
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream(bytes);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = ms,
        };

        await tarWriter.WriteEntryAsync(entry, cancellationToken);
    }

    private async Task WritePayloadEntriesAsync(TarWriter tarWriter, Torrent torrent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            return;
        }

        var infoHash = !string.IsNullOrWhiteSpace(torrent.InfoHash) ? torrent.InfoHash.ToLowerInvariant() : "unknown";
        var payloadPrefix = $"payload/{infoHash}/";

        if (File.Exists(torrent.SavePath))
        {
            var fileName = Path.GetFileName(torrent.SavePath);
            var entryName = payloadPrefix + fileName;

            await using var fs = new FileStream(torrent.SavePath, FileMode.Open, FileAccess.Read, FileShare.Read, BoundedBufferSize, useAsync: true);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = fs,
            };
            await tarWriter.WriteEntryAsync(entry, cancellationToken);
        }
        else if (Directory.Exists(torrent.SavePath))
        {
            var rootDir = new DirectoryInfo(torrent.SavePath);
            var files = rootDir.EnumerateFiles("*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(torrent.SavePath, file.FullName).Replace('\\', '/');
                var entryName = payloadPrefix + relativePath;

                await using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, BoundedBufferSize, useAsync: true);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                {
                    DataStream = fs,
                };
                await tarWriter.WriteEntryAsync(entry, cancellationToken);
            }
        }
    }
}
