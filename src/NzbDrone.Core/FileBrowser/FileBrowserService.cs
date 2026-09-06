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

        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"The name '{newName}' is invalid.");
        }

        var parent = this.GetParentPath(current);
        var dest = Path.Combine(parent, newName);

        if (this.diskProvider.FolderExists(current))
        {
            this.diskProvider.MoveFolder(current, dest);
        }
        else
        {
            this.diskProvider.MoveFile(current, dest, true);
        }
    }

    public void Copy(string sourcePath, string destinationDirectory)
    {
        var source = this.ResolvePath(sourcePath);
        var destDir = this.ResolvePath(destinationDirectory);

        if (!this.diskProvider.FolderExists(destDir))
        {
            this.diskProvider.CreateFolder(destDir);
        }

        var name = Path.GetFileName(source);
        var target = Path.Combine(destDir, name);

        if (this.diskProvider.FolderExists(source))
        {
            this.CopyDirectoryRecursive(source, target);
        }
        else if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: true);
        }
    }

    public void Move(string sourcePath, string destinationDirectory)
    {
        var source = this.ResolvePath(sourcePath);
        var destDir = this.ResolvePath(destinationDirectory);

        if (!this.diskProvider.FolderExists(destDir))
        {
            this.diskProvider.CreateFolder(destDir);
        }

        var name = Path.GetFileName(source);
        var target = Path.Combine(destDir, name);

        if (this.diskProvider.FolderExists(source))
        {
            this.diskProvider.MoveFolder(source, target);
        }
        else if (File.Exists(source))
        {
            this.diskProvider.MoveFile(source, target, overwrite: true);
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
            File.Copy(file, Path.Combine(targetDir, fileName), overwrite: true);
        }

        foreach (var subDir in this.diskProvider.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
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
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private string GetDefaultPath()
    {
        if (!string.IsNullOrWhiteSpace(this.configService?.DownloadDir) &&
            this.diskProvider.FolderExists(this.configService.DownloadDir))
        {
            return Path.GetFullPath(this.configService.DownloadDir);
        }

        var fallback = Directory.Exists("/downloads") ? "/downloads" : Path.GetFullPath(".");
        return fallback;
    }

    private string GetParentPath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.GetDirectoryName(path) ?? path;
    }

    private bool IsRootPath(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && path.Equals(root, StringComparison.OrdinalIgnoreCase);
    }
}
