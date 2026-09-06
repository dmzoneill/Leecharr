using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
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

    void Delete(string path);
}

public class FileBrowserService : IFileBrowserService
{
    private readonly IDiskProvider diskProvider;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;

    public FileBrowserService(IDiskProvider diskProvider, IConfigService configService, IAppFolderInfo appFolderInfo = null)
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
        var allowedRoots = this.GetAllowedRoots();

        try
        {
            foreach (var dirPath in this.diskProvider.GetDirectories(target))
            {
                try
                {
                    var info = new DirectoryInfo(dirPath);
                    var linkTarget = info.ResolveLinkTarget(true);
                    if (linkTarget != null && !IsWithinAllowedRoots(Path.GetFullPath(linkTarget.FullName), allowedRoots))
                    {
                        continue;
                    }

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
        catch
        {
            // Root path itself is not readable
        }

        try
        {
            foreach (var filePath in this.diskProvider.GetFiles(target, false))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    var linkTarget = info.ResolveLinkTarget(true);
                    if (linkTarget != null && !IsWithinAllowedRoots(Path.GetFullPath(linkTarget.FullName), allowedRoots))
                    {
                        continue;
                    }

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
        catch
        {
            // Files enumeration not readable
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
            newName == "." ||
            newName == ".." ||
            newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"The name '{newName}' is invalid.");
        }

        var parent = this.GetParentPath(current);
        var dest = Path.Combine(parent, newName);
        this.ResolvePath(dest);

        if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(current), newName, StringComparison.Ordinal))
            {
                return;
            }
        }

        if (this.diskProvider.FolderExists(dest) || this.diskProvider.FileExists(dest))
        {
            throw new InvalidOperationException($"Destination '{newName}' already exists.");
        }

        if (this.diskProvider.FolderExists(current))
        {
            this.diskProvider.MoveFolder(current, dest);
        }
        else if (this.diskProvider.FileExists(current))
        {
            this.diskProvider.MoveFile(current, dest, false);
        }
        else
        {
            throw new FileNotFoundException($"Source path '{path}' does not exist.");
        }
    }

    public void Delete(string path)
    {
        var target = this.ResolvePath(path);

        var roots = this.GetAllowedRoots();
        if (roots.Any(r => string.Equals(r, target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Cannot delete the root directory.");
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

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.GetDefaultPath();
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            throw new UnauthorizedAccessException($"Invalid path: {path}");
        }

        var allowedRoots = this.GetAllowedRoots();
        if (!IsWithinAllowedRoots(fullPath, allowedRoots))
        {
            throw new UnauthorizedAccessException($"Access to path '{path}' is not permitted.");
        }

        if (!this.IsSymlinkTargetAllowed(fullPath, allowedRoots))
        {
            throw new UnauthorizedAccessException($"Path '{path}' resolves to a location outside allowed directories.");
        }

        return fullPath;
    }

    private bool IsSymlinkTargetAllowed(string fullPath, List<string> allowedRoots)
    {
        try
        {
            var current = fullPath;
            while (!string.IsNullOrEmpty(current))
            {
                if (this.diskProvider.FolderExists(current))
                {
                    var dirInfo = new DirectoryInfo(current);
                    var linkTarget = dirInfo.ResolveLinkTarget(true);
                    if (linkTarget != null)
                    {
                        var resolved = Path.GetFullPath(linkTarget.FullName);
                        if (!IsWithinAllowedRoots(resolved, allowedRoots))
                        {
                            return false;
                        }
                    }

                    break;
                }
                else if (this.diskProvider.FileExists(current))
                {
                    var fileInfo = new FileInfo(current);
                    var linkTarget = fileInfo.ResolveLinkTarget(true);
                    if (linkTarget != null)
                    {
                        var resolved = Path.GetFullPath(linkTarget.FullName);
                        if (!IsWithinAllowedRoots(resolved, allowedRoots))
                        {
                            return false;
                        }
                    }
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch
        {
            // Ignore resolution errors
        }

        return true;
    }

    private List<string> GetAllowedRoots()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(this.configService?.DownloadDir))
        {
            roots.Add(Path.GetFullPath(this.configService.DownloadDir));
        }
        else
        {
            roots.Add(Path.GetFullPath("/downloads"));
        }

        if (!string.IsNullOrWhiteSpace(this.configService?.IncompleteDownloadDir))
        {
            roots.Add(Path.GetFullPath(this.configService.IncompleteDownloadDir));
        }

        if (!string.IsNullOrWhiteSpace(this.appFolderInfo?.AppDataFolder))
        {
            roots.Add(Path.GetFullPath(this.appFolderInfo.AppDataFolder));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string GetDefaultPath()
    {
        if (!string.IsNullOrWhiteSpace(this.configService?.DownloadDir) &&
            this.diskProvider.FolderExists(this.configService.DownloadDir))
        {
            return Path.GetFullPath(this.configService.DownloadDir);
        }

        if (this.diskProvider.FolderExists("/downloads"))
        {
            return Path.GetFullPath("/downloads");
        }

        var roots = this.GetAllowedRoots();
        var existingRoot = roots.FirstOrDefault(r => this.diskProvider.FolderExists(r));
        if (existingRoot != null)
        {
            return existingRoot;
        }

        return roots.FirstOrDefault() ?? Path.GetFullPath("/downloads");
    }

    private string GetParentPath(string path)
    {
        var roots = this.GetAllowedRoots();
        if (roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
        {
            return path;
        }

        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent != null && IsWithinAllowedRoots(parent, roots))
        {
            return parent;
        }

        return path;
    }

    private bool IsRootPath(string path)
    {
        var roots = this.GetAllowedRoots();
        if (roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && path.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinAllowedRoots(string path, IEnumerable<string> allowedRoots)
    {
        var normalized = Path.GetFullPath(path);
        foreach (var root in allowedRoots)
        {
            var normalizedRoot = Path.GetFullPath(root);
            if (normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var rootWithSep = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (normalized.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
