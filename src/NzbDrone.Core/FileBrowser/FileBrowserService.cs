using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.FileBrowser;

public class FileBrowserEntry
{
    public string Name { get; set; }

    public string Path { get; set; }

    public bool IsDirectory { get; set; }

    public long Size { get; set; }

    public DateTime? Modified { get; set; }

    public string Extension { get; set; }
}

public class FileBrowserListing
{
    public string Path { get; set; }

    public string Parent { get; set; }

    public bool Exists { get; set; }

    public bool IsRoot { get; set; }

    public string DefaultPath { get; set; }

    public List<FileBrowserEntry> Entries { get; set; } = new();
}

public interface IFileBrowserService
{
    FileBrowserListing ListDirectory(string path);

    void CreateDirectory(string path);

    void Rename(string path, string newName);

    void Copy(string sourcePath, string destinationDirectory);

    void Move(string sourcePath, string destinationDirectory);

    void Delete(string path);

    string ResolvePath(string path);

    string GetParentPath(string path);

    bool IsRootPath(string path);

    bool IsRootOrSystemDirectory(string path);
}

public class FileBrowserService : IFileBrowserService
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly IDiskProvider diskProvider;
    private readonly IConfigService configService;
    private readonly NzbDrone.Common.EnvironmentInfo.IAppFolderInfo appFolderInfo;

    public FileBrowserService(
        IDiskProvider diskProvider,
        IConfigService configService,
        NzbDrone.Common.EnvironmentInfo.IAppFolderInfo appFolderInfo = null)
    {
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
    }

    public FileBrowserListing ListDirectory(string path)
    {
        var target = this.ResolvePath(path);

        var listing = new FileBrowserListing
        {
            Path = target,
            DefaultPath = this.GetDefaultPath(),
            IsRoot = this.IsRootPath(target),
            Parent = this.GetParentPath(target),
            Exists = this.diskProvider.FolderExists(target),
            Entries = new List<FileBrowserEntry>(),
        };

        if (!listing.Exists)
        {
            return listing;
        }

        var entries = new List<FileBrowserEntry>();

        try
        {
            foreach (var dirPath in this.diskProvider.GetDirectories(target))
            {
                try
                {
                    var info = new DirectoryInfo(dirPath);
                    entries.Add(new FileBrowserEntry
                    {
                        Name = info.Name,
                        Path = dirPath,
                        IsDirectory = true,
                        Modified = info.LastWriteTime == DateTime.MinValue ? (DateTime?)null : info.LastWriteTime,
                    });
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to read directory info for '{0}'", dirPath);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            this.logger.Trace(ex, "Unauthorized access enumerating directories in '{0}'", target);
        }
        catch (SecurityException ex)
        {
            this.logger.Trace(ex, "Security error enumerating directories in '{0}'", target);
        }

        try
        {
            foreach (var filePath in this.diskProvider.GetFiles(target, false))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    entries.Add(new FileBrowserEntry
                    {
                        Name = info.Name,
                        Path = filePath,
                        IsDirectory = false,
                        Size = info.Length,
                        Modified = info.LastWriteTime == DateTime.MinValue ? (DateTime?)null : info.LastWriteTime,
                        Extension = info.Extension?.TrimStart('.'),
                    });
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to read file info for '{0}'", filePath);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            this.logger.Trace(ex, "Unauthorized access enumerating files in '{0}'", target);
        }
        catch (SecurityException ex)
        {
            this.logger.Trace(ex, "Security error enumerating files in '{0}'", target);
        }

        listing.Entries = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return listing;
    }

    public void CreateDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var target = this.ResolvePath(path);
        if (this.IsRootPath(target) || this.IsRootOrSystemDirectory(target))
        {
            throw new InvalidOperationException($"Cannot create directory in root or system directory '{target}'.");
        }

        this.diskProvider.CreateFolder(target);
    }

    public void Rename(string path, string newName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var current = this.ResolvePath(path);
        if (this.IsRootPath(current) || this.IsRootOrSystemDirectory(current))
        {
            throw new InvalidOperationException($"Cannot rename root or system directory or file '{current}'.");
        }

        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(newName, ".", StringComparison.Ordinal) ||
            string.Equals(newName, "..", StringComparison.Ordinal) ||
            !NzbDrone.Core.Organizer.FileNameSanitizer.IsValidFileNameStatic(newName))
        {
            throw new ArgumentException($"The name '{newName}' is invalid.");
        }

        var parent = this.GetParentPath(current);
        var dest = Path.Combine(parent, newName);

        if (string.Equals(current, dest, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (this.IsRootPath(dest) || this.IsRootOrSystemDirectory(dest))
        {
            throw new InvalidOperationException($"Cannot rename to root or system directory or file '{dest}'.");
        }

        if (this.diskProvider.FileExists(dest) || this.diskProvider.FolderExists(dest))
        {
            throw new InvalidOperationException($"Destination '{dest}' already exists.");
        }

        if (this.diskProvider.FolderExists(current))
        {
            this.diskProvider.MoveFolder(current, dest);
        }
        else
        {
            this.diskProvider.MoveFile(current, dest, false);
        }
    }

    public void Copy(string sourcePath, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));
        }

        var source = this.ResolvePath(sourcePath);
        var destDir = this.ResolvePath(destinationDirectory);

        if (this.IsRootPath(source) || this.IsRootOrSystemDirectory(source))
        {
            throw new InvalidOperationException($"Cannot copy root or system path '{source}'.");
        }

        if (this.IsRootPath(destDir) || this.IsRootOrSystemDirectory(destDir))
        {
            throw new InvalidOperationException($"Cannot copy to root or system directory '{destDir}'.");
        }

        var name = Path.GetFileName(source);
        var target = Path.Combine(destDir, name);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourceWithSep = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (this.diskProvider.FolderExists(source) &&
            (target.StartsWith(sourceWithSep, StringComparison.OrdinalIgnoreCase) ||
             destDir.StartsWith(sourceWithSep, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(destDir, source, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Cannot copy a directory into one of its subdirectories.");
        }

        if (!this.diskProvider.FolderExists(destDir))
        {
            this.diskProvider.CreateFolder(destDir);
        }

        if (this.diskProvider.FolderExists(source))
        {
            this.CopyDirectoryRecursive(source, target);
        }
        else if (this.diskProvider.FileExists(source) || File.Exists(source))
        {
            this.diskProvider.CopyFile(source, target, overwrite: true);
        }
    }

    public void Move(string sourcePath, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));
        }

        var source = this.ResolvePath(sourcePath);
        var destDir = this.ResolvePath(destinationDirectory);

        if (this.IsRootPath(source) || this.IsRootOrSystemDirectory(source))
        {
            throw new InvalidOperationException($"Cannot move root or system path '{source}'.");
        }

        if (this.IsRootPath(destDir) || this.IsRootOrSystemDirectory(destDir))
        {
            throw new InvalidOperationException($"Cannot move to root or system directory '{destDir}'.");
        }

        var name = Path.GetFileName(source);
        var target = Path.Combine(destDir, name);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourceWithSep = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (this.diskProvider.FolderExists(source) &&
            (target.StartsWith(sourceWithSep, StringComparison.OrdinalIgnoreCase) ||
             destDir.StartsWith(sourceWithSep, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(destDir, source, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Cannot move a directory into one of its subdirectories.");
        }

        if (!this.diskProvider.FolderExists(destDir))
        {
            this.diskProvider.CreateFolder(destDir);
        }

        if (this.diskProvider.FolderExists(source))
        {
            try
            {
                this.diskProvider.MoveFolder(source, target);
            }
            catch (IOException)
            {
                // Fallback for cross-device / cross-volume moves (EXDEV)
                this.CopyDirectoryRecursive(source, target);
                this.diskProvider.DeleteFolder(source, true);
            }
        }
        else if (this.diskProvider.FileExists(source) || File.Exists(source))
        {
            try
            {
                this.diskProvider.MoveFile(source, target, overwrite: true);
            }
            catch (IOException)
            {
                // Fallback for cross-device / cross-volume moves (EXDEV)
                this.diskProvider.CopyFile(source, target, overwrite: true);
                this.diskProvider.DeleteFile(source);
            }
        }
    }

    private void CopyDirectoryRecursive(string sourceDir, string targetDir, HashSet<string> visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var canonicalSource = Path.GetFullPath(sourceDir);
        try
        {
            var targetInfo = Directory.ResolveLinkTarget(sourceDir, true);
            if (targetInfo != null)
            {
                canonicalSource = targetInfo.FullName;
            }
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to resolve link target for '{0}'", sourceDir);
        }

        if (!visited.Add(canonicalSource))
        {
            // Cycle detected: skip to prevent infinite recursion
            return;
        }

        if (!this.diskProvider.FolderExists(targetDir))
        {
            this.diskProvider.CreateFolder(targetDir);
        }

        foreach (var file in this.diskProvider.GetFiles(sourceDir, false))
        {
            var fileName = Path.GetFileName(file);
            this.diskProvider.CopyFile(file, Path.Combine(targetDir, fileName), overwrite: true);
        }

        foreach (var subDir in this.diskProvider.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(Path.TrimEndingDirectorySeparator(subDir));
            this.CopyDirectoryRecursive(subDir, Path.Combine(targetDir, dirName), visited);
        }
    }

    public void Delete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        if (this.IsRootPath(path) || this.IsRootOrSystemDirectory(path))
        {
            throw new InvalidOperationException($"Cannot delete root or system directory '{path}'.");
        }

        var target = this.ResolvePath(path);

        if (this.IsRootPath(target) || this.IsRootOrSystemDirectory(target))
        {
            throw new InvalidOperationException($"Cannot delete root or system directory '{target}'.");
        }

        if (this.diskProvider.FolderExists(target))
        {
            this.diskProvider.DeleteFolder(target, true);
        }
        else
        {
            this.diskProvider.DeleteFile(target);
        }
    }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.GetDefaultPath();
        }

        if (path.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Path contains invalid characters.", nameof(path));
        }

        try
        {
            var full = Path.GetFullPath(path);
            return this.IsRootPath(full) ? full : Path.TrimEndingDirectorySeparator(full);
        }
        catch
        {
            return Path.TrimEndingDirectorySeparator(path);
        }
    }

    public string GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var normalized = this.IsRootPath(path) ? path : Path.TrimEndingDirectorySeparator(path);
        var root = Path.GetPathRoot(normalized);
        if (!string.IsNullOrEmpty(root) && normalized.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var parent = Path.GetDirectoryName(normalized);
        return string.IsNullOrEmpty(parent) ? normalized : parent;
    }

    private string GetDefaultPath()
    {
        if (!string.IsNullOrWhiteSpace(this.configService?.DownloadDir) &&
            this.diskProvider.FolderExists(this.configService.DownloadDir))
        {
            var full = Path.GetFullPath(this.configService.DownloadDir);
            return this.IsRootPath(full) ? full : Path.TrimEndingDirectorySeparator(full);
        }

        var fallback = Directory.Exists("/downloads") ? "/downloads" : Path.GetFullPath(".");
        return this.IsRootPath(fallback) ? fallback : Path.TrimEndingDirectorySeparator(fallback);
    }

    public bool IsRootOrSystemDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (this.IsRootPath(path))
        {
            return true;
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            normalized = Path.TrimEndingDirectorySeparator(path);
        }

        if (this.IsRootPath(normalized))
        {
            return true;
        }

        static bool MatchesOrIsDescendant(string testPath, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                return false;
            }

            var clean = Path.TrimEndingDirectorySeparator(targetDir);
            if (testPath.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return testPath.StartsWith(clean + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   testPath.StartsWith(clean + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Protect AppDataFolder and its contents
        if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.AppDataFolder))
        {
            var appData = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.appFolderInfo.AppDataFolder));
            if (MatchesOrIsDescendant(normalized, appData))
            {
                return true;
            }
        }

        // Protect StartUpFolder and its contents
        if (this.appFolderInfo != null && !string.IsNullOrWhiteSpace(this.appFolderInfo.StartUpFolder))
        {
            var startUp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.appFolderInfo.StartUpFolder));
            if (MatchesOrIsDescendant(normalized, startUp))
            {
                return true;
            }
        }

        // Protect AppContext.BaseDirectory and its contents
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
        {
            var baseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            if (MatchesOrIsDescendant(normalized, baseDir))
            {
                return true;
            }
        }

        // Check configured DownloadDir
        string downloadDir = null;
        if (this.configService != null && !string.IsNullOrWhiteSpace(this.configService.DownloadDir))
        {
            try
            {
                downloadDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.configService.DownloadDir));
            }
            catch
            {
                downloadDir = Path.TrimEndingDirectorySeparator(this.configService.DownloadDir);
            }

            // Protect the DownloadDir root folder itself from deletion
            if (normalized.Equals(downloadDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var systemDirs = new[]
        {
            "/bin", "/sbin", "/etc", "/usr", "/var", "/lib", "/lib64", "/boot",
            "/dev", "/proc", "/sys", "/root", "/opt", "/srv",
        };

        foreach (var sysDir in systemDirs)
        {
            if (MatchesOrIsDescendant(normalized, sysDir))
            {
                return true;
            }
        }

        // For /home: protect /home and its descendants unless strictly inside configured DownloadDir
        if (MatchesOrIsDescendant(normalized, "/home"))
        {
            var isInsideDownloadDir = downloadDir != null &&
                (normalized.StartsWith(downloadDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(downloadDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (!isInsideDownloadDir)
            {
                return true;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir) && MatchesOrIsDescendant(normalized, windowsDir))
            {
                return true;
            }

            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(systemDir) && MatchesOrIsDescendant(normalized, systemDir))
            {
                return true;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles) && MatchesOrIsDescendant(normalized, programFiles))
            {
                return true;
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86) && MatchesOrIsDescendant(normalized, programFilesX86))
            {
                return true;
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                if (normalized.Equals(Path.TrimEndingDirectorySeparator(userProfile), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var isInsideDownloadDir = downloadDir != null &&
                    (normalized.StartsWith(downloadDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                     normalized.StartsWith(downloadDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

                if (!isInsideDownloadDir && MatchesOrIsDescendant(normalized, userProfile))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim();
        if (trimmed == "/" || trimmed == "\\" || trimmed == "~")
        {
            return true;
        }

        var root = Path.GetPathRoot(trimmed);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        return trimmed.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }
}
