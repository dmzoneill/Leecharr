// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Subtitles;

public class SubtitleDiscoveryService : ISubtitleDiscoveryService
{
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".vtt",
        ".sub"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".avi",
        ".mov",
        ".wmv",
        ".webm",
        ".flv",
        ".m4v",
        ".ts",
        ".m2ts"
    };

    private static readonly Regex EpisodeRegex = new(
        @"(?i)\b(?:S(?<spad>\d{1,2})E(?<epad>\d{1,3})|(?<sno>\d{1,2})x(?<eno>\d{1,3}))\b",
        RegexOptions.Compiled);

    private static readonly Regex TrackIndexPrefixRegex = new(
        @"^(?:\d+[\s._-]+)+",
        RegexOptions.Compiled);

    private static readonly Regex ForcedRegex = new(
        @"(?i)\b(forced|forc[eé]e?)\b",
        RegexOptions.Compiled);

    private static readonly Regex SdhRegex = new(
        @"(?i)\b(sdh|cc|hearing[\s._-]*impaired)\b",
        RegexOptions.Compiled);

    private record LanguageEntry(string Code, string TwoLetter, string DisplayName);

    private static readonly Dictionary<string, LanguageEntry> LanguageMap = BuildLanguageMap();

    public bool IsSubtitleFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return SubtitleExtensions.Contains(ext);
    }

    public bool IsMediaFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return MediaExtensions.Contains(ext);
    }

    public List<SubtitleTrackInfo> DiscoverSubtitles(TorrentFile videoFile, IEnumerable<TorrentFile> allTorrentFiles)
    {
        if (videoFile == null || allTorrentFiles == null)
        {
            return new List<SubtitleTrackInfo>();
        }

        var allList = allTorrentFiles.Where(f => !f.IsPaddingFile).ToList();
        var videoFiles = allList.Where(f => IsMediaFile(f.Path)).ToList();
        var subtitleFiles = allList.Where(f => IsSubtitleFile(f.Path)).ToList();

        if (subtitleFiles.Count == 0)
        {
            return new List<SubtitleTrackInfo>();
        }

        var isSingleVideo = videoFiles.Count <= 1;
        var videoPath = Normalize(videoFile.Path);
        var videoDir = GetDirectory(videoPath);
        var videoStem = Path.GetFileNameWithoutExtension(videoPath);
        var videoEp = ExtractEpisodeCode(videoStem);

        var matched = new List<TorrentFile>();

        foreach (var sub in subtitleFiles)
        {
            var subPath = Normalize(sub.Path);
            var subDir = GetDirectory(subPath);
            var subStem = Path.GetFileNameWithoutExtension(subPath);
            var subEp = ExtractEpisodeCode(subPath);

            // 1. Episode mismatch guard
            if (!string.IsNullOrEmpty(videoEp) && !string.IsNullOrEmpty(subEp))
            {
                if (!string.Equals(videoEp, subEp, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Belongs to a different episode
                }

                matched.Add(sub);
                continue;
            }

            // 2. Exact or prefix stem match
            if (string.Equals(subStem, videoStem, StringComparison.OrdinalIgnoreCase) ||
                subStem.StartsWith(videoStem + ".", StringComparison.OrdinalIgnoreCase) ||
                subStem.StartsWith(videoStem + "_", StringComparison.OrdinalIgnoreCase) ||
                subStem.StartsWith(videoStem + "-", StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(sub);
                continue;
            }

            // 3. Subdirectory / Subs folder check
            if (IsInsideSubsFolder(subPath, videoDir))
            {
                // Subtitle inside Subs/ or Subtitles/
                if (subPath.IndexOf("/" + videoEp + "/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    subPath.IndexOf("/" + videoStem + "/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    subStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase) ||
                    (isSingleVideo && string.IsNullOrEmpty(subEp)))
                {
                    matched.Add(sub);
                    continue;
                }
            }

            // 4. Same directory match if single video or episode matches
            if (string.Equals(subDir, videoDir, StringComparison.OrdinalIgnoreCase))
            {
                if (isSingleVideo && string.IsNullOrEmpty(subEp))
                {
                    matched.Add(sub);
                    continue;
                }
            }

            // 5. Single video torrent catch-all (if not conflicting with episode)
            if (isSingleVideo && string.IsNullOrEmpty(subEp))
            {
                matched.Add(sub);
            }
        }

        var trackId = 1;
        var result = new List<SubtitleTrackInfo>();

        foreach (var sub in matched.DistinctBy(m => m.Id > 0 ? m.Id.ToString() : m.Path))
        {
            var track = ParseTrackInfo(sub.Path, videoStem, trackId++, sub.Id > 0 ? sub.Id : null);
            result.Add(track);
        }

        return result;
    }

    public List<SubtitleTrackInfo> DiscoverSubtitles(string videoFilePath)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return new List<SubtitleTrackInfo>();
        }

        var dir = Path.GetDirectoryName(videoFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return new List<SubtitleTrackInfo>();
        }

        var candidateFiles = new List<string>();
        try
        {
            candidateFiles.AddRange(Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly));

            var subsDir = Path.Combine(dir, "Subs");
            if (Directory.Exists(subsDir))
            {
                candidateFiles.AddRange(Directory.GetFiles(subsDir, "*.*", SearchOption.AllDirectories));
            }

            var subtitlesDir = Path.Combine(dir, "Subtitles");
            if (Directory.Exists(subtitlesDir))
            {
                candidateFiles.AddRange(Directory.GetFiles(subtitlesDir, "*.*", SearchOption.AllDirectories));
            }
        }
        catch
        {
            // Ignore directory enumeration failures
        }

        return DiscoverSubtitles(videoFilePath, candidateFiles);
    }

    public List<SubtitleTrackInfo> DiscoverSubtitles(string videoFilePath, IEnumerable<string> allFilePaths)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || allFilePaths == null)
        {
            return new List<SubtitleTrackInfo>();
        }

        var id = 1;
        var fakeVideo = new TorrentFile { Id = id++, Path = videoFilePath };
        var fakeFiles = new List<TorrentFile> { fakeVideo };

        foreach (var path in allFilePaths)
        {
            if (!string.Equals(path, videoFilePath, StringComparison.OrdinalIgnoreCase))
            {
                fakeFiles.Add(new TorrentFile { Id = id++, Path = path });
            }
        }

        return DiscoverSubtitles(fakeVideo, fakeFiles);
    }

    private static SubtitleTrackInfo ParseTrackInfo(string subPath, string videoStem, int trackId, int? fileId)
    {
        var normalized = Normalize(subPath);
        var subFileName = Path.GetFileName(normalized);
        var ext = Path.GetExtension(normalized).TrimStart('.').ToLowerInvariant();
        var subStem = Path.GetFileNameWithoutExtension(normalized);

        var isForced = ForcedRegex.IsMatch(normalized);
        var isSdh = SdhRegex.IsMatch(normalized);

        var rawTokens = subStem;
        if (subStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase))
        {
            rawTokens = subStem.Substring(videoStem.Length).TrimStart('.', '_', '-', ' ');
        }

        // Strip leading track numbers like "2_" or "01 - "
        rawTokens = TrackIndexPrefixRegex.Replace(rawTokens, string.Empty).Trim();

        var tokens = rawTokens.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        string languageCode = null;
        string twoLetter = null;
        string languageName = null;

        foreach (var token in tokens)
        {
            var key = token.ToLowerInvariant();
            if (LanguageMap.TryGetValue(key, out var entry))
            {
                languageCode = entry.Code;
                twoLetter = entry.TwoLetter;
                languageName = entry.DisplayName;
                break;
            }
        }

        // If not found in tokens, check parent folder name (e.g. Subs/English/...)
        if (languageCode == null)
        {
            var dirParts = (Path.GetDirectoryName(normalized) ?? string.Empty).Split('/');
            foreach (var part in dirParts)
            {
                var cleanPart = TrackIndexPrefixRegex.Replace(part, string.Empty).Trim().ToLowerInvariant();
                if (LanguageMap.TryGetValue(cleanPart, out var entry))
                {
                    languageCode = entry.Code;
                    twoLetter = entry.TwoLetter;
                    languageName = entry.DisplayName;
                    break;
                }
            }
        }

        languageCode ??= "und";
        twoLetter ??= "und";
        languageName ??= (string.Equals(subStem, videoStem, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(rawTokens))
            ? "Default"
            : rawTokens;

        var title = languageName;
        if (isForced && !title.Contains("Forced", StringComparison.OrdinalIgnoreCase))
        {
            title = $"{title} (Forced)";
        }

        if (isSdh && !title.Contains("SDH", StringComparison.OrdinalIgnoreCase))
        {
            title = $"{title} (SDH)";
        }

        return new SubtitleTrackInfo
        {
            TrackId = trackId,
            FileId = fileId,
            Title = title,
            Language = languageCode,
            TwoLetterCode = twoLetter,
            Format = ext,
            Path = subPath,
            IsExternal = true,
            IsForced = isForced,
            IsHearingImpaired = isSdh,
            IsDefault = trackId == 1 && !isForced
        };
    }

    private static bool IsInsideSubsFolder(string subPath, string videoDir)
    {
        var rel = subPath;
        if (!string.IsNullOrEmpty(videoDir) && rel.StartsWith(videoDir, StringComparison.OrdinalIgnoreCase))
        {
            rel = rel.Substring(videoDir.Length).TrimStart('/');
        }

        return rel.StartsWith("Subs/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("Subtitles/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/Subs/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/Subtitles/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractEpisodeCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = EpisodeRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        if (match.Groups["spad"].Success)
        {
            var s = int.Parse(match.Groups["spad"].Value);
            var e = int.Parse(match.Groups["epad"].Value);
            return $"S{s:D2}E{e:D2}";
        }

        if (match.Groups["sno"].Success)
        {
            var s = int.Parse(match.Groups["sno"].Value);
            var e = int.Parse(match.Groups["eno"].Value);
            return $"S{s:D2}E{e:D2}";
        }

        return null;
    }

    private static string Normalize(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }

    private static string GetDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(dir) ? string.Empty : Normalize(dir);
    }

    private static Dictionary<string, LanguageEntry> BuildLanguageMap()
    {
        var map = new Dictionary<string, LanguageEntry>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, string code, string two, string name)
        {
            map[key] = new LanguageEntry(code, two, name);
        }

        // English
        Add("en", "en", "en", "English");
        Add("eng", "eng", "en", "English");
        Add("english", "eng", "en", "English");

        // Spanish
        Add("es", "es", "es", "Spanish");
        Add("spa", "spa", "es", "Spanish");
        Add("spanish", "spa", "es", "Spanish");
        Add("espanol", "spa", "es", "Spanish");
        Add("castellano", "spa", "es", "Spanish");

        // French
        Add("fr", "fr", "fr", "French");
        Add("fre", "fre", "fr", "French");
        Add("fra", "fra", "fr", "French");
        Add("french", "fre", "fr", "French");
        Add("francais", "fre", "fr", "French");

        // German
        Add("de", "de", "de", "German");
        Add("ger", "ger", "de", "German");
        Add("deu", "deu", "de", "German");
        Add("german", "ger", "de", "German");
        Add("deutsch", "ger", "de", "German");

        // Italian
        Add("it", "it", "it", "Italian");
        Add("ita", "ita", "it", "Italian");
        Add("italian", "ita", "it", "Italian");
        Add("italiano", "ita", "it", "Italian");

        // Portuguese
        Add("pt", "pt", "pt", "Portuguese");
        Add("por", "por", "pt", "Portuguese");
        Add("portuguese", "por", "pt", "Portuguese");
        Add("portugues", "por", "pt", "Portuguese");
        Add("pt-br", "pt-BR", "pt", "Portuguese (Brazil)");
        Add("brazilian", "pt-BR", "pt", "Portuguese (Brazil)");
        Add("pob", "pt-BR", "pt", "Portuguese (Brazil)");

        // Russian
        Add("ru", "ru", "ru", "Russian");
        Add("rus", "rus", "ru", "Russian");
        Add("russian", "rus", "ru", "Russian");

        // Japanese
        Add("ja", "ja", "ja", "Japanese");
        Add("jpn", "jpn", "ja", "Japanese");
        Add("japanese", "jpn", "ja", "Japanese");

        // Chinese
        Add("zh", "zh", "zh", "Chinese");
        Add("chi", "chi", "zh", "Chinese");
        Add("zho", "zho", "zh", "Chinese");
        Add("chinese", "chi", "zh", "Chinese");
        Add("chs", "zh-Hans", "zh", "Chinese (Simplified)");
        Add("cht", "zh-Hant", "zh", "Chinese (Traditional)");

        // Korean
        Add("ko", "ko", "ko", "Korean");
        Add("kor", "kor", "ko", "Korean");
        Add("korean", "kor", "ko", "Korean");

        // Arabic
        Add("ar", "ar", "ar", "Arabic");
        Add("ara", "ara", "ar", "Arabic");
        Add("arabic", "ara", "ar", "Arabic");

        // Dutch
        Add("nl", "nl", "nl", "Dutch");
        Add("dut", "dut", "nl", "Dutch");
        Add("nld", "nld", "nl", "Dutch");
        Add("dutch", "dut", "nl", "Dutch");

        // Polish
        Add("pl", "pl", "pl", "Polish");
        Add("pol", "pol", "pl", "Polish");
        Add("polish", "pol", "pl", "Polish");

        // Swedish
        Add("sv", "sv", "sv", "Swedish");
        Add("swe", "swe", "sv", "Swedish");
        Add("swedish", "swe", "sv", "Swedish");

        // Norwegian
        Add("no", "no", "no", "Norwegian");
        Add("nor", "nor", "no", "Norwegian");
        Add("norwegian", "nor", "no", "Norwegian");

        // Danish
        Add("da", "da", "da", "Danish");
        Add("dan", "dan", "da", "Danish");
        Add("danish", "dan", "da", "Danish");

        // Finnish
        Add("fi", "fi", "fi", "Finnish");
        Add("fin", "fin", "fi", "Finnish");
        Add("finnish", "fin", "fi", "Finnish");

        // Turkish
        Add("tr", "tr", "tr", "Turkish");
        Add("tur", "tur", "tr", "Turkish");
        Add("turkish", "tur", "tr", "Turkish");

        // Greek
        Add("el", "el", "el", "Greek");
        Add("gre", "gre", "el", "Greek");
        Add("ell", "ell", "el", "Greek");
        Add("greek", "gre", "el", "Greek");

        // Hebrew
        Add("he", "he", "he", "Hebrew");
        Add("heb", "heb", "he", "Hebrew");
        Add("hebrew", "heb", "he", "Hebrew");

        // Hindi
        Add("hi", "hi", "hi", "Hindi");
        Add("hin", "hin", "hi", "Hindi");
        Add("hindi", "hin", "hi", "Hindi");

        // Vietnamese
        Add("vi", "vi", "vi", "Vietnamese");
        Add("vie", "vie", "vi", "Vietnamese");
        Add("vietnamese", "vie", "vi", "Vietnamese");

        // Thai
        Add("th", "th", "th", "Thai");
        Add("tha", "tha", "th", "Thai");
        Add("thai", "tha", "th", "Thai");

        // Indonesian
        Add("id", "id", "id", "Indonesian");
        Add("ind", "ind", "id", "Indonesian");
        Add("indonesian", "ind", "id", "Indonesian");

        // Czech
        Add("cs", "cs", "cs", "Czech");
        Add("cze", "cze", "cs", "Czech");
        Add("ces", "ces", "cs", "Czech");
        Add("czech", "cze", "cs", "Czech");

        // Hungarian
        Add("hu", "hu", "hu", "Hungarian");
        Add("hun", "hun", "hu", "Hungarian");
        Add("hungarian", "hun", "hu", "Hungarian");

        // Romanian
        Add("ro", "ro", "ro", "Romanian");
        Add("ron", "ron", "ro", "Romanian");
        Add("rum", "rum", "ro", "Romanian");
        Add("romanian", "ron", "ro", "Romanian");

        // Ukrainian
        Add("uk", "uk", "uk", "Ukrainian");
        Add("ukr", "ukr", "uk", "Ukrainian");
        Add("ukrainian", "ukr", "uk", "Ukrainian");

        return map;
    }
}
