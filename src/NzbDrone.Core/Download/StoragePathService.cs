// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Download;

public interface IStoragePathService
{
    string GetIncompleteDirectory();

    string GetCompletedDirectory(string category);

    string GetWorkingPath(string infoHash, string torrentName, string category = null);

    string GetFinalPath(string category, string torrentName);

    bool MoveToCompleted(string sourcePath, string category, string torrentName, out string finalDestination);

    void StripIncompleteExtensions(string targetDirectoryOrFile);
}

public class StoragePathService : IStoragePathService
{
    private static readonly string[] DefaultIncompleteExtensions = new[] { ".!mt", ".!leech", ".incomplete" };

    private readonly IConfigService configService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger;

    public StoragePathService(
        IConfigService configService,
        ICategoryService categoryService,
        IDiskProvider diskProvider,
        IAppFolderInfo appFolderInfo = null)
    {
        this.configService = configService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.appFolderInfo = appFolderInfo;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public string GetIncompleteDirectory()
    {
        var configured = this.configService.IncompleteDownloadDir;
        if (string.IsNullOrWhiteSpace(configured))
        {
            var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                ? this.appFolderInfo.AppDataFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
            configured = Path.Combine(appData, "downloads", "incomplete");
        }

        if (!this.diskProvider.FolderExists(configured))
        {
            this.diskProvider.CreateFolder(configured);
        }

        return configured;
    }

    public string GetCompletedDirectory(string category)
    {
        var categoryPath = this.categoryService.GetSavePathForCategory(category);
        if (!string.IsNullOrWhiteSpace(categoryPath))
        {
            if (!this.diskProvider.FolderExists(categoryPath))
            {
                this.diskProvider.CreateFolder(categoryPath);
            }

            return categoryPath;
        }

        var baseDir = this.configService.DownloadDir;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                ? this.appFolderInfo.AppDataFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
            baseDir = Path.Combine(appData, "downloads");
        }

        var target = baseDir;

        if (!this.diskProvider.FolderExists(target))
        {
            this.diskProvider.CreateFolder(target);
        }

        return target;
    }

    public string GetWorkingPath(string infoHash, string torrentName, string category = null)
    {
        if (!this.configService.EnableIncompleteDir)
        {
            return this.GetFinalPath(category, torrentName);
        }

        var incompleteDir = this.GetIncompleteDirectory();
        if (string.IsNullOrWhiteSpace(torrentName))
        {
            return incompleteDir;
        }

        var sanitizedName = TorrentPathValidator.SanitizeRelativePath(torrentName);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = torrentName;
        }

