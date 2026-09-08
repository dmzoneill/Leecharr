// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Text.RegularExpressions;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Extraction;

public static class ArchiveTimeoutCalculator
{
    public const int DefaultMinutesPerGigabyte = 1;
    public static readonly TimeSpan DefaultBaseTimeout = TimeSpan.FromMinutes(30);

    public static long EstimateTotalArchiveSize(string archivePath, IDiskProvider diskProvider)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || diskProvider == null || !diskProvider.FileExists(archivePath))
        {
            return 0L;
        }

        try
        {
            var baseSize = diskProvider.GetFileSize(archivePath);
            var dir = Path.GetDirectoryName(archivePath);
            if (string.IsNullOrEmpty(dir) || !diskProvider.FolderExists(dir))
            {
                return Math.Max(0L, baseSize);
            }

            var fileName = Path.GetFileName(archivePath);
            var partMatch = Regex.Match(fileName, @"^(.*?)\.part\d+\.(rar|7z|zip)$", RegexOptions.IgnoreCase);
            if (partMatch.Success)
            {
                var prefix = partMatch.Groups[1].Value;
                var ext = partMatch.Groups[2].Value;
                var companionFiles = diskProvider.GetFiles(dir, false);
                long totalVolumeSize = 0;
                foreach (var file in companionFiles)
                {
                    var fn = Path.GetFileName(file);
                    if (Regex.IsMatch(fn, $@"^{Regex.Escape(prefix)}\.part\d+\.{Regex.Escape(ext)}$", RegexOptions.IgnoreCase))
                    {
                        totalVolumeSize += diskProvider.GetFileSize(file);
                    }
                }

                return totalVolumeSize > 0 ? totalVolumeSize : Math.Max(0L, baseSize);
            }

            var numMatch = Regex.Match(fileName, @"^(.*?)\.(r\d{2}|\d{3}|z\d{2}|001)$", RegexOptions.IgnoreCase);
            if (numMatch.Success)
            {
                var prefix = numMatch.Groups[1].Value;
                var companionFiles = diskProvider.GetFiles(dir, false);
                long totalVolumeSize = 0;
                foreach (var file in companionFiles)
                {
                    var fn = Path.GetFileName(file);
                    if (Regex.IsMatch(fn, $@"^{Regex.Escape(prefix)}\.(r\d{{2}}|\d{{3}}|z\d{{2}}|rar)$", RegexOptions.IgnoreCase))
                    {
                        totalVolumeSize += diskProvider.GetFileSize(file);
                    }
                }

                return totalVolumeSize > 0 ? totalVolumeSize : Math.Max(0L, baseSize);
            }

            var splitMatch = Regex.Match(fileName, @"^(.*?)\.(7z|tar|zip|rar)\.\d+$", RegexOptions.IgnoreCase);
            if (splitMatch.Success)
            {
                var prefix = splitMatch.Groups[1].Value;
                var ext = splitMatch.Groups[2].Value;
                var companionFiles = diskProvider.GetFiles(dir, false);
                long totalVolumeSize = 0;
                foreach (var file in companionFiles)
                {
                    var fn = Path.GetFileName(file);
                    if (Regex.IsMatch(fn, $@"^{Regex.Escape(prefix)}\.{Regex.Escape(ext)}\.\d+$", RegexOptions.IgnoreCase))
                    {
                        totalVolumeSize += diskProvider.GetFileSize(file);
                    }
                }

                return totalVolumeSize > 0 ? totalVolumeSize : Math.Max(0L, baseSize);
            }

            return Math.Max(0L, baseSize);
        }
        catch
        {
            return 0L;
        }
    }

    public static TimeSpan CalculateDynamicTimeout(
        string archivePath,
        IDiskProvider diskProvider,
        TimeSpan? baseTimeout = null,
        int minutesPerGb = DefaultMinutesPerGigabyte)
    {
        var effectiveBaseTimeout = baseTimeout ?? DefaultBaseTimeout;
        if (effectiveBaseTimeout < TimeSpan.FromMinutes(1))
        {
            effectiveBaseTimeout = DefaultBaseTimeout;
        }

        var effectiveMinutesPerGb = Math.Max(0, minutesPerGb);
        var totalBytes = EstimateTotalArchiveSize(archivePath, diskProvider);
        if (totalBytes <= 0)
        {
            return effectiveBaseTimeout;
        }

        var sizeGb = (double)totalBytes / (1024.0 * 1024.0 * 1024.0);
        var additionalMinutes = (int)Math.Ceiling(sizeGb * effectiveMinutesPerGb);

        return effectiveBaseTimeout + TimeSpan.FromMinutes(additionalMinutes);
    }
}
