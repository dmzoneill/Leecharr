// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;

namespace NzbDrone.Core.Common;

public static class CliProcessDiscovery
{
    public static string FindExecutable(string binaryName, string envVarOverride = null, string[] additionalPaths = null)
    {
        if (!string.IsNullOrWhiteSpace(envVarOverride))
        {
            var customPath = Environment.GetEnvironmentVariable(envVarOverride);
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                return customPath;
            }
        }

        var isWindows = OperatingSystem.IsWindows();
        var extensions = isWindows ? new[] { ".exe", ".cmd", ".bat", string.Empty } : new[] { string.Empty };

        if (additionalPaths != null)
        {
            foreach (var basePath in additionalPaths)
            {
                if (string.IsNullOrWhiteSpace(basePath))
                {
                    continue;
                }

                foreach (var ext in extensions)
                {
                    var full = Path.Combine(basePath, binaryName + ext);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            foreach (var ext in extensions)
            {
                var full = Path.Combine(dir, binaryName + ext);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        var standardPaths = new[]
        {
            "/usr/bin",
            "/usr/local/bin",
            "/bin",
            "/opt/homebrew/bin",
            "/usr/pkg/bin",
            AppContext.BaseDirectory,
        };

        foreach (var dir in standardPaths)
        {
            foreach (var ext in extensions)
            {
                var full = Path.Combine(dir, binaryName + ext);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        if (isWindows)
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var winDirs = new[]
            {
                Path.Combine(programFiles, "7-Zip"),
                Path.Combine(programFilesX86, "7-Zip"),
                Path.Combine(programFiles, "WinRAR"),
                Path.Combine(programFilesX86, "WinRAR"),
                Path.Combine(programFiles, "MediaInfo"),
                Path.Combine(programFilesX86, "MediaInfo"),
                Path.Combine(programFiles, "ffmpeg", "bin"),
                Path.Combine(programFiles, "ffprobe", "bin"),
                Path.Combine(localAppData, "Programs", "7-Zip"),
                Path.Combine(localAppData, "Programs", "MediaInfo"),
                Path.Combine(localAppData, "Programs", "ffmpeg", "bin"),
            };

            foreach (var dir in winDirs)
            {
                foreach (var ext in extensions)
                {
                    var full = Path.Combine(dir, binaryName + ext);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
            }
        }

        return null;
    }
}
