// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent.Creation;

public class TorrentCreationService : ITorrentCreationService
{
    private static readonly string[] SensitiveDirectoriesUnix = new[]
    {
        "/etc",
        "/bin",
        "/sbin",
        "/usr",
        "/root",
        "/boot",
        "/dev",
        "/proc",
        "/sys",
        "/run",
        "/var/run",
        "/var/log",
        "/var/spool",
        "/var/mail",
        "/var/backups",
        "/var/lib",
        "/lib",
        "/lib64",
        "/lib32",
    };

    private static readonly string[] SensitiveDirectoriesWindows = GetWindowsSensitiveDirectories();

    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly List<string> allowedDirectories;

    public TorrentCreationService()
        : this(null)
    {
    }

    public TorrentCreationService(IEnumerable<string> allowedDirectories)
    {
        this.allowedDirectories = allowedDirectories?
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList() ?? new List<string>();
    }

    public TorrentCreationService(
        ICategoryService categoryService,
        IConfigService configService,
        IStoragePathService storagePathService = null)
    {
        this.allowedDirectories = new List<string>();

        if (configService != null)
        {
            if (!string.IsNullOrWhiteSpace(configService.DownloadDir))
            {
                this.allowedDirectories.Add(configService.DownloadDir);
            }

            if (!string.IsNullOrWhiteSpace(configService.IncompleteDownloadDir))
            {
                this.allowedDirectories.Add(configService.IncompleteDownloadDir);
            }
        }

        if (categoryService != null)
        {
            try
            {
                var categories = categoryService.GetAll();
                if (categories != null)
                {
                    foreach (var category in categories)
                    {
                        if (!string.IsNullOrWhiteSpace(category.SavePath))
                        {
                            this.allowedDirectories.Add(category.SavePath);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (storagePathService != null)
        {
            try
            {
                var inc = storagePathService.GetIncompleteDirectory();
                if (!string.IsNullOrWhiteSpace(inc))
                {
                    this.allowedDirectories.Add(inc);
                }
            }
            catch
            {
            }
        }
    }

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

        if (!this.ValidatePath(request.Path, "Source path", out var pathError))
        {
            return new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = pathError,
            };
        }

        if (!string.IsNullOrWhiteSpace(request.OutputPath) && !this.ValidatePath(request.OutputPath, "Output path", out var outputError))
        {
            return new TorrentCreationResult
            {
                Success = false,
                ErrorMessage = outputError,
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
            var parsed = MonoTorrent.Torrent.Load(bytes);

            string outputPath = null;
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                outputPath = request.OutputPath;
                if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith('/'))
                {
                    var fileName = $"{parsed.Name}.torrent";
                    outputPath = Path.Combine(outputPath, fileName);
                }

                if (!this.ValidatePath(outputPath, "Resolved output path", out var finalOutputError))
                {
                    return new TorrentCreationResult
                    {
                        Success = false,
                        ErrorMessage = finalOutputError,
                    };
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

    private static string[] GetWindowsSensitiveDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.System));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        AddIfNotEmpty(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        return list.ToArray();
    }

    private static void AddIfNotEmpty(List<string> list, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(path);
        }
    }

    private bool ValidatePath(string path, string paramName, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = $"{paramName} cannot be empty or whitespace.";
            return false;
        }

        if (path.Contains('\0'))
        {
            error = $"{paramName} contains null bytes.";
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = $"{paramName} contains invalid path characters.";
            return false;
        }

        var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed == "." || trimmed == "..")
            {
                error = $"{paramName} contains directory traversal sequence: '{path}'.";
                return false;
            }
        }

        if (path.Contains("%2e", StringComparison.OrdinalIgnoreCase))
        {
            var unescaped = Uri.UnescapeDataString(path);
            if (unescaped.Contains(".."))
            {
                error = $"{paramName} contains encoded directory traversal sequence: '{path}'.";
                return false;
            }
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            error = $"{paramName} is invalid: {ex.Message}";
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Reject filesystem root
        if (fullPath == "/" || (fullPath.Length == 3 && char.IsLetter(fullPath[0]) && fullPath[1] == ':' && (fullPath[2] == '\\' || fullPath[2] == '/')))
        {
            error = $"{paramName} cannot be the filesystem root: '{path}'.";
            return false;
        }

        // Reject sensitive system directories on Unix
        if (!OperatingSystem.IsWindows())
        {
            foreach (var sensitive in SensitiveDirectoriesUnix)
            {
                if (fullPath.Equals(sensitive, comparison) ||
                    fullPath.StartsWith(sensitive + "/", comparison))
                {
                    error = $"{paramName} resides within a restricted system directory: '{path}'.";
                    return false;
                }
            }
        }
        else
        {
            // Reject sensitive system directories on Windows
            foreach (var sensitive in SensitiveDirectoriesWindows)
            {
                if (!string.IsNullOrEmpty(sensitive) &&
                    (fullPath.Equals(sensitive, comparison) ||
                     fullPath.StartsWith(sensitive + "\\", comparison) ||
                     fullPath.StartsWith(sensitive + "/", comparison)))
                {
                    error = $"{paramName} resides within a restricted system directory: '{path}'.";
                    return false;
                }
            }
        }

        // If allowed directories are specified, ensure the path resides inside at least one of them
        if (this.allowedDirectories.Count > 0)
        {
            bool isAllowed = false;
            foreach (var allowed in this.allowedDirectories)
            {
                if (string.IsNullOrWhiteSpace(allowed))
                {
                    continue;
                }

                try
                {
                    var fullAllowed = Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var fullTarget = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (string.Equals(fullAllowed, fullTarget, comparison) ||
                        fullTarget.StartsWith(fullAllowed + Path.DirectorySeparatorChar, comparison) ||
                        fullTarget.StartsWith(fullAllowed + "/", comparison))
                    {
                        isAllowed = true;
                        break;
                    }
                }
                catch
                {
                }
            }

            if (!isAllowed)
            {
                error = $"{paramName} resides outside of allowed storage directories: '{path}'.";
                return false;
            }
        }

        return true;
    }
}
