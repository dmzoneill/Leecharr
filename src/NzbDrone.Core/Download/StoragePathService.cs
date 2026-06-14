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
}

public class StoragePathService : IStoragePathService
{
    private readonly IConfigService _configService;
    private readonly ICategoryService _categoryService;
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger;

    public StoragePathService(
        IConfigService configService,
        ICategoryService categoryService,
        IDiskProvider diskProvider)
    {
        _configService = configService;
        _categoryService = categoryService;
        _diskProvider = diskProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public string GetIncompleteDirectory()
    {
        var configured = _configService.IncompleteDownloadDir;
        if (string.IsNullOrWhiteSpace(configured))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            configured = Path.Combine(appData, "Leecharr", "downloads", "incomplete");
        }

        if (!_diskProvider.FolderExists(configured))
        {
            _diskProvider.CreateFolder(configured);
        }

        return configured;
    }

    public string GetCompletedDirectory(string category)
    {
        var categoryPath = _categoryService.GetSavePathForCategory(category);
        if (!string.IsNullOrWhiteSpace(categoryPath))
        {
            if (!_diskProvider.FolderExists(categoryPath))
            {
                _diskProvider.CreateFolder(categoryPath);
            }

            return categoryPath;
        }

        var baseDir = _configService.DownloadDir;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            baseDir = Path.Combine(appData, "Leecharr", "downloads");
        }

        var target = string.IsNullOrWhiteSpace(category)
            ? baseDir
            : Path.Combine(baseDir, category);

        if (!_diskProvider.FolderExists(target))
        {
            _diskProvider.CreateFolder(target);
        }

        return target;
    }

    public string GetWorkingPath(string infoHash, string torrentName)
    {
        var incompleteDir = GetIncompleteDirectory();
        return Path.Combine(incompleteDir, torrentName);
    }

    public string GetFinalPath(string category, string torrentName)
    {
        var completedDir = GetCompletedDirectory(category);
        return Path.Combine(completedDir, torrentName);
    }

    public bool MoveToCompleted(string sourcePath, string category, string torrentName, out string finalDestination)
    {
        finalDestination = GetFinalPath(category, torrentName);

        if (string.Equals(sourcePath, finalDestination, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_diskProvider.FileExists(sourcePath) && !_diskProvider.FolderExists(sourcePath))
        {
            _logger.Warn("Source path does not exist for moving: {0}", sourcePath);
            return false;
        }

        try
        {
            _logger.Info("Moving completed torrent from '{0}' to '{1}'", sourcePath, finalDestination);

            if (_diskProvider.FolderExists(sourcePath))
            {
                _diskProvider.MoveFolder(sourcePath, finalDestination);
            }
            else if (_diskProvider.FileExists(sourcePath))
            {
                _diskProvider.MoveFile(sourcePath, finalDestination);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to move completed torrent from '{0}' to '{1}'", sourcePath, finalDestination);
            return false;
        }
    }
}
