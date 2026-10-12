// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Extraction;

internal static class NativeExtractorDestinationRollback
{
    internal sealed class Snapshot
    {
        public HashSet<string> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Directories { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> BackupsByOriginalPath { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public string BackupDirectory { get; init; }

        public bool IsReliable { get; init; } = true;
    }

    internal static Snapshot Capture(IDiskProvider diskProvider, string destinationPath, Logger logger)
    {
        if (diskProvider == null || string.IsNullOrWhiteSpace(destinationPath) || !diskProvider.FolderExists(destinationPath))
        {
            return new Snapshot();
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesReliable = SnapshotFiles(diskProvider, destinationPath, files, logger);
        var directoriesReliable = SnapshotDirectories(diskProvider, destinationPath, directories, logger);
        var reliable = filesReliable && directoriesReliable;

        string backupDirectory = null;
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (reliable && files.Count > 0)
        {
            backupDirectory = Path.Combine(Path.GetTempPath(), "leecharr-extract-backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                diskProvider.EnsureFolder(backupDirectory);
                foreach (var file in files)
                {
                    if (!diskProvider.FileExists(file))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(destinationPath, file);
                    if (relativePath.StartsWith("..", StringComparison.Ordinal))
                    {
                        reliable = false;
                        break;
                    }

                    var backupPath = Path.Combine(backupDirectory, relativePath);
                    var backupParent = Path.GetDirectoryName(backupPath);
                    if (!string.IsNullOrEmpty(backupParent))
                    {
                        diskProvider.EnsureFolder(backupParent);
                    }

                    diskProvider.CopyFile(file, backupPath, overwrite: true);
                    backups[file] = backupPath;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "Failed to back up pre-existing files in '{0}' before extraction", destinationPath);
                reliable = false;
            }
        }

        var snapshot = new Snapshot
        {
            Files = files,
            Directories = directories,
            BackupsByOriginalPath = backups,
            BackupDirectory = backupDirectory,
            IsReliable = reliable,
        };

        if (!snapshot.IsReliable)
        {
            logger?.Warn("Pre-extraction snapshot of '{0}' was incomplete; rollback will be skipped if extraction fails.", destinationPath);
            ReleaseBackups(diskProvider, snapshot, logger);
        }

        return snapshot;
    }

    internal static void Rollback(IDiskProvider diskProvider, string targetDir, Snapshot snapshot, Logger logger)
    {
        if (diskProvider == null || snapshot == null)
        {
            return;
        }

        if (!snapshot.IsReliable)
        {
            logger?.Warn("Skipping rollback of extracted files in '{0}' because the pre-extraction snapshot was incomplete.", targetDir);
            return;
        }

        try
        {
            if (!diskProvider.FolderExists(targetDir))
            {
                return;
            }

            var currentFiles = diskProvider.GetFiles(targetDir, recursive: true);
            foreach (var file in currentFiles)
            {
                if (!snapshot.Files.Contains(file))
                {
                    try
                    {
                        if (diskProvider.FileExists(file))
                        {
                            diskProvider.DeleteFile(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "Failed to clean up partial extracted file '{0}'", file);
                    }
                }
            }

            foreach (var entry in snapshot.BackupsByOriginalPath)
            {
                var originalPath = entry.Key;
                var backupPath = entry.Value;

                try
                {
                    if (!diskProvider.FileExists(backupPath))
                    {
                        continue;
                    }

                    var parent = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        diskProvider.EnsureFolder(parent);
                    }

                    diskProvider.CopyFile(backupPath, originalPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "Failed to restore pre-existing file '{0}' after failed extraction", originalPath);
                }
            }

            var currentDirectories = GetAllDirectories(diskProvider, targetDir)
                .OrderByDescending(d => d.Length);

            foreach (var dir in currentDirectories)
            {
                if (!snapshot.Directories.Contains(dir))
                {
                    try
                    {
                        if (diskProvider.FolderExists(dir) && diskProvider.FolderEmpty(dir))
                        {
                            diskProvider.DeleteFolder(dir, recursive: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "Failed to clean up newly created directory '{0}'", dir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Warn(ex, "Failed to rollback extracted files in: {0}", targetDir);
        }
    }

    internal static void ReleaseBackups(IDiskProvider diskProvider, Snapshot snapshot, Logger logger)
    {
        if (diskProvider == null || snapshot == null || string.IsNullOrWhiteSpace(snapshot.BackupDirectory))
        {
            return;
        }

        try
        {
            if (diskProvider.FolderExists(snapshot.BackupDirectory))
            {
                diskProvider.DeleteFolder(snapshot.BackupDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger?.Warn(ex, "Failed to delete extraction backup directory '{0}'", snapshot.BackupDirectory);
        }
    }

    private static bool SnapshotFiles(IDiskProvider diskProvider, string current, HashSet<string> files, Logger logger)
    {
        var reliable = true;

        try
        {
            foreach (var file in diskProvider.GetFiles(current, recursive: false))
            {
                files.Add(file);
            }
        }
        catch (Exception ex)
        {
            logger?.Trace(ex, "Failed to list files in '{Directory}' during snapshot", current);
            reliable = false;
        }

        try
        {
            foreach (var dir in diskProvider.GetDirectories(current))
            {
                if (!SnapshotFiles(diskProvider, dir, files, logger))
                {
                    reliable = false;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Trace(ex, "Failed to list subdirectories of '{Directory}' during file snapshot", current);
            reliable = false;
        }

        return reliable;
    }

    private static bool SnapshotDirectories(IDiskProvider diskProvider, string current, HashSet<string> directories, Logger logger)
    {
        var reliable = true;

        try
        {
            foreach (var dir in diskProvider.GetDirectories(current))
            {
                if (directories.Add(dir) && !SnapshotDirectories(diskProvider, dir, directories, logger))
                {
                    reliable = false;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Trace(ex, "Failed to recurse into directory '{Directory}' during snapshot", current);
            reliable = false;
        }

        return reliable;
    }

    private static HashSet<string> GetAllDirectories(IDiskProvider diskProvider, string path)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (diskProvider == null || !diskProvider.FolderExists(path))
        {
            return dirs;
        }

        SnapshotDirectories(diskProvider, path, dirs, null);
        return dirs;
    }
}
