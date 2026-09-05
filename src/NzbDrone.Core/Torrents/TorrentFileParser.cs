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
    private readonly Logger logger;

    public TorrentFileParser()
    {
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public ParsedTorrent Parse(string filePath)
    {
        this.logger.Debug("Parsing torrent file: {0}", filePath);
        using var stream = File.OpenRead(filePath);
        return this.Parse(stream);
    }

    public ParsedTorrent Parse(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        using var stream = new MemoryStream(bytes);
        return this.Parse(stream);
    }

    public ParsedTorrent Parse(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        try
        {
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

            if (!info.ContainsKey("pieces") || info["pieces"] is not BString piecesStr)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'pieces'.");
            }

            if (piecesStr.Value.Length == 0 || piecesStr.Value.Length % 20 != 0)
            {
                throw new InvalidTorrentFileException("Pieces hash string length must be a non-zero multiple of 20.");
            }

            if (!info.ContainsKey("name") || info["name"] is not BString nameStr)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'name'.");
            }

            var pieceCount = piecesStr.Value.Length / 20;

            string announceUrl = null;
            if (torrent.ContainsKey("announce") && torrent["announce"] is BString mainAnnounceStr)
            {
                var s = mainAnnounceStr.ToString().Trim();
                if (!string.IsNullOrEmpty(s))
                {
                    announceUrl = s;
                }
            }
            else if (info.ContainsKey("announce") && info["announce"] is BString infoAnnounceStr)
            {
                var s = infoAnnounceStr.ToString().Trim();
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
                InfoHash = InfoHashCalculator.Calculate(info),
                PieceLength = (int)pieceLengthNum.Value,
                PieceCount = pieceCount,
                PieceHashes = piecesStr.Value.ToArray(),
                Comment = torrent.ContainsKey("comment") ? (torrent["comment"] as BString)?.ToString() : null,
                IsPrivate = info.ContainsKey("private") &&
                    (((info["private"] as BNumber)?.Value == 1) ||
                     ((info["private"] as BString)?.ToString() == "1")),
                AnnounceUrl = announceUrl,
                AnnounceList = announceListParsed,
                Files = new List<ParsedTorrentFile>(),
            };

            if (torrent.ContainsKey("creation date") && torrent["creation date"] is BNumber creationDateNum)
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(creationDateNum.Value).UtcDateTime;
            }

            if (info.ContainsKey("files") && info["files"] is BList files)
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

                    if (!file.ContainsKey("path") || file["path"] is not BList pathList || pathList.Count == 0)
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

                    result.Files.Add(new ParsedTorrentFile
                    {
                        Path = relativeFilePath,
                        Size = fileLengthNum.Value,
                    });
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

                result.Files.Add(new ParsedTorrentFile
                {
                    Path = sanitizedTorrentName,
                    Size = lengthNum.Value,
                });
            }

            result.TotalSize = result.Files.Sum(f => f.Size);

            if (result.TotalSize > 0)
            {
                var expectedPieceCount = (int)Math.Ceiling((double)result.TotalSize / result.PieceLength);
                if (result.PieceCount != expectedPieceCount)
                {
                    throw new InvalidTorrentFileException("Piece count does not match total file size.");
                }
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
