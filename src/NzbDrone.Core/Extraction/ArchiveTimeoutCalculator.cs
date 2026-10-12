// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            if (TryGetVolumeFileNamePattern(fileName, out var pattern, out var contiguousNumericSplit, out var numericPrefix))
            {
                if (contiguousNumericSplit)
                {
                    var numericTotal = SumContiguousNumericSplitVolumes(numericPrefix, dir, diskProvider);
                    return numericTotal > 0 ? numericTotal : Math.Max(0L, baseSize);
                }

                return SumMatchingVolumeFiles(dir, diskProvider, pattern, baseSize);
            }

            return Math.Max(0L, baseSize);
        }
        catch
        {
            return 0L;
        }
    }

    public static long SumArchiveVolumeBytesFromTorrentFiles(string primaryTorrentPath, IEnumerable<(string Path, long Size)> torrentFiles)
    {
        if (string.IsNullOrWhiteSpace(primaryTorrentPath) || torrentFiles == null)
        {
            return 0L;
        }

        var primaryFileName = Path.GetFileName(primaryTorrentPath);
        var primarySize = torrentFiles
            .Where(f => string.Equals(f.Path, primaryTorrentPath, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Size)
            .DefaultIfEmpty(0L)
            .First();

        if (!TryGetVolumeFileNamePattern(primaryFileName, out var pattern, out var contiguousNumericSplit, out var numericPrefix))
        {
            return Math.Max(0L, primarySize);
        }

        var matching = torrentFiles
            .Select(f => (Name: Path.GetFileName(f.Path), f.Size))
            .Where(f => !string.IsNullOrEmpty(f.Name) && Regex.IsMatch(f.Name, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            .ToList();

        if (matching.Count == 0)
        {
            return Math.Max(0L, primarySize);
        }

        if (contiguousNumericSplit)
        {
            var byName = matching.ToDictionary(m => m.Name, m => m.Size, StringComparer.OrdinalIgnoreCase);
            var numericTotal = SumContiguousNumericSplitVolumeSizes(numericPrefix, byName);
            return numericTotal > 0 ? numericTotal : Math.Max(0L, primarySize);
        }

        var total = matching.Sum(m => m.Size > 0 ? m.Size : 0);
        return total > 0 ? total : Math.Max(0L, primarySize);
    }

    private static bool TryGetVolumeFileNamePattern(string fileName, out string pattern, out bool contiguousNumericSplit, out string numericPrefix)
    {
        pattern = null;
        contiguousNumericSplit = false;
        numericPrefix = null;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var partMatch = Regex.Match(fileName, @"^(.*?)\.part\d+\.(rar|7z|zip)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        if (partMatch.Success)
        {
            var prefix = partMatch.Groups[1].Value;
            var ext = partMatch.Groups[2].Value;
            pattern = $"^{Regex.Escape(prefix)}\\.part\\d+\\.{Regex.Escape(ext)}$";
            return true;
        }

        var numMatch = Regex.Match(fileName, @"^(.*?)\.(r\d{2}|\d{3}|z\d{2}|001)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        if (numMatch.Success)
        {
            var prefix = numMatch.Groups[1].Value;
            var openedExt = numMatch.Groups[2].Value;
            if (Regex.IsMatch(openedExt, @"^\d{3}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            {
                contiguousNumericSplit = true;
                numericPrefix = prefix;
                pattern = $"^{Regex.Escape(prefix)}\\.\\d{{3}}$";
                return true;
            }

            if (Regex.IsMatch(openedExt, @"^r\d{2}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            {
                pattern = $"^{Regex.Escape(prefix)}\\.(rar|r\\d{{2}})$";
                return true;
            }

            if (Regex.IsMatch(openedExt, @"^z\d{2}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            {
                pattern = $"^{Regex.Escape(prefix)}\\.(zip|z\\d{{2}})$";
                return true;
            }
        }

        var splitMatch = Regex.Match(fileName, @"^(.*?)\.(7z|tar|zip|rar)\.\d+$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        if (splitMatch.Success)
        {
            var prefix = splitMatch.Groups[1].Value;
            var ext = splitMatch.Groups[2].Value;
            pattern = $"^{Regex.Escape(prefix)}\\.{Regex.Escape(ext)}\\.\\d+$";
            return true;
        }

        var primaryRarZip = Regex.Match(fileName, @"^(.*?)\.(rar|zip)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        if (primaryRarZip.Success)
        {
            var prefix = primaryRarZip.Groups[1].Value;
            var ext = primaryRarZip.Groups[2].Value;
            pattern = string.Equals(ext, "rar", StringComparison.OrdinalIgnoreCase)
                ? $"^{Regex.Escape(prefix)}\\.(rar|r\\d{{2}})$"
                : $"^{Regex.Escape(prefix)}\\.(zip|z\\d{{2}})$";
            return true;
        }

        return false;
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

    private static long SumContiguousNumericSplitVolumeSizes(string prefix, IReadOnlyDictionary<string, long> volumeSizesByFileName)
    {
        long totalVolumeSize = 0;
        for (var part = 1; part <= 999; part++)
        {
            var partName = $"{prefix}.{part:D3}";
            if (!volumeSizesByFileName.TryGetValue(partName, out var size))
            {
                break;
            }

            totalVolumeSize += size > 0 ? size : 0;
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
