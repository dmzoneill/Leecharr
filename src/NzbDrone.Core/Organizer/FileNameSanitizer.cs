// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Organizer;

public class FileNameSanitizer : IFileNameSanitizer
{
    private static readonly HashSet<string> ReservedDosNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] ForbiddenComponentChars = new[]
    {
        ':', '*', '?', '"', '<', '>', '|', '/', '\\',
    };

    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SmartColonRegex = new(@"(?<=\d):(?=\d)", RegexOptions.Compiled);
    private static readonly Regex CleanTitleIllegalCharsRegex = new(@"[^\w\s\.-]", RegexOptions.Compiled);

    public string SanitizeFileName(string fileName, ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace, string customColon = null)
    {
        return SanitizeFileNameStatic(fileName, colonFormat, customColon);
    }

    public string SanitizeFolderName(string folderName, ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace, string customColon = null)
    {
        return SanitizeFolderNameStatic(folderName, colonFormat, customColon);
    }

    public string SanitizePath(string path, ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace, string customColon = null)
    {
        return SanitizePathStatic(path, colonFormat, customColon);
    }

    public string CleanTitle(string title)
    {
        return CleanTitleStatic(title);
    }

    public bool IsValidFileName(string fileName)
    {
        return IsValidFileNameStatic(fileName);
    }

    public bool IsValidPath(string path)
    {
        return IsValidPathStatic(path);
    }

    public static string SanitizeFileNameStatic(
        string fileName,
        ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace,
        string customColon = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Unnamed";
        }

        var normalized = fileName.Replace('/', '-').Replace('\\', '-');
        var lastDot = normalized.LastIndexOf('.');
        string baseName;
        string extension;
        if (lastDot >= 0)
        {
            baseName = normalized.Substring(0, lastDot);
            extension = normalized.Substring(lastDot);
        }
        else
        {
            baseName = normalized;
            extension = string.Empty;
        }

        // Apply colon replacement to base
        baseName = ReplaceColon(baseName, colonFormat, customColon);

        // Sanitize base name characters
        baseName = SanitizeComponentString(baseName);

        // Trim leading spaces/dots and trailing spaces/dots
        baseName = baseName.Trim(' ', '.');

        // Check DOS reserved names
        if (ReservedDosNames.Contains(baseName))
        {
            baseName = "_" + baseName;
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "_";
        }

        // Sanitize extension
        var sanitizedExt = SanitizeExtension(extension);

        var result = baseName + sanitizedExt;
        return PathTruncator.TruncateFileNameStatic(result);
    }

    public static string SanitizeFolderNameStatic(
        string folderName,
        ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace,
        string customColon = null)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return "Unnamed";
        }

        var sanitized = ReplaceColon(folderName, colonFormat, customColon);
        sanitized = SanitizeComponentString(sanitized);
        sanitized = sanitized.Trim(' ', '.');

        if (ReservedDosNames.Contains(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "_";
        }

        return PathTruncator.TruncateFolderNameStatic(sanitized);
    }

    public static string SanitizePathStatic(
        string path,
        ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace,
        string customColon = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        var isUnc = normalized.StartsWith("//");
        var isAbsoluteUnix = normalized.StartsWith('/') && !isUnc;
        var drivePrefix = string.Empty;

        if (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
        {
            drivePrefix = normalized.Substring(0, 2);
            normalized = normalized.Substring(2).TrimStart('/');
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return drivePrefix.Length > 0 ? drivePrefix + "/" : "/";
        }

        var sanitizedSegments = new List<string>();
        for (var i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1 && !path.EndsWith('/') && !path.EndsWith('\\');
            var seg = segments[i];

            if (isLast && seg.Contains('.'))
            {
                sanitizedSegments.Add(SanitizeFileNameStatic(seg, colonFormat, customColon));
            }
            else
            {
                sanitizedSegments.Add(SanitizeFolderNameStatic(seg, colonFormat, customColon));
            }
        }

        var result = string.Join('/', sanitizedSegments);
        if (isUnc)
        {
            result = "//" + result;
        }
        else if (isAbsoluteUnix)
        {
            result = "/" + result;
        }
        else if (drivePrefix.Length > 0)
        {
            result = drivePrefix + "/" + result;
        }

        return PathTruncator.TruncatePathStatic(result);
    }

    public static string CleanTitleStatic(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var cleaned = title.Replace("'", string.Empty)
            .Replace("`", string.Empty)
            .Replace("’", string.Empty)
            .Replace("&", "and")
            .Replace("/", " ")
            .Replace("\\", " ")
            .Replace(":", " ")
            .Replace("*", " ")
            .Replace("?", " ")
            .Replace("\"", " ")
            .Replace("<", " ")
            .Replace(">", " ")
            .Replace("|", " ");

        // Remove control characters
        cleaned = RemoveControlChars(cleaned);

        // Remove illegal characters except word chars, spaces, dots, hyphens
        cleaned = CleanTitleIllegalCharsRegex.Replace(cleaned, string.Empty);

        // Collapse whitespace
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim(' ', '.');

        return cleaned;
    }

    public static bool IsValidFileNameStatic(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName == "." || fileName == "..")
        {
            return false;
        }

        if (fileName.StartsWith(' ') || fileName.EndsWith(' ') || fileName.EndsWith('.'))
        {
            return false;
        }

        foreach (var c in fileName)
        {
            if (c <= 0x1F || c == 0x7F)
            {
                return false;
            }

            if (ForbiddenComponentChars.Contains(c))
            {
                return false;
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (ReservedDosNames.Contains(baseName))
        {
            return false;
        }

        return true;
    }

    public static bool IsValidPathStatic(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("//") && !normalized.StartsWith("//"))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed == "." || trimmed == ".." || !IsValidFileNameStatic(trimmed))
            {
                return false;
            }
        }

        return true;
    }

    private static string ReplaceColon(string text, ColonReplacementFormat format, string customColon)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(':'))
        {
            return text ?? string.Empty;
        }

        return format switch
        {
            ColonReplacementFormat.Delete => text.Replace(":", string.Empty),
            ColonReplacementFormat.Dash => text.Replace(":", "-"),
            ColonReplacementFormat.SpaceDash => text.Replace(":", " -"),
            ColonReplacementFormat.SpaceDashSpace => text.Replace(":", " - "),
            ColonReplacementFormat.Smart => SmartColonRegex.Replace(text.Replace(" : ", " - ").Replace(": ", " - ").Replace(" :", " -"), "-").Replace(":", " - "),
            ColonReplacementFormat.Custom => text.Replace(":", customColon ?? string.Empty),
            _ => text.Replace(":", " - "),
        };
    }

    private static string SanitizeComponentString(string component)
    {
        var sb = new StringBuilder(component.Length);
        foreach (var c in component)
        {
            if (c <= 0x1F || c == 0x7F)
            {
                continue;
            }

            if (c is '/' or '\\')
            {
                sb.Append('-');
            }
            else if (c is '*' or '?' or '"' or '<' or '>' or '|')
            {
                continue;
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = MultipleSpacesRegex.Replace(sb.ToString(), " ");
        return result;
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(extension.Length);
        foreach (var c in extension)
        {
            if (c <= 0x1F || c == 0x7F || ForbiddenComponentChars.Contains(c))
            {
                continue;
            }

            sb.Append(c);
        }

        var result = sb.ToString().TrimEnd(' ', '.');
        if (result.Length > 0 && !result.StartsWith('.'))
        {
            result = "." + result;
        }

        return result;
    }

    private static string RemoveControlChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c > 0x1F && c != 0x7F)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
