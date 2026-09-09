// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using NLog;

namespace NzbDrone.Common.Disk;

public class DiskProvider : IDiskProvider
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public long? GetAvailableSpace(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var drive = GetBestMatchingDrive(path);
            return drive?.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            Logger.Trace(ex, "Failed to determine available space for path: {0}", path);
            return null;
        }
    }

    public long? GetTotalSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var drive = GetBestMatchingDrive(path);
            return drive?.TotalSize;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            Logger.Trace(ex, "Failed to determine total size for path: {0}", path);
            return null;
        }
    }

    private static DriveInfo GetBestMatchingDrive(string path)
    {
        var rawPath = path;
        if (rawPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = @"\\" + rawPath.Substring(8);
        }
        else if (rawPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = rawPath.Substring(4);
        }

        var fullPath = Path.GetFullPath(rawPath);

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
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            Logger.Trace(ex, "Failed to retrieve system drives");
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
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                Logger.Trace(ex, "Failed to inspect drive '{0}' for path '{1}'", drive.Name, fullPath);
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

        return !Directory.EnumerateFileSystemEntries(path).Any();
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
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
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
        }
        catch
        {
            // Root path itself may be inaccessible
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

    public string SanitizeNtfsFileName(string fileName, string replacement = "_")
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return fileName;
        }

        var invalidNtfsChars = new[] { '<', '>', ':', '"', '|', '?', '*' };
        var sb = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            if (c <= 0x1F || invalidNtfsChars.Contains(c) || c == '/' || c == '\\')
            {
                sb.Append(replacement);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public string SanitizeNtfsPath(string path, string replacement = "_")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var isForwardSlash = path.Contains('/') && !path.Contains('\\');
        var sep = isForwardSlash ? '/' : '\\';
        var normalized = path.Replace('/', '\\');

        var prefix = string.Empty;
        var remaining = normalized;

        if (remaining.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            prefix = @"\\?\UNC\";
            remaining = remaining.Substring(8);
        }
        else if (remaining.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            prefix = remaining.Substring(0, 4);
            remaining = remaining.Substring(4);

            if (remaining.Length >= 2 && char.IsLetter(remaining[0]) && remaining[1] == ':')
            {
                prefix += remaining.Substring(0, 2) + @"\";
                remaining = remaining.Length > 3 ? remaining.Substring(3) : (remaining.Length > 2 ? remaining.Substring(2) : string.Empty);
            }
        }
        else if (remaining.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            prefix = @"\\";
            remaining = remaining.Substring(2);
        }
        else if (remaining.Length >= 2 && char.IsLetter(remaining[0]) && remaining[1] == ':')
        {
            prefix = remaining.Substring(0, 2) + @"\";
            remaining = remaining.Length > 3 ? remaining.Substring(3) : (remaining.Length > 2 ? remaining.Substring(2) : string.Empty);
        }
        else if (remaining.StartsWith('\\'))
        {
            prefix = isForwardSlash ? "/" : @"\";
            remaining = remaining.Substring(1);
        }

        var segments = remaining.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = new List<string>();
        foreach (var segment in segments)
        {
            sanitizedSegments.Add(this.SanitizeNtfsFileName(segment, replacement));
        }

        var result = prefix + string.Join(sep, sanitizedSegments);
        return result;
    }

    public string EnsureLongPathPrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= 260)
        {
            return path;
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path.Substring(2);
        }

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return @"\\?\" + path;
        }

        return path;
    }
}
