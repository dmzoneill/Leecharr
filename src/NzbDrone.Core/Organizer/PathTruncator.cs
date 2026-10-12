// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Organizer;

public class PathTruncator : IPathTruncator
{
    public const int DefaultMaxFileNameBytes = 255;
    public const int DefaultMaxPathChars = 260;

    private static readonly Regex StructuredMetadataRegex = new(
        @"^(?<prefix>.*?(?:[sS]\d+[eE]\d+(?:-[eE]?\d+)?|\b\d{4}\b)\s*-\s*)(?<title>.*?)(?<suffix>\s*(?:\[[^\]]+\]|\([^\)]+\))+)$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex BracketedSuffixRegex = new(
        @"^(?<prefix>.+?)(?<suffix>\s*(?:\[[^\]]+\]|\([^\)]+\))+)$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    public string TruncateFileName(string fileName, int maxBytes = DefaultMaxFileNameBytes)
    {
        return TruncateFileNameStatic(fileName, maxBytes);
    }

    public string TruncateFolderName(string folderName, int maxBytes = DefaultMaxFileNameBytes)
    {
        return TruncateFolderNameStatic(folderName, maxBytes);
    }

    public string TruncatePath(string path, int maxPathChars = DefaultMaxPathChars, int maxComponentBytes = DefaultMaxFileNameBytes)
    {
        return TruncatePathStatic(path, maxPathChars, maxComponentBytes);
    }

    public static string TruncateFileNameStatic(string fileName, int maxBytes = DefaultMaxFileNameBytes)
    {
        if (string.IsNullOrEmpty(fileName) || maxBytes <= 0)
        {
            return fileName ?? string.Empty;
        }

        var encoding = Encoding.UTF8;
        if (encoding.GetByteCount(fileName) <= maxBytes)
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;

        var extBytes = encoding.GetByteCount(extension);
        if (extBytes >= maxBytes)
        {
            return TruncateUtf8(extension, maxBytes);
        }

        var targetBytes = maxBytes - extBytes;

        // Try smart structured preservation (Series + Episode/Year prefix, title middle, bracketed suffix)
        var structuredMatch = StructuredMetadataRegex.Match(baseName);
        if (structuredMatch.Success)
        {
            var prefix = structuredMatch.Groups["prefix"].Value;
            var title = structuredMatch.Groups["title"].Value;
            var suffix = structuredMatch.Groups["suffix"].Value;

            var prefixBytes = encoding.GetByteCount(prefix);
            var suffixBytes = encoding.GetByteCount(suffix);

            if (prefixBytes + suffixBytes < targetBytes)
            {
                var availableTitleBytes = targetBytes - prefixBytes - suffixBytes;
                var truncatedTitle = TruncateUtf8(title, availableTitleBytes).TrimEnd(' ', '-', '.', '_');
                var combinedBase = $"{prefix}{truncatedTitle}{suffix}".TrimEnd(' ', '.', '-');
                if (encoding.GetByteCount(combinedBase) <= targetBytes)
                {
                    return combinedBase + extension;
                }
            }
            else if (prefixBytes < targetBytes)
            {
                var availableSuffixBytes = targetBytes - prefixBytes;
                var truncatedSuffix = TruncateUtf8(suffix, availableSuffixBytes).TrimEnd(' ', '-', '.', '_');
                var combinedBase = $"{prefix}{truncatedSuffix}".TrimEnd(' ', '.', '-');
                if (encoding.GetByteCount(combinedBase) <= targetBytes)
                {
                    return combinedBase + extension;
                }
            }
        }

        // Try general bracketed suffix preservation
        var bracketedMatch = BracketedSuffixRegex.Match(baseName);
        if (bracketedMatch.Success)
        {
            var prefix = bracketedMatch.Groups["prefix"].Value;
            var suffix = bracketedMatch.Groups["suffix"].Value;

            var suffixBytes = encoding.GetByteCount(suffix);
            if (suffixBytes < targetBytes)
            {
                var availablePrefixBytes = targetBytes - suffixBytes;
                var truncatedPrefix = TruncateUtf8(prefix, availablePrefixBytes).TrimEnd(' ', '-', '.', '_');
                var combinedBase = $"{truncatedPrefix}{suffix}".TrimEnd(' ', '.', '-');
                if (encoding.GetByteCount(combinedBase) <= targetBytes)
                {
                    return combinedBase + extension;
                }
            }
        }

        // Fallback: standard UTF-8 truncation of baseName
        var truncated = TruncateUtf8(baseName, targetBytes).TrimEnd(' ', '-', '.', '_');
        return truncated + extension;
    }

    public static string TruncateFolderNameStatic(string folderName, int maxBytes = DefaultMaxFileNameBytes)
    {
        if (string.IsNullOrEmpty(folderName) || maxBytes <= 0)
        {
            return folderName ?? string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(folderName) <= maxBytes)
        {
            return folderName;
        }

        return TruncateUtf8(folderName, maxBytes).TrimEnd(' ', '-', '.', '_');
    }

    public static string TruncatePathStatic(string path, int maxPathChars = DefaultMaxPathChars, int maxComponentBytes = DefaultMaxFileNameBytes)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path ?? string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        var isUnc = normalized.StartsWith("//");
        var isAbsolute = Path.IsPathRooted(path) || isUnc;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count == 0)
        {
            return path;
        }

