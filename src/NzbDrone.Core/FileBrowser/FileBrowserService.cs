// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
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

    List<string> GetAllowedRoots();
}

public class FileBrowserService : IFileBrowserService
{
    private readonly IDiskProvider diskProvider;
    private readonly IConfigService configService;
    private readonly IAppFolderInfo appFolderInfo;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public FileBrowserService(
        IDiskProvider diskProvider = null,
        IConfigService configService = null,
        IAppFolderInfo appFolderInfo = null)
    {
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.configService = configService;
        this.appFolderInfo = appFolderInfo;
    }

    public List<string> GetAllowedRoots()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(this.configService?.DownloadDir))
        {
            try
            {
                roots.Add(Path.GetFullPath(this.configService.DownloadDir));
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(this.configService?.IncompleteDownloadDir))
        {
            try
            {
                roots.Add(Path.GetFullPath(this.configService.IncompleteDownloadDir));
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(this.appFolderInfo?.AppDataFolder))
        {
            try
            {
                roots.Add(Path.GetFullPath(this.appFolderInfo.AppDataFolder));
            }
            catch
            {
            }
        }

        if (Directory.Exists("/downloads"))
        {
            try
            {
                roots.Add(Path.GetFullPath("/downloads"));
            }
            catch
            {
            }
        }

        var defaultPath = this.GetDefaultPath();
        if (!string.IsNullOrWhiteSpace(defaultPath))
        {
            try
            {
                roots.Add(Path.GetFullPath(defaultPath));
            }
            catch
            {
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public FileBrowserListing ListDirectory(string path)
    {
        var target = this.ResolveAndConfinePath(path);

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
                    if (!this.IsPathConfined(dirPath, out var resolvedDir))
                    {
                        continue;
                    }

                    var info = new DirectoryInfo(resolvedDir);
                    entries.Add(new FileBrowserEntry
                    {
                        Name = info.Name,
                        Path = resolvedDir,
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

        foreach (var filePath in this.diskProvider.GetFiles(target, false))
        {
            try
            {
                if (!this.IsPathConfined(filePath, out var resolvedFile))
                {
                    continue;
                }

                var info = new FileInfo(resolvedFile);
                entries.Add(new FileBrowserEntry
                {
                    Name = info.Name,
                    Path = resolvedFile,
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

        listing.Entries = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return listing;
    }

    public void CreateDirectory(string path)
    {
        var target = this.ResolveAndConfinePath(path);
        this.diskProvider.CreateFolder(target);
    }

    public void Rename(string path, string newName)
    {
        var current = this.ResolveAndConfinePath(path);

        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"The name '{newName}' is invalid.");
        }

        if (this.IsRootPath(current))
        {
            throw new InvalidOperationException($"Cannot rename root directory '{current}'.");
        }

        var parent = this.GetParentPath(current);
        var dest = Path.Combine(parent, newName);

        if (!this.IsPathConfined(dest, out var resolvedDest))
        {
            throw new UnauthorizedAccessException($"Access to destination '{dest}' is outside allowed directory roots.");
        }

        if (this.diskProvider.FolderExists(current))
        {
            this.diskProvider.MoveFolder(current, resolvedDest);
        }
        else
        {
            this.diskProvider.MoveFile(current, resolvedDest, true);
        }
    }

    public void Delete(string path)
    {
        var target = this.ResolveAndConfinePath(path);

        if (this.IsRootPath(target))
        {
            throw new InvalidOperationException($"Cannot delete root directory '{target}'.");
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

    public bool IsPathConfined(string path, out string canonicalPath)
    {
        canonicalPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            // Resolve real path if link target exists
            var realPath = fullPath;
            try
            {
                if (Directory.Exists(fullPath))
                {
                    realPath = Directory.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName ?? fullPath;
                }
                else if (File.Exists(fullPath))
                {
                    realPath = File.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName ?? fullPath;
                }
            }
            catch
            {
                realPath = fullPath;
            }

            var allowedRoots = this.GetAllowedRoots();
            foreach (var root in allowedRoots)
            {
                var normRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normTarget = realPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (normTarget.Equals(normRoot, StringComparison.OrdinalIgnoreCase) ||
                    normTarget.StartsWith(normRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normTarget.StartsWith(normRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalPath = fullPath;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string ResolveAndConfinePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this.GetDefaultPath();
        }

        if (!this.IsPathConfined(path, out var canonicalPath))
        {
            throw new UnauthorizedAccessException($"Access to path '{path}' is outside allowed directory roots.");
        }

        return canonicalPath;
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
        if (this.IsRootPath(path))
        {
            return path;
        }

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent) || !this.IsPathConfined(parent, out _))
        {
            return path;
        }

        return parent;
    }

    private bool IsRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var allowedRoots = this.GetAllowedRoots();

        if (allowedRoots.Any(r => r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(normPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && path.Equals(root, StringComparison.OrdinalIgnoreCase);
    }
}