        return Path.Combine(incompleteDir, sanitizedName);
    }

    public string GetFinalPath(string category, string torrentName)
    {
        var completedDir = this.GetCompletedDirectory(category);
        if (string.IsNullOrWhiteSpace(torrentName))
        {
            return completedDir;
        }

        var sanitizedName = TorrentPathValidator.SanitizeRelativePath(torrentName);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = torrentName;
        }

        return Path.Combine(completedDir, sanitizedName);
    }

    public bool MoveToCompleted(string sourcePath, string category, string torrentName, out string finalDestination)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(torrentName))
        {
            finalDestination = null;
            return false;
        }

        finalDestination = this.GetFinalPath(category, torrentName);
        if (string.Equals(sourcePath, finalDestination, StringComparison.OrdinalIgnoreCase))
        {
            this.StripIncompleteExtensions(finalDestination);
            return true;
        }

        var completedDir = this.GetCompletedDirectory(category);
        try
        {
            var incompleteDir = this.GetIncompleteDirectory();
            if (!string.IsNullOrWhiteSpace(incompleteDir) &&
                string.Equals(
                    Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(incompleteDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                this.logger.Warn("Source path '{0}' is the incomplete root directory; refusing to move entire directory.", sourcePath);
                return false;
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to normalize or compare sourcePath '{0}' against incomplete directory.", sourcePath);
            return false;
        }

        var candidateExtensions = this.GetCandidateIncompleteExtensions();
        var actualSource = sourcePath;
        if (!this.diskProvider.FileExists(actualSource) && !this.diskProvider.FolderExists(actualSource))
        {
            foreach (var ext in candidateExtensions)
            {
                if (this.diskProvider.FileExists(sourcePath + ext))
                {
                    actualSource = sourcePath + ext;
                    break;
                }
            }
        }

        if (!this.diskProvider.FileExists(actualSource) && !this.diskProvider.FolderExists(actualSource))
        {
            this.logger.Warn("Source path does not exist for moving: {0}", sourcePath);
            finalDestination = this.GetFinalPath(category, torrentName);
            return false;
        }

        if (this.diskProvider.FileExists(actualSource))
        {
            var rawFileName = Path.GetFileName(actualSource);
            var cleanFileName = this.StripIncompleteExtensionFromFileName(rawFileName, candidateExtensions);
            var sourceExt = Path.GetExtension(cleanFileName);

            string targetFileName;
            if (string.IsNullOrWhiteSpace(torrentName))
            {
                targetFileName = cleanFileName;
            }
            else if (!string.IsNullOrWhiteSpace(sourceExt) && !torrentName.EndsWith(sourceExt, StringComparison.OrdinalIgnoreCase))
            {
                targetFileName = torrentName + sourceExt;
            }
            else
            {
                targetFileName = torrentName;
            }

            var sanitizedTarget = TorrentPathValidator.SanitizeRelativePath(targetFileName);
            if (string.IsNullOrWhiteSpace(sanitizedTarget))
            {
                sanitizedTarget = targetFileName;
            }

            finalDestination = Path.Combine(completedDir, sanitizedTarget);
        }
        else
        {
            var sanitizedTarget = TorrentPathValidator.SanitizeRelativePath(torrentName);
            if (string.IsNullOrWhiteSpace(sanitizedTarget))
            {
                sanitizedTarget = torrentName;
            }

            finalDestination = Path.Combine(completedDir, sanitizedTarget);
        }

        if (string.Equals(sourcePath, finalDestination, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actualSource, finalDestination, StringComparison.OrdinalIgnoreCase))
        {
            this.StripIncompleteExtensions(finalDestination);
            return true;
        }

        try
        {
            this.FlushBuffersToDisk(actualSource);
            this.logger.Info("Moving completed torrent from '{0}' to '{1}'", actualSource, finalDestination);

            if (this.diskProvider.FileExists(actualSource))
            {
                try
                {
                    this.diskProvider.MoveFile(actualSource, finalDestination, overwrite: true);
                }
                catch (IOException ioEx)
                {
                    this.logger.Info(ioEx, "MoveFile failed from '{0}' to '{1}'. Falling back to copy and delete.", actualSource, finalDestination);
                    this.diskProvider.CopyFile(actualSource, finalDestination, overwrite: true);
                    this.diskProvider.DeleteFile(actualSource);
                }
            }
            else if (this.diskProvider.FolderExists(actualSource))
            {
                this.MoveFolderWithFallback(actualSource, finalDestination);
            }

            this.StripIncompleteExtensions(finalDestination);
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to move completed torrent from '{0}' to '{1}'", sourcePath, finalDestination);
            return false;
        }
    }

    public void StripIncompleteExtensions(string targetDirectoryOrFile)
    {
        if (string.IsNullOrWhiteSpace(targetDirectoryOrFile))
        {
            return;
        }

        try
        {
            var candidateExtensions = this.GetCandidateIncompleteExtensions();

            if (this.diskProvider.FolderExists(targetDirectoryOrFile))
            {
                var files = this.diskProvider.GetFiles(targetDirectoryOrFile, true);
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        foreach (var ext in candidateExtensions)
                        {
                            if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                            {
                                var cleanPath = file[..^ext.Length];
                                this.diskProvider.MoveFile(file, cleanPath, overwrite: true);
                                break;
                            }
                        }
                    }
                }

                return;
            }

            // Single-file case 1: targetDirectoryOrFile is the clean path (e.g. /downloads/Movie.mkv)
            // and the file on disk has the incomplete extension appended (e.g. /downloads/Movie.mkv.!mt)
            foreach (var ext in candidateExtensions)
            {
                var incompletePath = targetDirectoryOrFile + ext;
                if (this.diskProvider.FileExists(incompletePath))
                {
                    this.diskProvider.MoveFile(incompletePath, targetDirectoryOrFile, overwrite: true);
                    return;
                }
            }

            // Single-file case 2: targetDirectoryOrFile itself already includes the incomplete extension
            if (this.diskProvider.FileExists(targetDirectoryOrFile))
            {
                foreach (var ext in candidateExtensions)
                {
                    if (targetDirectoryOrFile.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        var cleanPath = targetDirectoryOrFile[..^ext.Length];
                        this.diskProvider.MoveFile(targetDirectoryOrFile, cleanPath, overwrite: true);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to strip incomplete extension from {0}", targetDirectoryOrFile);
        }
    }

    private List<string> GetCandidateIncompleteExtensions()
    {
        var candidateExtensions = new List<string>();
        var configuredExt = this.configService.IncompleteExtension;
        if (!string.IsNullOrWhiteSpace(configuredExt))
        {
            candidateExtensions.Add(configuredExt);
        }

        foreach (var ext in DefaultIncompleteExtensions)
        {
            if (!candidateExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                candidateExtensions.Add(ext);
            }
        }

        return candidateExtensions;
    }

    private string StripIncompleteExtensionFromFileName(string fileName, List<string> candidateExtensions)
    {
        foreach (var ext in candidateExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^ext.Length];
            }
        }

        return fileName;
    }

    private void MoveFolderWithFallback(string source, string destination)
    {
        try
        {
            this.diskProvider.MoveFolder(source, destination);
        }
        catch (IOException ioEx)
        {
            this.logger.Info(ioEx, "MoveFolder failed (cross-volume) from '{0}' to '{1}'. Falling back to recursive copy and delete.", source, destination);
            this.CopyFolderRecursive(source, destination);
            this.diskProvider.DeleteFolder(source, true);
        }
    }

    private void CopyFolderRecursive(string source, string destination)
    {
        this.diskProvider.EnsureFolder(destination);

        var dirs = this.diskProvider.GetDirectories(source);
        if (dirs != null)
        {
            foreach (var dir in dirs)
            {
                var dirName = Path.GetFileName(dir);
                var destSubDir = Path.Combine(destination, dirName);
                this.CopyFolderRecursive(dir, destSubDir);
            }
        }

        var files = this.diskProvider.GetFiles(source, false);
        if (files != null)
        {
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destination, fileName);
                this.diskProvider.CopyFile(file, destFile, overwrite: true);
            }
        }
    }

    private void FlushBuffersToDisk(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (this.diskProvider.FileExists(path))
            {
                this.FlushSingleFileBufferToDisk(path);
            }
            else if (this.diskProvider.FolderExists(path))
            {
                var files = this.diskProvider.GetFiles(path, true);
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        this.FlushSingleFileBufferToDisk(file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Non-critical error while committing disk write cache for '{0}'", path);
        }
    }

    private void FlushSingleFileBufferToDisk(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                fs.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to flush OS file buffer for '{0}'", filePath);
        }
    }
}
