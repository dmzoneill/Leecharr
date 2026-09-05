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

namespace NzbDrone.Core.Download;

public interface IStoragePathService
{
    string GetIncompleteDirectory();

    string GetCompletedDirectory(string category);

    string GetWorkingPath(string infoHash, string torrentName);

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

    public string GetWorkingPath(string infoHash, string torrentName)
    {
        var incompleteDir = this.GetIncompleteDirectory();
        return Path.Combine(incompleteDir, torrentName);
    }

    public string GetFinalPath(string category, string torrentName)
    {
        var completedDir = this.GetCompletedDirectory(category);
        return Path.Combine(completedDir, torrentName);
    }

    public bool MoveToCompleted(string sourcePath, string category, string torrentName, out string finalDestination)
    {
        finalDestination = this.GetFinalPath(category, torrentName);

        if (string.Equals(sourcePath, finalDestination, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!this.diskProvider.FileExists(sourcePath) && !this.diskProvider.FolderExists(sourcePath))
        {
            this.logger.Warn("Source path does not exist for moving: {0}", sourcePath);
            return false;
        }

        try
        {
            this.logger.Info("Moving completed torrent from '{0}' to '{1}'", sourcePath, finalDestination);

            if (this.diskProvider.FolderExists(sourcePath))
            {
                this.MoveFolderWithFallback(sourcePath, finalDestination);
            }
            else if (this.diskProvider.FileExists(sourcePath))
            {
                try
                {
                    this.diskProvider.MoveFile(sourcePath, finalDestination, overwrite: true);
                }
                catch (IOException ioEx)
                {
                    this.logger.Info(ioEx, "MoveFile failed from '{0}' to '{1}'. Falling back to copy and delete.", sourcePath, finalDestination);
                    this.diskProvider.CopyFile(sourcePath, finalDestination, overwrite: true);
                    this.diskProvider.DeleteFile(sourcePath);
                }
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
            this.logger.Warn(ex, "Error stripping incomplete extensions from '{0}'", targetDirectoryOrFile);
        }
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
}
