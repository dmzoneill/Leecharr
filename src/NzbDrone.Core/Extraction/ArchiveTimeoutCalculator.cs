// Copyright (c) FeedItOut. All rights reserved.

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
            var partMatch = Regex.Match(fileName, @"^(.*?)\.part\d+\.(rar|7z|zip)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            if (partMatch.Success)
            {
                var prefix = partMatch.Groups[1].Value;
                var ext = partMatch.Groups[2].Value;
                var pattern = $"^{Regex.Escape(prefix)}\\.part\\d+\\.{Regex.Escape(ext)}$";
                return SumMatchingVolumeFiles(dir, diskProvider, pattern, baseSize);
            }

            var numMatch = Regex.Match(fileName, @"^(.*?)\.(r\d{2}|\d{3}|z\d{2}|001)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            if (numMatch.Success)
            {
                var prefix = numMatch.Groups[1].Value;
                var openedExt = numMatch.Groups[2].Value;
                if (Regex.IsMatch(openedExt, @"^\d{3}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
                {
                    var numericTotal = SumContiguousNumericSplitVolumes(prefix, dir, diskProvider);
                    return numericTotal > 0 ? numericTotal : Math.Max(0L, baseSize);
                }

                if (Regex.IsMatch(openedExt, @"^r\d{2}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
                {
                    var pattern = $"^{Regex.Escape(prefix)}\\.(rar|r\\d{{2}})$";
                    return SumMatchingVolumeFiles(dir, diskProvider, pattern, baseSize);
                }

                if (Regex.IsMatch(openedExt, @"^z\d{2}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
                {
                    var pattern = $"^{Regex.Escape(prefix)}\\.(zip|z\\d{{2}})$";
                    return SumMatchingVolumeFiles(dir, diskProvider, pattern, baseSize);
                }
            }

            var splitMatch = Regex.Match(fileName, @"^(.*?)\.(7z|tar|zip|rar)\.\d+$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            if (splitMatch.Success)
            {
                var prefix = splitMatch.Groups[1].Value;
                var ext = splitMatch.Groups[2].Value;
                var pattern = $"^{Regex.Escape(prefix)}\\.{Regex.Escape(ext)}\\.\\d+$";
                return SumMatchingVolumeFiles(dir, diskProvider, pattern, baseSize);
            }

            var primaryRarZip = Regex.Match(fileName, @"^(.*?)\.(rar|zip)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            if (primaryRarZip.Success)
            {
                var prefix = primaryRarZip.Groups[1].Value;
                var ext = primaryRarZip.Groups[2].Value;
                var pattern = string.Equals(ext, "rar", StringComparison.OrdinalIgnoreCase)
                    ? $"^{Regex.Escape(prefix)}\\.(rar|r\\d{{2}})$"
                    : $"^{Regex.Escape(prefix)}\\.(zip|z\\d{{2}})$";
                return SumMatchingVolumeFiles(dir, diskProvider, pattern, baseSize);
            }

            return Math.Max(0L, baseSize);
        }
        catch
        {
            return 0L;
        }
    }

    private static long SumMatchingVolumeFiles(string dir, IDiskProvider diskProvider, string fileNamePattern, long fallbackSize)
    {
        var companionFiles = diskProvider.GetFiles(dir, false);
        long totalVolumeSize = 0;
        foreach (var file in companionFiles)
        {
            var fn = Path.GetFileName(file);
            if (Regex.IsMatch(fn, fileNamePattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            {
                totalVolumeSize += diskProvider.GetFileSize(file);
            }
        }

        return totalVolumeSize > 0 ? totalVolumeSize : Math.Max(0L, fallbackSize);
    }

    private static long SumContiguousNumericSplitVolumes(string prefix, string dir, IDiskProvider diskProvider)
    {
        var firstPart = Path.Combine(dir, prefix + ".001");
        if (!diskProvider.FileExists(firstPart))
        {
            return 0L;
        }

        long totalVolumeSize = 0;
        for (var part = 1; part <= 999; part++)
        {
            var partPath = Path.Combine(dir, $"{prefix}.{part:D3}");
            if (!diskProvider.FileExists(partPath))
            {
                break;
            }

            totalVolumeSize += diskProvider.GetFileSize(partPath);
        }

        return totalVolumeSize;
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
