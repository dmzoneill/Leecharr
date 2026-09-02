// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Common.Disk;

public class DiskProvider : IDiskProvider
{
    public long? GetAvailableSpace(string path)
    {
        try
        {
            var drive = GetBestMatchingDrive(path);
            return drive?.AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    public long? GetTotalSize(string path)
    {
        try
        {
            var drive = GetBestMatchingDrive(path);
            return drive?.TotalSize;
        }
        catch
        {
            return null;
        }
    }

    private static DriveInfo GetBestMatchingDrive(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows())
        {
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                return new DriveInfo(root);
            }
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            drives = Array.Empty<DriveInfo>();
        }

        DriveInfo bestMatch = null;
        var longestMatchLength = -1;

        var normalizedFullPath = fullPath;
        if (!normalizedFullPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            normalizedFullPath += Path.DirectorySeparatorChar;
        }

        foreach (var drive in drives)
        {
            try
            {
                var mountPath = drive.RootDirectory.FullName;
                if (!mountPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && mountPath != "/")
                {
                    mountPath += Path.DirectorySeparatorChar;
                }

                if (normalizedFullPath.StartsWith(mountPath, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Equals(drive.Name.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    if (mountPath.Length > longestMatchLength)
                    {
                        longestMatchLength = mountPath.Length;
                        bestMatch = drive;
                    }
                }
            }
            catch
            {
                // Skip drives that cannot be inspected
            }
        }

        if (bestMatch != null)
        {
            return bestMatch;
        }

        return new DriveInfo(Path.GetPathRoot(fullPath) ?? "/");
    }

    public DateTime FolderGetCreationTime(string path) => Directory.GetCreationTime(path);

    public DateTime FolderGetLastWrite(string path) => Directory.GetLastWriteTime(path);

    public DateTime FileGetLastWrite(string path) => File.GetLastWriteTime(path);

    public void EnsureFolder(string path)
    {
        if (!this.FolderExists(path))
        {
            this.CreateFolder(path);
        }
    }

    public bool FolderExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool FolderWritable(string path)
    {
        try
        {
            var testFile = Path.Combine(path, $"write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool FolderEmpty(string path)
    {
        if (!this.FolderExists(path))
        {
            return true;
        }

        return !Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext();
    }

    public IEnumerable<string> GetDirectories(string path) => Directory.GetDirectories(path);

    public IEnumerable<string> GetFiles(string path, bool recursive)
    {
        return Directory.GetFiles(path, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    }

    public long GetFolderSize(string path)
    {
        if (!this.FolderExists(path))
        {
            return 0;
        }

        long size = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                size += new FileInfo(file).Length;
            }
            catch
            {
                // Skip inaccessible files
            }
        }

        return size;
    }

    public long GetFileSize(string path)
    {
        if (!this.FileExists(path))
        {
            return 0;
        }

        return new FileInfo(path).Length;
    }

    public void CreateFolder(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path)
    {
        if (this.FileExists(path))
        {
            File.Delete(path);
        }
    }

    public void CopyFile(string source, string destination, bool overwrite = false)
    {
        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir) && !this.FolderExists(destDir))
        {
            this.CreateFolder(destDir);
        }

        File.Copy(source, destination, overwrite);
    }

    public void MoveFile(string source, string destination, bool overwrite = false)
    {
        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir) && !this.FolderExists(destDir))
        {
            this.CreateFolder(destDir);
        }

        File.Move(source, destination, overwrite);
    }

    public void MoveFolder(string source, string destination)
    {
        var destParent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destParent) && !this.FolderExists(destParent))
        {
            this.CreateFolder(destParent);
        }

        Directory.Move(source, destination);
    }

    public void DeleteFolder(string path, bool recursive)
    {
        if (this.FolderExists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public string ReadAllText(string filePath) => File.ReadAllText(filePath);

    public void WriteAllText(string filename, string contents)
    {
        var destDir = Path.GetDirectoryName(filename);
        if (!string.IsNullOrEmpty(destDir) && !this.FolderExists(destDir))
        {
            this.CreateFolder(destDir);
        }

        File.WriteAllText(filename, contents);
    }

    public FileStream OpenReadStream(string path) => File.OpenRead(path);

    public FileStream OpenWriteStream(string path)
    {
        var destDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(destDir) && !this.FolderExists(destDir))
        {
            this.CreateFolder(destDir);
        }

        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }
}
