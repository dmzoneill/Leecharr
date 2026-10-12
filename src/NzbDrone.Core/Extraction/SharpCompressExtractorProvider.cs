// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace NzbDrone.Core.Extraction;

public class SharpCompressExtractorProvider : IArchiveExtractorProvider
{
    public const int DefaultBufferSize = 128 * 1024; // 128 KB high-throughput chunk buffer

    private readonly IDiskProvider diskProvider;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly int bufferSize;
    private readonly Logger logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".lz", ".z", ".001",
    };

    public string ProviderId => "SharpCompress";

    public string DisplayName => "SharpCompress (Pure C# .NET)";

    public string Version => typeof(ArchiveFactory).Assembly.GetName().Version?.ToString() ?? "0.50.4";

    public string Description => "Pure managed C# archive extraction engine powered by SharpCompress. Zero native dependencies.";

    public bool IsAvailable => true;

    public int BufferSize => this.bufferSize;

    public ArchiveExtractorCapabilities Capabilities { get; } = new()
    {
        SupportsRar5 = true,
        Supports7z = true,
        SupportsZip = true,
        SupportsTarGz = true,
        SupportsMultiPart = true,
        SupportsPasswordProtected = true,
        SupportsSolidArchives = true,
        SupportsRecoveryVolumes = false,
    };

    public SharpCompressExtractorProvider(
        IDiskProvider diskProvider,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        int bufferSize = DefaultBufferSize)
    {
        this.diskProvider = diskProvider;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.bufferSize = bufferSize > 0 ? bufferSize : DefaultBufferSize;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<ExtractorHealthCheckResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ExtractorHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "SharpCompress managed library is operational.",
            DependencyChecks = new List<string> { "SharpCompress .NET assembly: Loaded & Ready" },
        });
    }

    public bool CanExtract(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (ArchiveExtractorEventHandler.IsSecondaryVolume(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext))
        {
            return true;
        }

        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".7z.001", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rar.001", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".zip.001", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tar.001", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ExtractAsync(
        string archivePath,
        string destinationPath,
        string password = null,
        IReadOnlyList<string> passwordCandidates = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !this.diskProvider.FileExists(archivePath))
        {
            this.logger.Warn("Archive file does not exist: {0}", archivePath);
            return false;
        }

        var targetDir = destinationPath;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            var dir = Path.GetDirectoryName(archivePath);
            targetDir = !string.IsNullOrWhiteSpace(dir)
                ? dir
                : (!string.IsNullOrWhiteSpace(this.configService?.ExtractorTempDir)
                    ? this.configService.ExtractorTempDir
                    : (!string.IsNullOrWhiteSpace(this.configFileProvider?.ExtractorTempDir)
                        ? this.configFileProvider.ExtractorTempDir
                        : (!string.IsNullOrWhiteSpace(this.configService?.IncompleteDownloadDir)
                            ? this.configService.IncompleteDownloadDir
                            : Path.GetTempPath())));
        }

        this.diskProvider.EnsureFolder(targetDir);

        var normalizedDest = targetDir.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(normalizedDest))
        {
            normalizedDest = targetDir;
        }

        var existingFiles = this.GetAllFiles(normalizedDest);
        var existingDirectories = this.GetAllDirectories(normalizedDest);
        var passwordsToTry = BuildPasswordCandidateList(password, passwordCandidates);

        async Task<bool> ExtractActionAsync()
        {
            Exception lastException = null;

            foreach (var candidatePassword in passwordsToTry)
            {
                var backedUpOriginals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var readerOptions = new SharpCompress.Readers.ReaderOptions();
                    if (!string.IsNullOrEmpty(candidatePassword))
                    {
                        readerOptions.Password = candidatePassword;
                        this.logger.Info("SharpCompress extracting '{0}' to '{1}' with password candidate...", archivePath, targetDir);
                    }
                    else
                    {
                        this.logger.Info("SharpCompress extracting '{0}' to '{1}'...", archivePath, targetDir);
                    }

                    using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);

                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var entryKey = entry.Key;
                        if (string.IsNullOrWhiteSpace(entryKey))
                        {
                            continue;
                        }

                        var entryPath = entryKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                        string targetFilePath;
                        try
                        {
                            targetFilePath = Path.GetFullPath(Path.Combine(targetDir, entryPath));
                        }
                        catch (Exception ex)
                        {
                            this.logger.Warn(ex, "Invalid path in archive '{0}' for entry '{1}'. Skipping entry.", archivePath, entryKey);
                            continue;
                        }

                        if (!TorrentPathValidator.IsStrictSubPath(targetDir, targetFilePath))
                        {
                            this.logger.Warn("ZipSlip traversal detected in archive '{0}' for entry '{1}'. Skipping entry.", archivePath, entryKey);
                            continue;
                        }

                        var entryDir = Path.GetDirectoryName(targetFilePath);
                        if (!string.IsNullOrEmpty(entryDir))
                        {
                            this.diskProvider.EnsureFolder(entryDir);
                        }

                        var tempFilePath = targetFilePath + ".leecharr.tmp";

                        using (var entryStream = entry.OpenEntryStream())
                        using (var fileStream = new FileStream(
                            tempFilePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            this.bufferSize,
                            FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            await entryStream.CopyToAsync(fileStream, this.bufferSize, cancellationToken);
                        }

                        if (this.diskProvider.FileExists(targetFilePath))
                        {
                            var backupPath = targetFilePath + ".leecharr.bak";
                            this.diskProvider.MoveFile(targetFilePath, backupPath, overwrite: true);
                            backedUpOriginals[targetFilePath] = backupPath;
                        }

                        this.diskProvider.MoveFile(tempFilePath, targetFilePath, overwrite: true);

                        if (entry.LastModifiedTime.HasValue)
                        {
                            try
                            {
                                File.SetLastWriteTimeUtc(targetFilePath, entry.LastModifiedTime.Value.ToUniversalTime());
                            }
                            catch (Exception ex)
                            {
                                this.logger.Trace(ex, "Failed to update extracted file timestamp for '{0}'", targetFilePath);
                            }
                        }
                    }

                    this.DeleteBackupFiles(backedUpOriginals.Values);
                    this.logger.Info("SharpCompress successfully extracted archive '{0}'.", archivePath);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    this.RollbackExtractedFiles(normalizedDest, existingFiles, existingDirectories, backedUpOriginals);
                    this.logger.Warn("Extraction of '{0}' was canceled.", archivePath);
                    throw;
                }
                catch (Exception ex)
                {
                    this.RollbackExtractedFiles(normalizedDest, existingFiles, existingDirectories, backedUpOriginals);
                    lastException = ex;
                    if (passwordsToTry.Count > 1)
                    {
                        this.logger.Debug("SharpCompress password candidate failed for archive '{0}': {1}", archivePath, ex.Message);
                    }
                }
            }

            if (lastException != null)
            {
                this.logger.Error(lastException, "SharpCompress failed to extract archive: {0}", archivePath);
            }

            return false;
        }

        return await ExtractActionAsync();
    }

    private HashSet<string> GetAllFiles(string path)
    {
        if (this.diskProvider == null || !this.diskProvider.FolderExists(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return new HashSet<string>(this.diskProvider.GetFiles(path, recursive: true), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to snapshot files in '{0}'", path);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private HashSet<string> GetAllDirectories(string path)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (this.diskProvider == null || !this.diskProvider.FolderExists(path))
        {
            return dirs;
        }

        void Recurse(string current)
        {
            try
            {
                foreach (var dir in this.diskProvider.GetDirectories(current))
                {
                    if (dirs.Add(dir))
                    {
                        Recurse(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to recurse into directory '{0}' during snapshot", current);
            }
        }

        Recurse(path);
        return dirs;
    }

    private void DeleteBackupFiles(IEnumerable<string> backupPaths)
    {
        if (backupPaths == null)
        {
            return;
        }

        foreach (var backupPath in backupPaths)
        {
            try
            {
                if (this.diskProvider.FileExists(backupPath))
                {
                    this.diskProvider.DeleteFile(backupPath);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to delete extraction backup file: {0}", backupPath);
            }
        }
    }

    private void RollbackExtractedFiles(
        string targetDir,
        HashSet<string> existingFiles,
        HashSet<string> existingDirectories,
        Dictionary<string, string> backedUpOriginals)
    {
        if (this.diskProvider == null)
        {
            return;
        }

        if (backedUpOriginals != null)
        {
            foreach (var backup in backedUpOriginals)
            {
                try
                {
                    if (this.diskProvider.FileExists(backup.Key))
                    {
                        this.diskProvider.DeleteFile(backup.Key);
                    }

                    if (this.diskProvider.FileExists(backup.Value))
                    {
                        this.diskProvider.MoveFile(backup.Value, backup.Key, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to restore pre-existing file from backup: {0}", backup.Key);
                }
            }
        }

        try
        {
            if (!this.diskProvider.FolderExists(targetDir))
            {
                return;
            }

            var currentFiles = this.diskProvider.GetFiles(targetDir, recursive: true);
            foreach (var file in currentFiles)
            {
                if (!existingFiles.Contains(file))
                {
                    try
                    {
                        if (this.diskProvider.FileExists(file))
                        {
                            this.diskProvider.DeleteFile(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to clean up partial extracted file '{0}'", file);
                    }
                }
            }

            var currentDirectories = this.GetAllDirectories(targetDir)
                .OrderByDescending(d => d.Length);

            foreach (var dir in currentDirectories)
            {
                if (!existingDirectories.Contains(dir))
                {
                    try
                    {
                        if (this.diskProvider.FolderExists(dir) && this.diskProvider.FolderEmpty(dir))
                        {
                            this.diskProvider.DeleteFolder(dir, recursive: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Warn(ex, "Failed to clean up directory created during extraction: {0}", dir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to roll back extracted files in '{0}'", targetDir);
        }
    }

    private static List<string> BuildPasswordCandidateList(string password, IReadOnlyList<string> passwordCandidates)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(password))
        {
            candidates.Add(password);
        }

        if (passwordCandidates != null)
        {
            foreach (var candidate in passwordCandidates)
            {
                if (!string.IsNullOrEmpty(candidate) && !candidates.Contains(candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (candidates.Count == 0)
        {
            candidates.Add(null);
        }

        return candidates;
    }
}
