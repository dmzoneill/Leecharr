// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
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
    private readonly IConfigService configService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;

    public StoragePathService(
        IConfigService configService,
        ICategoryService categoryService,
        IDiskProvider diskProvider)
    {
        this.configService = configService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public string GetIncompleteDirectory()
    {
        var configured = this.configService.IncompleteDownloadDir;
        if (string.IsNullOrWhiteSpace(configured))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            configured = Path.Combine(appData, "Leecharr", "downloads", "incomplete");
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            baseDir = Path.Combine(appData, "Leecharr", "downloads");
        }

        var target = string.IsNullOrWhiteSpace(category)
            ? baseDir
            : Path.Combine(baseDir, category);

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
                this.diskProvider.MoveFolder(sourcePath, finalDestination);
            }
            else if (this.diskProvider.FileExists(sourcePath))
            {
                this.diskProvider.MoveFile(sourcePath, finalDestination);
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
            var ext = this.configService.IncompleteExtension;
            if (this.diskProvider.FileExists(targetDirectoryOrFile))
            {
                if (targetDirectoryOrFile.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    var cleanPath = targetDirectoryOrFile[..^ext.Length];
                    this.diskProvider.MoveFile(targetDirectoryOrFile, cleanPath);
                }
                else if (targetDirectoryOrFile.EndsWith(".!mt", StringComparison.OrdinalIgnoreCase))
                {
                    var cleanPath = targetDirectoryOrFile[..^4];
                    this.diskProvider.MoveFile(targetDirectoryOrFile, cleanPath);
                }
            }
            else if (this.diskProvider.FolderExists(targetDirectoryOrFile))
            {
                var files = this.diskProvider.GetFiles(targetDirectoryOrFile, true);
                foreach (var file in files)
                {
                    if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        var cleanPath = file[..^ext.Length];
                        this.diskProvider.MoveFile(file, cleanPath);
                    }
                    else if (file.EndsWith(".!mt", StringComparison.OrdinalIgnoreCase))
                    {
                        var cleanPath = file[..^4];
                        this.diskProvider.MoveFile(file, cleanPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error stripping incomplete extensions from '{0}'", targetDirectoryOrFile);
        }
    }
}
