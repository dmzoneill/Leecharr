// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Torrents;

public class ParsedTorrent
{
    public string Name { get; set; }

    public string InfoHash { get; set; }

    public string V1InfoHash { get; set; }

    public string V2InfoHash { get; set; }

    public long TotalSize { get; set; }

    public int PieceCount { get; set; }

    public int PieceLength { get; set; }

    public byte[] PieceHashes { get; set; }

    public string Comment { get; set; }

    public string CreatedBy { get; set; }

    public DateTime? CreationDate { get; set; }

    public bool IsPrivate { get; set; }

    public string AnnounceUrl { get; set; }

    public List<List<string>> AnnounceList { get; set; }

    public List<ParsedTorrentFile> Files { get; set; }
}

public class ParsedTorrentFile
{
    public string Path { get; set; }

    public long Size { get; set; }
}

public interface ITorrentFileParser
{
    ParsedTorrent Parse(string filePath);

    ParsedTorrent Parse(Stream stream);

    ParsedTorrent Parse(byte[] bytes);
}

public class TorrentFileParser : ITorrentFileParser
{
    public const int MaxBencodeDepth = 64;
    public const int MinPieceLength = 16 * 1024; // 16 KiB
    public const int MaxPieceLength = 64 * 1024 * 1024; // 64 MiB

    private readonly Logger logger;

    public TorrentFileParser()
    {
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public ParsedTorrent Parse(string filePath)
    {
        this.logger.Debug("Parsing torrent file: {0}", filePath);
        var bytes = File.ReadAllBytes(filePath);
        return this.Parse(bytes);
    }

    public ParsedTorrent Parse(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return this.Parse(memoryStream.ToArray());
    }

    public ParsedTorrent Parse(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        try
        {
            ValidateBencodeDepth(bytes, MaxBencodeDepth);

            using var stream = new MemoryStream(bytes);
            var parser = new BencodeParser();
            var torrent = parser.Parse<BDictionary>(stream);

            if (!torrent.ContainsKey("info") || torrent["info"] is not BDictionary info)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'info' dictionary.");
            }

            if (!info.ContainsKey("piece length") || info["piece length"] is not BNumber pieceLengthNum)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'piece length'.");
            }

            if (pieceLengthNum.Value <= 0)
            {
                throw new InvalidTorrentFileException("Piece length must be a positive integer.");
            }

            if (pieceLengthNum.Value < MinPieceLength ||
                pieceLengthNum.Value > MaxPieceLength ||
                (pieceLengthNum.Value & (pieceLengthNum.Value - 1)) != 0)
            {
                throw new InvalidTorrentFileException("Piece length must be a power of 2 between 16 KiB and 64 MiB.");
            }

            var isV2 = (info.ContainsKey("meta version") && (info["meta version"] as BNumber)?.Value == 2) ||
                       info.ContainsKey("file tree") ||
                       torrent.ContainsKey("piece layers");

            var hasV1Pieces = false;

            if (info.ContainsKey("pieces"))
            {
                if (info["pieces"] is not BString piecesStr || piecesStr.Value.Length == 0 || piecesStr.Value.Length % 20 != 0)
                {
                    throw new InvalidTorrentFileException("Pieces hash string length must be a non-zero multiple of 20.");
                }

                hasV1Pieces = true;
            }
            else if (!isV2)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'pieces'.");
            }

