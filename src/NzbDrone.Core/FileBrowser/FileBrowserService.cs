using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
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
}

public class FileBrowserService : IFileBrowserService
{
    private readonly IDiskProvider diskProvider;
    private readonly IConfigService configService;

    public FileBrowserService(IDiskProvider diskProvider, IConfigService configService)
    {
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.configService = configService;
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
                catch
                {
                    // Skip inaccessible directories
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Root path itself is not readable
        }
        catch (SecurityException)
        {
            // Security permission error
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
                catch
                {
                    // Skip inaccessible files
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Root path itself is not readable
        }
        catch (SecurityException)
        {
            // Security permission error
        }

        listing.Entries = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return listing;
    }

    public void CreateDirectory(string path)
    {
        var target = this.ResolvePath(path);
        this.diskProvider.CreateFolder(target);
    }

    public void Rename(string path, string newName)
    {
        var current = this.ResolvePath(path);

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
        var source = this.ResolvePath(sourcePath);
        var destDir = this.ResolvePath(destinationDirectory);

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
        var source = this.ResolvePath(sourcePath);
        var destDir = this.ResolvePath(destinationDirectory);

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

    private void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
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
            this.CopyDirectoryRecursive(subDir, Path.Combine(targetDir, dirName));
        }
    }

    public void Delete(string path)
    {
        var target = this.ResolvePath(path);

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

    private bool IsRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               path.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }
}
