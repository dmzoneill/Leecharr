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

    void EnsureAccessiblePermissions(string path);

    string NormalizeCompletedSavePath(string rawSavePath, string category = null);
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
            if (this.diskProvider.FolderExists("/downloads"))
            {
                configured = "/downloads/incomplete";
            }
            else
            {
                var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                    ? this.appFolderInfo.AppDataFolder
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
                configured = Path.Combine(appData, "downloads", "incomplete");
            }
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
            if (this.diskProvider.FolderExists("/downloads"))
            {
                baseDir = "/downloads";
            }
            else
            {
                var appData = this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder)
                    ? this.appFolderInfo.AppDataFolder
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leecharr");
                baseDir = Path.Combine(appData, "downloads");
            }
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

            if (!string.IsNullOrWhiteSpace(torrentName) && !Path.HasExtension(torrentName))
            {
                var sanitizedFolder = TorrentPathValidator.SanitizeRelativePath(torrentName);
                if (string.IsNullOrWhiteSpace(sanitizedFolder))
                {
                    sanitizedFolder = torrentName;
                }

                var targetDir = Path.Combine(completedDir, sanitizedFolder);
                if (!this.diskProvider.FolderExists(targetDir))
                {
                    this.diskProvider.CreateFolder(targetDir);
                }

                finalDestination = Path.Combine(targetDir, sanitizedTarget);
            }
            else
            {
                finalDestination = Path.Combine(completedDir, sanitizedTarget);
            }
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
            if (string.Equals(Path.GetFullPath(actualSource), Path.GetFullPath(finalDestination), StringComparison.OrdinalIgnoreCase))
            {
                this.StripIncompleteExtensions(finalDestination);
                return true;
            }
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to resolve full path when comparing {Source} and {Destination}", actualSource, finalDestination);
        }

        try
        {
            this.FlushBuffersToDisk(actualSource);

            if (this.diskProvider.FileExists(actualSource))
            {
                if (this.diskProvider.FileExists(finalDestination) || this.diskProvider.FolderExists(finalDestination))
                {
                    finalDestination = this.ResolveNonCollidingFilePath(finalDestination);
                }

                this.logger.Info("Moving completed torrent from '{0}' to '{1}'", actualSource, finalDestination);

                try
                {
                    this.diskProvider.MoveFile(actualSource, finalDestination, overwrite: false);
                }
                catch (IOException ioEx)
                {
                    this.logger.Info(ioEx, "MoveFile failed from '{0}' to '{1}'. Falling back to copy and delete.", actualSource, finalDestination);
                    this.SafeCopyFileWithStaging(actualSource, finalDestination);
                    this.diskProvider.DeleteFile(actualSource);
                }
            }
            else if (this.diskProvider.FolderExists(actualSource))
            {
                if (this.diskProvider.FolderExists(finalDestination) || this.diskProvider.FileExists(finalDestination))
                {
                    finalDestination = this.ResolveNonCollidingFolderPath(finalDestination);
                }

                this.logger.Info("Moving completed torrent from '{0}' to '{1}'", actualSource, finalDestination);

                this.MoveFolderWithFallback(actualSource, finalDestination);
            }

            this.StripIncompleteExtensions(finalDestination);
            this.EnsureAccessiblePermissions(finalDestination);
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
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) &&
            (this.diskProvider.FolderExists(destination) || this.diskProvider.FileExists(destination)))
        {
            destination = this.ResolveNonCollidingFolderPath(destination);
        }

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
                if (this.diskProvider.FileExists(destFile) || this.diskProvider.FolderExists(destFile))
                {
                    destFile = this.ResolveNonCollidingFilePath(destFile);
                }

                this.SafeCopyFileWithStaging(file, destFile);
            }
        }
    }

    private void SafeCopyFileWithStaging(string source, string destination)
    {
        var tempFile = destination + ".leecharr.tmp";
        try
        {
            if (this.diskProvider.FileExists(tempFile))
            {
                this.diskProvider.DeleteFile(tempFile);
            }

            this.diskProvider.CopyFile(source, tempFile, overwrite: true);
            this.FlushSingleFileBufferToDisk(tempFile);
            this.diskProvider.MoveFile(tempFile, destination, overwrite: false);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to copy file from '{0}' to temporary staging file '{1}' or move to '{2}'", source, tempFile, destination);
            try
            {
                if (this.diskProvider.FileExists(tempFile))
                {
                    this.diskProvider.DeleteFile(tempFile);
                }
            }
            catch (Exception cleanupEx)
            {
                this.logger.Debug(cleanupEx, "Failed to clean up temporary staging file '{0}' after copy failure", tempFile);
            }

            throw;
        }
    }

    private string ResolveNonCollidingFilePath(string destinationPath)
    {
        if (!this.diskProvider.FileExists(destinationPath) && !this.diskProvider.FolderExists(destinationPath))
        {
            return destinationPath;
        }

        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);

        var counter = 1;
        while (true)
        {
            var candidate = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
            if (!this.diskProvider.FileExists(candidate) && !this.diskProvider.FolderExists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    private string ResolveNonCollidingFolderPath(string destinationPath)
    {
        var trimmed = destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!this.diskProvider.FolderExists(trimmed) && !this.diskProvider.FileExists(trimmed))
        {
            return trimmed;
        }

        var parent = Path.GetDirectoryName(trimmed) ?? string.Empty;
        var folderName = Path.GetFileName(trimmed);

        var counter = 1;
        while (true)
        {
            var candidate = Path.Combine(parent, $"{folderName}_{counter}");
            if (!this.diskProvider.FolderExists(candidate) && !this.diskProvider.FileExists(candidate))
            {
                return candidate;
            }

            counter++;
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
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                fs.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to flush OS file buffer for '{0}'", filePath);
        }
    }

    public void EnsureAccessiblePermissions(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var (dirMode, fileMode) = this.GetConfiguredUnixModes();
            this.ApplyUnixPermissions(path, dirMode, fileMode);
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to apply permissions for '{0}'", path);
        }
    }

    private void ApplyUnixPermissions(string path, UnixFileMode dirMode, UnixFileMode fileMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (this.diskProvider.FolderExists(path))
        {
            try
            {
                File.SetUnixFileMode(path, dirMode);
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Unable to set Unix file mode on directory {Path}", path);
            }

            var dirs = this.diskProvider.GetDirectories(path);
            if (dirs != null)
            {
                foreach (var d in dirs)
                {
                    this.ApplyUnixPermissions(d, dirMode, fileMode);
                }
            }

            var files = this.diskProvider.GetFiles(path, false);
            if (files != null)
            {
                foreach (var f in files)
                {
                    try
                    {
                        File.SetUnixFileMode(f, fileMode);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Trace(ex, "Unable to set Unix file mode on file {File}", f);
                    }
                }
            }
        }
        else if (this.diskProvider.FileExists(path))
        {
            try
            {
                File.SetUnixFileMode(path, fileMode);
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Unable to set Unix file mode on file {Path}", path);
            }
        }
    }

    private (UnixFileMode DirMode, UnixFileMode FileMode) GetConfiguredUnixModes()
    {
        const int defaultUmask = 18; // octal 022
        var umask = defaultUmask;

        var configuredUmask = this.configService?.Umask;
        if (!string.IsNullOrWhiteSpace(configuredUmask))
        {
            try
            {
                umask = Convert.ToInt32(configuredUmask.Trim(), 8);
            }
            catch
            {
                umask = defaultUmask;
            }
        }

        var dirModeInt = 511 & ~umask; // 0777 & ~umask
        var fileModeInt = 438 & ~umask; // 0666 & ~umask

        var configuredFolderChmod = this.configService?.GetValue("FolderChmod", string.Empty);
        if (!string.IsNullOrWhiteSpace(configuredFolderChmod))
        {
            try
            {
                dirModeInt = Convert.ToInt32(configuredFolderChmod.Trim(), 8);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Invalid octal value for FolderChmod: {Value}", configuredFolderChmod);
            }
        }

        var configuredFileChmod = this.configService?.GetValue("FileChmod", string.Empty);
        if (!string.IsNullOrWhiteSpace(configuredFileChmod))
        {
            try
            {
                fileModeInt = Convert.ToInt32(configuredFileChmod.Trim(), 8);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Invalid octal value for FileChmod: {Value}", configuredFileChmod);
            }
        }

        return ((UnixFileMode)dirModeInt, (UnixFileMode)fileModeInt);
    }

    public string NormalizeCompletedSavePath(string rawSavePath, string category = null)
    {
        var completedDir = this.GetCompletedDirectory(category);
        if (string.IsNullOrWhiteSpace(rawSavePath))
        {
            return completedDir;
        }

        var trimmedRaw = rawSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configuredIncomplete = this.configService?.IncompleteDownloadDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string incompleteDir = null;
        try
        {
            incompleteDir = this.GetIncompleteDirectory()?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Unable to resolve incomplete directory for path normalization");
        }

        var incompleteCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(incompleteDir))
        {
            incompleteCandidates.Add(incompleteDir);
        }

        if (!string.IsNullOrWhiteSpace(configuredIncomplete) && !incompleteCandidates.Contains(configuredIncomplete, StringComparer.OrdinalIgnoreCase))
        {
            incompleteCandidates.Add(configuredIncomplete);
        }

        if (!incompleteCandidates.Contains("/downloads/incomplete", StringComparer.OrdinalIgnoreCase))
        {
            incompleteCandidates.Add("/downloads/incomplete");
        }

        foreach (var inc in incompleteCandidates)
        {
            if (string.Equals(trimmedRaw, inc, StringComparison.OrdinalIgnoreCase))
            {
                return completedDir;
            }

            if (trimmedRaw.StartsWith(inc + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                trimmedRaw.StartsWith(inc + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = trimmedRaw.Substring(inc.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !string.IsNullOrWhiteSpace(relative) ? Path.Combine(completedDir, relative) : completedDir;
            }
        }

        if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(completedDir))
        {
            var baseDir = this.configService?.DownloadDir ?? "/downloads";
            var trimmedBase = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var catDir1 = trimmedBase + "/" + category;
            var catDir2 = trimmedBase + "\\" + category;

            if (string.Equals(trimmedRaw, catDir1, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedRaw, catDir2, StringComparison.OrdinalIgnoreCase))
            {
                return completedDir.Equals(catDir1, StringComparison.OrdinalIgnoreCase) || completedDir.Equals(catDir2, StringComparison.OrdinalIgnoreCase)
                    ? trimmedBase
                    : completedDir;
            }

            if (trimmedRaw.StartsWith(catDir1 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                trimmedRaw.StartsWith(catDir1 + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = trimmedRaw.Substring(catDir1.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetBase = completedDir.Equals(catDir1, StringComparison.OrdinalIgnoreCase) || completedDir.Equals(catDir2, StringComparison.OrdinalIgnoreCase)
                    ? trimmedBase
                    : completedDir;
                return !string.IsNullOrWhiteSpace(relative) ? Path.Combine(targetBase, relative) : targetBase;
            }

            if (trimmedRaw.StartsWith(catDir2 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                trimmedRaw.StartsWith(catDir2 + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = trimmedRaw.Substring(catDir2.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetBase = completedDir.Equals(catDir1, StringComparison.OrdinalIgnoreCase) || completedDir.Equals(catDir2, StringComparison.OrdinalIgnoreCase)
                    ? trimmedBase
                    : completedDir;
                return !string.IsNullOrWhiteSpace(relative) ? Path.Combine(targetBase, relative) : targetBase;
            }
        }

        return rawSavePath;
    }
}