            var nameStr = GetUtf8String(info, "name");
            if (nameStr == null)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'name'.");
            }

            string announceUrl = null;
            var mainAnnounce = GetUtf8String(torrent, "announce") ?? GetUtf8String(info, "announce");
            if (mainAnnounce != null)
            {
                var s = mainAnnounce.ToString().Trim();
                if (!string.IsNullOrEmpty(s))
                {
                    announceUrl = s;
                }
            }

            List<List<string>> announceListParsed = null;
            if (torrent.ContainsKey("announce-list") && torrent["announce-list"] is BList announceList)
            {
                announceListParsed = new List<List<string>>();
                ExtractAnnounceList(announceList, announceListParsed);
            }
            else if (info.ContainsKey("announce-list") && info["announce-list"] is BList infoAnnounceList)
            {
                announceListParsed = new List<List<string>>();
                ExtractAnnounceList(infoAnnounceList, announceListParsed);
            }

            if (announceUrl == null && announceListParsed != null && announceListParsed.Count > 0 && announceListParsed[0].Count > 0)
            {
                announceUrl = announceListParsed[0][0];
            }

            var torrentName = nameStr.ToString();
            var sanitizedTorrentName = SanitizeTorrentName(torrentName);

            var result = new ParsedTorrent
            {
                Name = torrentName,
                PieceLength = (int)pieceLengthNum.Value,
                Comment = GetUtf8String(torrent, "comment")?.ToString(),
                CreatedBy = GetUtf8String(torrent, "created by")?.ToString(),
                IsPrivate = info.ContainsKey("private") &&
                    (((info["private"] as BNumber)?.Value == 1) ||
                     ((info["private"] as BString)?.ToString() == "1")),
                AnnounceUrl = announceUrl,
                AnnounceList = announceListParsed,
                Files = new List<ParsedTorrentFile>(),
            };

            if (hasV1Pieces)
            {
                var piecesVal = ((BString)info["pieces"]).Value;
                result.PieceCount = piecesVal.Length / 20;
                result.PieceHashes = piecesVal.ToArray();
                result.V1InfoHash = InfoHashCalculator.Calculate(info);
            }
            else
            {
                result.PieceHashes = Array.Empty<byte>();
            }

            if (isV2)
            {
                result.V2InfoHash = InfoHashCalculator.CalculateV2(info);
            }

            result.InfoHash = result.V1InfoHash ?? result.V2InfoHash;

            if (torrent.ContainsKey("creation date") && torrent["creation date"] is BNumber creationDateNum)
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(creationDateNum.Value).UtcDateTime;
            }

            long rawTotalSize = 0;

            if (info.ContainsKey("file tree") && info["file tree"] is BDictionary fileTree)
            {
                TraverseFileTree(fileTree, new List<string>(), result.Files, 1, MaxBencodeDepth, ref rawTotalSize);
            }
            else if (info.ContainsKey("files") && info["files"] is BList files)
            {
                foreach (var fileObj in files)
                {
                    if (fileObj is not BDictionary file)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file list entry is not a dictionary.");
                    }

                    if (!file.ContainsKey("length") || file["length"] is not BNumber fileLengthNum)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file entry missing or invalid 'length'.");
                    }

                    if (fileLengthNum.Value < 0)
                    {
                        throw new InvalidTorrentFileException("File length cannot be negative.");
                    }

                    var pathList = GetUtf8PathList(file);
                    if (pathList == null || pathList.Count == 0)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file entry missing, empty, or invalid 'path'.");
                    }

                    var pathParts = new List<string>();
                    foreach (var pathItem in pathList)
                    {
                        if (pathItem is not BString pathPartStr)
                        {
                            throw new InvalidTorrentFileException("Malformed torrent file: file entry path component is not a string.");
                        }

                        var part = pathPartStr.ToString();
                        ValidateAndSanitizePathPart(part);
                        pathParts.Add(part.Trim());
                    }

                    var relativeFilePath = string.Join("/", pathParts);
                    if (Path.IsPathRooted(relativeFilePath) || relativeFilePath.StartsWith('/') || relativeFilePath.StartsWith('\\'))
                    {
                        throw new InvalidTorrentFileException($"Malformed torrent file: resolved file path cannot be an absolute path: '{relativeFilePath}'.");
                    }

                    rawTotalSize += fileLengthNum.Value;

                    if (!IsPaddingFile(file, relativeFilePath))
                    {
                        result.Files.Add(new ParsedTorrentFile
                        {
                            Path = relativeFilePath,
                            Size = fileLengthNum.Value,
                        });
                    }
                }
            }
            else
            {
                if (!info.ContainsKey("length") || info["length"] is not BNumber lengthNum)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'length' for single-file torrent.");
                }

                if (lengthNum.Value < 0)
                {
                    throw new InvalidTorrentFileException("File length cannot be negative.");
                }

                rawTotalSize += lengthNum.Value;

                if (!IsPaddingFile(info, sanitizedTorrentName))
                {
                    result.Files.Add(new ParsedTorrentFile
                    {
                        Path = sanitizedTorrentName,
                        Size = lengthNum.Value,
                    });
                }
            }

            result.TotalSize = result.Files.Sum(f => f.Size);

            if (hasV1Pieces)
            {
                if (rawTotalSize > 0)
                {
                    var expectedPieceCount = (int)Math.Ceiling((double)rawTotalSize / result.PieceLength);
                    if (result.PieceCount != expectedPieceCount)
                    {
                        throw new InvalidTorrentFileException("Piece count does not match total file size.");
                    }
                }
            }
            else
            {
                result.PieceCount = result.TotalSize > 0 && result.PieceLength > 0
                    ? (int)Math.Ceiling((double)result.TotalSize / result.PieceLength)
                    : 0;
            }

            return result;
        }
        catch (InvalidTorrentFileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidTorrentFileException($"Failed to parse torrent file: {ex.Message}", ex);
        }
    }

    public static void ValidateBencodeDepth(ReadOnlySpan<byte> data, int maxDepth = 64)
    {
        if (data.IsEmpty)
        {
            throw new InvalidTorrentFileException("Bencode content is empty.");
        }

        var depth = 0;
        var index = 0;
        var length = data.Length;

        while (index < length)
        {
            var b = data[index];
            if (b == (byte)'d' || b == (byte)'l')
            {
                depth++;
                if (depth > maxDepth)
                {
                    throw new InvalidTorrentFileException($"Bencode structure exceeds maximum recursion depth of {maxDepth}.");
                }

                index++;
            }
            else if (b == (byte)'e')
            {
                depth--;
                if (depth < 0)
                {
                    throw new InvalidTorrentFileException("Malformed bencode: unexpected 'e'.");
                }

                index++;

                if (depth == 0)
                {
                    while (index < length && (data[index] == (byte)'\r' || data[index] == (byte)'\n' || data[index] == (byte)' ' || data[index] == (byte)'\t' || data[index] == 0))
                    {
                        index++;
                    }
                }
            }
            else if (b == (byte)'i')
            {
                index++;
                while (index < length && data[index] != (byte)'e')
                {
                    index++;
                }

                if (index >= length)
                {
                    throw new InvalidTorrentFileException("Malformed bencode: unterminated integer.");
                }

                index++; // skip 'e'
            }
            else if (b >= (byte)'0' && b <= (byte)'9')
            {
                var lenStart = index;
                while (index < length && data[index] >= (byte)'0' && data[index] <= (byte)'9')
                {
                    index++;
                }

                if (index >= length || data[index] != (byte)':')
                {
                    throw new InvalidTorrentFileException("Malformed bencode: invalid string length specifier.");
                }

                var lenSpan = data.Slice(lenStart, index - lenStart);
                if (!long.TryParse(System.Text.Encoding.ASCII.GetString(lenSpan), out var strLen) || strLen < 0)
                {
                    throw new InvalidTorrentFileException("Malformed bencode: invalid string length.");
                }

                index++; // skip ':'

                if (index + strLen > length || strLen > int.MaxValue)
                {
                    throw new InvalidTorrentFileException("Malformed bencode: string length extends beyond buffer.");
                }

                index += (int)strLen;
            }
            else
            {
                throw new InvalidTorrentFileException($"Malformed bencode: unexpected byte 0x{b:X2} at position {index}.");
            }
        }

        if (depth != 0)
        {
            throw new InvalidTorrentFileException("Malformed bencode: unclosed dictionary or list.");
        }
    }

    private static BString GetUtf8String(BDictionary dict, string key)
    {
        if (dict.ContainsKey($"{key}.utf-8") && dict[$"{key}.utf-8"] is BString utf8Str)
        {
            return utf8Str;
        }

        if (dict.ContainsKey($"{key}.utf8") && dict[$"{key}.utf8"] is BString utf8AltStr)
        {
            return utf8AltStr;
        }

        if (dict.ContainsKey(key) && dict[key] is BString normalStr)
        {
            return normalStr;
        }

        return null;
    }

    private static BList GetUtf8PathList(BDictionary dict)
    {
        if (dict.ContainsKey("path.utf-8") && dict["path.utf-8"] is BList utf8List && utf8List.Count > 0)
        {
            return utf8List;
        }

        if (dict.ContainsKey("path.utf8") && dict["path.utf8"] is BList utf8AltList && utf8AltList.Count > 0)
        {
            return utf8AltList;
        }

        if (dict.ContainsKey("path") && dict["path"] is BList pathList && pathList.Count > 0)
        {
            return pathList;
        }

        return null;
    }

    private static bool IsPaddingFile(BDictionary fileMeta, string relativeFilePath)
    {
        if (fileMeta.ContainsKey("attr") && fileMeta["attr"] is BString attrStr)
        {
            var attrVal = attrStr.ToString();
            if (attrVal.Contains('p', StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (relativeFilePath.StartsWith(".pad/", StringComparison.OrdinalIgnoreCase) ||
            relativeFilePath.Contains("/.pad/", StringComparison.OrdinalIgnoreCase) ||
            relativeFilePath.Equals(".pad", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void TraverseFileTree(
        BDictionary dirDict,
        List<string> currentPath,
        List<ParsedTorrentFile> files,
        int depth,
        int maxDepth,
        ref long rawTotalSize)
    {
        if (depth > maxDepth)
        {
            throw new InvalidTorrentFileException($"File tree exceeds maximum directory depth of {maxDepth}.");
        }

        foreach (var (keyBString, valueObj) in dirDict)
        {
            var key = keyBString.ToString();
            if (valueObj is not BDictionary childDict)
            {
                throw new InvalidTorrentFileException("Malformed file tree: node is not a dictionary.");
            }

            if (childDict.ContainsKey(string.Empty))
            {
                if (childDict[string.Empty] is not BDictionary fileMeta)
                {
                    throw new InvalidTorrentFileException("Malformed file tree: file metadata is not a dictionary.");
                }

                if (!fileMeta.ContainsKey("length") || fileMeta["length"] is not BNumber fileLengthNum)
                {
                    throw new InvalidTorrentFileException("Malformed file tree: file entry missing or invalid 'length'.");
                }

                if (fileLengthNum.Value < 0)
                {
                    throw new InvalidTorrentFileException("File length cannot be negative.");
                }

                ValidateAndSanitizePathPart(key);
                var filePathParts = new List<string>(currentPath) { key };
                var relativeFilePath = string.Join("/", filePathParts);

                if (Path.IsPathRooted(relativeFilePath) || relativeFilePath.StartsWith('/') || relativeFilePath.StartsWith('\\'))
                {
                    throw new InvalidTorrentFileException($"Malformed torrent file: resolved file path cannot be an absolute path: '{relativeFilePath}'.");
                }

                rawTotalSize += fileLengthNum.Value;

                if (!IsPaddingFile(fileMeta, relativeFilePath))
                {
                    files.Add(new ParsedTorrentFile
                    {
                        Path = relativeFilePath,
                        Size = fileLengthNum.Value,
                    });
                }
            }
            else
            {
                ValidateAndSanitizePathPart(key);
                var nextPath = new List<string>(currentPath) { key };
                TraverseFileTree(childDict, nextPath, files, depth + 1, maxDepth, ref rawTotalSize);
            }
        }
    }

    private static void ExtractAnnounceList(BList announceList, List<List<string>> targetList)
    {
        foreach (var item in announceList)
        {
            if (item is BList tierList)
            {
                var tierUrls = tierList.OfType<BString>()
                    .Select(u => u.ToString().Trim())
                    .Where(u => !string.IsNullOrEmpty(u))
                    .ToList();
                if (tierUrls.Count > 0)
                {
                    targetList.Add(tierUrls);
                }
            }
            else if (item is BString singleUrlStr)
            {
                var u = singleUrlStr.ToString().Trim();
                if (!string.IsNullOrEmpty(u))
                {
                    targetList.Add(new List<string> { u });
                }
            }
        }
    }

    private static string SanitizeTorrentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidTorrentFileException("Malformed torrent file: torrent name is missing or empty.");
        }

        if (name.Contains('\0'))
        {
            throw new InvalidTorrentFileException("Malformed torrent file: torrent name contains null byte.");
        }

        var normalized = name.Replace('\\', '/');

        if (normalized.StartsWith('/') ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw new InvalidTorrentFileException($"Malformed torrent file: torrent name cannot be an absolute path: '{name}'.");
        }

        var parts = normalized.Split('/');
        if (parts.Any(p => p.Trim() == "." || p.Trim() == ".."))
        {
            throw new InvalidTorrentFileException($"Malformed torrent file: torrent name contains directory traversal sequence: '{name}'.");
        }

        return string.Join("_", parts.Where(p => p.Length > 0));
    }

    private static void ValidateAndSanitizePathPart(string part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            throw new InvalidTorrentFileException("Malformed torrent file: file path component is empty or whitespace.");
        }

        if (part.Contains('\0'))
        {
            throw new InvalidTorrentFileException($"Malformed torrent file: file path component contains null byte: '{part}'.");
        }

        if (part.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidTorrentFileException($"Malformed torrent file: file path component contains invalid path characters: '{part}'.");
        }

        if (Path.IsPathRooted(part) || part.StartsWith('/') || part.StartsWith('\\') ||
            (part.Length >= 2 && char.IsLetter(part[0]) && part[1] == ':'))
        {
            throw new InvalidTorrentFileException($"Malformed torrent file: file path component cannot be an absolute path: '{part}'.");
        }

        var segments = part.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new InvalidTorrentFileException($"Malformed torrent file: file path component contains empty segment: '{part}'.");
            }

            if (trimmed == "." || trimmed == "..")
            {
                throw new InvalidTorrentFileException($"Malformed torrent file: file path component contains directory traversal sequence: '{part}'.");
            }
        }
    }
}
