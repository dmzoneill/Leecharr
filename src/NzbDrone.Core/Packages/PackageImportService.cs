// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Packages;

public class PackageImportService : IPackageImportService
{
    private const int BufferSize = 65536; // 64 KiB
    private const long DefaultMaxUncompressedBytes = 50L * 1024 * 1024 * 1024; // 50 GB

    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly IConfigService configService;
    private readonly Logger logger;

    public PackageImportService(
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser = null,
        IAppFolderInfo appFolderInfo = null,
        IConfigService configService = null)
    {
        this.torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        this.torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.appFolderInfo = appFolderInfo;
        this.configService = configService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<PackageImportResult> ImportPackageAsync(
        Stream archiveStream,
        PackageImportOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        options ??= new PackageImportOptions();

        var result = new PackageImportResult();
        var isTemporarySandbox = false;
        var targetRootDir = options.TargetRootDir;

        if (string.IsNullOrWhiteSpace(targetRootDir))
        {
            var appData = this.appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
            targetRootDir = Path.Combine(appData, "temp", "import_" + Guid.NewGuid().ToString("N"));
            isTemporarySandbox = true;
        }

        var canonicalTargetRoot = Path.GetFullPath(targetRootDir);
        Directory.CreateDirectory(canonicalTargetRoot);

        try
        {
            var effectiveMaxBytes = options.MaxUncompressedBytes ?? DefaultMaxUncompressedBytes;
            var maxCompressionRatio = options.MaxCompressionRatio > 0 ? options.MaxCompressionRatio : 50.0;
            var minBytesForRatioCheck = options.MinBytesForRatioCheck;

            // Check GZip magic header (0x1F, 0x8B)
            var header = new byte[2];
            var readHeader = await archiveStream.ReadAsync(header.AsMemory(0, 2), cancellationToken);
            var isGzip = readHeader == 2 && header[0] == 0x1F && header[1] == 0x8B;

            Stream effectiveStream;
            if (archiveStream.CanSeek)
            {
                archiveStream.Position = 0;
                effectiveStream = isGzip ? new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: true) : archiveStream;
            }
            else
            {
                var ms = new MemoryStream();
                ms.Write(header, 0, readHeader);
                await archiveStream.CopyToAsync(ms, cancellationToken);
                ms.Position = 0;
                effectiveStream = isGzip ? new GZipStream(ms, CompressionMode.Decompress, leaveOpen: true) : ms;
            }

            PackageManifest manifest = null;
            var metainfoFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var extractedPayloadFiles = new List<string>();
            long totalExtractedBytes = 0;
            long totalReadArchiveBytes = 0;

            await using (effectiveStream)
            {
                using var tarReader = new TarReader(effectiveStream, leaveOpen: true);
                TarEntry entry;

                while ((entry = await tarReader.GetNextEntryAsync(cancellationToken: cancellationToken)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryName = entry.Name.Replace('\\', '/');

                    // Security: Zip-Slip prevention
                    PackagePathTranslator.ValidateZipSlip(entryName);

                    if (entry.EntryType == TarEntryType.Directory)
                    {
                        continue;
                    }

                    if (entry.EntryType != TarEntryType.RegularFile && entry.EntryType != TarEntryType.V7RegularFile)
                    {
                        continue;
                    }

                    if (entry.DataStream == null)
                    {
                        continue;
                    }

                    // 1. Manifest
                    if (string.Equals(entryName, "manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var ms = new MemoryStream();
                        await entry.DataStream.CopyToAsync(ms, cancellationToken);
                        var json = Encoding.UTF8.GetString(ms.ToArray());
                        manifest = System.Text.Json.JsonSerializer.Deserialize<PackageManifest>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        continue;
                    }

                    // 2. Metainfo (.torrent file)
                    if (entryName.StartsWith("metainfo/", StringComparison.OrdinalIgnoreCase) && entryName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                    {
                        var infoHash = Path.GetFileNameWithoutExtension(entryName);
                        using var ms = new MemoryStream();
                        await entry.DataStream.CopyToAsync(ms, cancellationToken);
                        metainfoFiles[infoHash] = ms.ToArray();
                        continue;
                    }

                    // 3. FastResume (optional metadata)
                    if (entryName.StartsWith("fastresume/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 4. Payload files
                    if (entryName.StartsWith("payload/", StringComparison.OrdinalIgnoreCase))
                    {
                        var subPath = entryName.Substring("payload/".Length);
                        var slashIdx = subPath.IndexOf('/');
                        var infoHash = slashIdx >= 0 ? subPath.Substring(0, slashIdx) : subPath;
                        var relativeFilePath = slashIdx >= 0 ? subPath.Substring(slashIdx + 1) : string.Empty;

                        var destinationDir = !string.IsNullOrWhiteSpace(options.DestinationPath)
                            ? options.DestinationPath
                            : (!string.IsNullOrWhiteSpace(options.DestinationRoot)
                                ? options.DestinationRoot
                                : this.configService?.DownloadDir ?? canonicalTargetRoot);

                        var translatedPath = PackagePathTranslator.TranslatePath(
                            relativeFilePath,
                            options.SourcePrefix,
                            options.DestinationPrefix,
                            destinationDir,
                            options.PathRemappings);

                        var fullDestPath = Path.GetFullPath(Path.Combine(destinationDir, translatedPath));
                        PackagePathTranslator.ValidateZipSlip(fullDestPath);

                        var dir = Path.GetDirectoryName(fullDestPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        await using (var outFs = new FileStream(fullDestPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
                        {
                            var buffer = new byte[BufferSize];
                            int read;
                            while ((read = await entry.DataStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                            {
                                totalExtractedBytes += read;
                                totalReadArchiveBytes += read;

                                if (totalExtractedBytes > effectiveMaxBytes)
                                {
                                    throw new InvalidOperationException($"Package payload exceeds maximum extraction limit of {effectiveMaxBytes} bytes.");
                                }

                                if (totalExtractedBytes > minBytesForRatioCheck && totalReadArchiveBytes > 0)
                                {
                                    var ratio = (double)totalExtractedBytes / totalReadArchiveBytes;
                                    if (ratio > maxCompressionRatio)
                                    {
                                        throw new InvalidOperationException($"Suspicious compression ratio ({ratio:F1}:1) exceeding safety limit of {maxCompressionRatio}:1.");
                                    }
                                }

                                await outFs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            }
                        }

                        extractedPayloadFiles.Add(fullDestPath);
                    }
                }
            }

            result.ExtractedFiles = extractedPayloadFiles;
            result.TotalBytesExtracted = totalExtractedBytes;

            // Restore torrents into Leecharr
            if (options.RestoreTorrents)
            {
                var itemsToProcess = new List<(string InfoHash, string Name, string Category, List<string> Tags)>();

                if (manifest?.Torrents != null && manifest.Torrents.Count > 0)
                {
                    foreach (var item in manifest.Torrents)
                    {
                        itemsToProcess.Add((item.InfoHash, item.Name, item.Category, item.Tags));
                    }
                }
                else
                {
                    foreach (var kvp in metainfoFiles)
                    {
                        itemsToProcess.Add((kvp.Key, kvp.Key, null, new List<string>()));
                    }
                }

                foreach (var item in itemsToProcess)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var infoHash = item.InfoHash?.ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(infoHash))
                    {
                        continue;
                    }

                    // Check duplicate
                    var existing = this.torrentService.GetByInfoHash(infoHash);
                    if (existing != null)
                    {
                        result.SkippedDuplicates.Add(infoHash);
                        result.Torrents.Add(new PackageImportTorrentSummary
                        {
                            Id = existing.Id,
                            Name = existing.Name,
                            InfoHash = existing.InfoHash,
                            Category = existing.Category,
                            TotalSize = existing.TotalSize,
                            IsDuplicate = true,
                            Status = existing.Status.ToString(),
                            SavePath = existing.SavePath,
                        });
                        continue;
                    }

                    if (!metainfoFiles.TryGetValue(infoHash, out var rawBytes) || rawBytes == null || rawBytes.Length == 0)
                    {
                        this.logger.Warn("Metainfo not found in package for infohash {0}", infoHash);
                        continue;
                    }

                    try
                    {
                        var parsed = this.torrentFileParser.Parse(rawBytes);
                        var savePath = !string.IsNullOrWhiteSpace(options.DestinationPath)
                            ? options.DestinationPath
                            : (!string.IsNullOrWhiteSpace(options.DestinationRoot)
                                ? options.DestinationRoot
                                : this.configService?.DownloadDir);

                        var added = await this.torrentService.AddFromParsedTorrentAsync(
                            parsed,
                            item.Category,
                            savePath,
                            startPaused: false,
                            rawBytes: rawBytes);

                        if (added != null)
                        {
                            result.Torrents.Add(new PackageImportTorrentSummary
                            {
                                Id = added.Id,
                                Name = added.Name,
                                InfoHash = added.InfoHash,
                                Category = added.Category,
                                Tags = item.Tags,
                                TotalSize = added.TotalSize,
                                IsDuplicate = false,
                                Status = added.Status.ToString(),
                                SavePath = added.SavePath,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error(ex, "Failed to import torrent {0} ({1}) from package", item.Name, infoHash);
                    }
                }
            }

            result.Message = $"Successfully imported {result.ImportedTorrentsCount} torrent(s). Skipped {result.SkippedDuplicatesCount} duplicate(s).";
            return result;
        }
        finally
        {
            if (isTemporarySandbox && Directory.Exists(canonicalTargetRoot))
            {
                try
                {
                    Directory.Delete(canonicalTargetRoot, recursive: true);
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to clean up temporary sandbox {0}", canonicalTargetRoot);
                }
            }
        }
    }
}