        // Truncate each component to byte limit
        for (var i = 0; i < segments.Count; i++)
        {
            var isLast = i == segments.Count - 1;
            segments[i] = isLast
                ? TruncateFileNameStatic(segments[i], maxComponentBytes)
                : TruncateFolderNameStatic(segments[i], maxComponentBytes);
        }

        var reconstructed = string.Join('/', segments);
        if (isUnc)
        {
            reconstructed = "//" + reconstructed;
        }
        else if (isAbsolute && normalized.StartsWith('/'))
        {
            reconstructed = "/" + reconstructed;
        }

        if (reconstructed.Length <= maxPathChars)
        {
            return reconstructed;
        }

        // Need to reduce overall path length to maxPathChars
        var excessChars = reconstructed.Length - maxPathChars;
        var fileName = segments[^1];
        var fileBase = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
        var fileExt = Path.GetExtension(fileName) ?? string.Empty;

        if (fileBase.Length > excessChars + 3)
        {
            var newBaseLength = fileBase.Length - excessChars;
            var truncatedBase = TruncatePrefixByCharCount(fileBase, newBaseLength).TrimEnd(' ', '-', '.', '_');
            segments[^1] = truncatedBase + fileExt;
        }
        else
        {
            ReducePathSegments(segments, ref excessChars, minDirectoryChars: 4, minFileBaseChars: 1);
        }

        var result = BuildPathFromSegments(segments, isUnc, isAbsolute, normalized);
        if (result.Length <= maxPathChars)
        {
            return result;
        }

        var remainingExcess = result.Length - maxPathChars;
        ReducePathSegments(segments, ref remainingExcess, minDirectoryChars: 1, minFileBaseChars: 0);
        result = BuildPathFromSegments(segments, isUnc, isAbsolute, normalized);
        if (result.Length > maxPathChars)
        {
            result = TruncatePathToMaxLength(result, maxPathChars);
        }

        return result;
    }

    private static string BuildPathFromSegments(
        List<string> segments,
        bool isUnc,
        bool isAbsolute,
        string normalized)
    {
        var result = string.Join('/', segments);
        if (isUnc)
        {
            result = "//" + result;
        }
        else if (isAbsolute && normalized.StartsWith('/'))
        {
            result = "/" + result;
        }

        return result;
    }

    private static void ReducePathSegments(
        List<string> segments,
        ref int excessChars,
        int minDirectoryChars,
        int minFileBaseChars)
    {
        if (excessChars <= 0 || segments.Count == 0)
        {
            return;
        }

        var fileName = segments[^1];
        var fileBase = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
        var fileExt = Path.GetExtension(fileName) ?? string.Empty;

        for (var i = segments.Count - 2; i >= 0 && excessChars > 0; i--)
        {
            var dir = segments[i];
            var reduction = Math.Min(excessChars, Math.Max(0, dir.Length - minDirectoryChars));
            if (reduction <= 0)
            {
                continue;
            }

            segments[i] = TruncatePrefixByCharCount(dir, dir.Length - reduction).TrimEnd(' ', '-', '.', '_');
            excessChars -= reduction;
        }

        if (excessChars > 0 && fileBase.Length > minFileBaseChars)
        {
            var newBaseLength = Math.Max(minFileBaseChars, fileBase.Length - excessChars);
            var truncatedBase = TruncatePrefixByCharCount(fileBase, newBaseLength).TrimEnd(' ', '-', '.', '_');
            segments[^1] = truncatedBase + fileExt;
            excessChars -= fileBase.Length - truncatedBase.Length;
        }
    }

    private static string TruncatePathToMaxLength(string path, int maxPathChars)
    {
        if (path.Length <= maxPathChars)
        {
            return path;
        }

        var extension = Path.GetExtension(path) ?? string.Empty;
        if (string.IsNullOrEmpty(extension) || extension.Length >= maxPathChars)
        {
            return TruncatePrefixByCharCount(path, maxPathChars);
        }

        var baseLength = maxPathChars - extension.Length;
        var pathWithoutExtension = path.Substring(0, path.Length - extension.Length);
        return TruncatePrefixByCharCount(pathWithoutExtension, baseLength) + extension;
    }

    private static string TruncatePrefixByCharCount(string text, int charCount)
    {
        if (string.IsNullOrEmpty(text) || charCount <= 0)
        {
            return string.Empty;
        }

        if (charCount >= text.Length)
        {
            return text;
        }

        if (char.IsHighSurrogate(text[charCount - 1]))
        {
            charCount--;
        }

        return charCount <= 0 ? string.Empty : text.Substring(0, charCount);
    }

    public static string TruncateUtf8(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0)
        {
            return string.Empty;
        }

        var encoding = Encoding.UTF8;
        if (encoding.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        var sb = new StringBuilder();
        var currentByteCount = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var charLength = char.IsSurrogatePair(text, i) ? 2 : 1;
            var sub = text.Substring(i, charLength);
            var bytes = encoding.GetByteCount(sub);

            if (currentByteCount + bytes > maxBytes)
            {
                break;
            }

            sb.Append(sub);
            currentByteCount += bytes;

            if (charLength == 2)
            {
                i++;
            }
        }

        return sb.ToString();
    }
}
