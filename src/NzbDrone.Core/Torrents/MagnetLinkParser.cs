// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Web;

namespace NzbDrone.Core.Torrents;

public class ParsedMagnetLink
{
    public string InfoHash { get; set; }

    public string V1InfoHash { get; set; }

    public string V2InfoHash { get; set; }

    public string DisplayName { get; set; }

    public List<string> Trackers { get; set; } = new();

    public List<string> ExactPeers { get; set; } = new();

    public List<string> WebSeeds { get; set; } = new();
}

public static class MagnetLinkParser
{
    public static ParsedMagnetLink Parse(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            throw new ArgumentException("Magnet URI cannot be null or empty", nameof(magnetUri));
        }

        if (!magnetUri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Invalid magnet link prefix");
        }

        var result = new ParsedMagnetLink();
        var query = magnetUri.Substring("magnet:?".Length);
        var parameters = query.Split('&');

        foreach (var param in parameters)
        {
            var kv = param.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = kv[0].Trim().ToLowerInvariant();
            var value = HttpUtility.UrlDecode(kv[1]);

            switch (key)
            {
                case "xt":
                    if (value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
                    {
                        var hash = value.Substring("urn:btih:".Length).Trim();

                        if (hash.Length == 32)
                        {
                            result.V1InfoHash = Base32ToHex(hash).ToLowerInvariant();
                        }
                        else if (hash.Length == 40 && IsValidHex(hash))
                        {
                            result.V1InfoHash = hash.ToLowerInvariant();
                        }
                        else
                        {
                            throw new FormatException($"Invalid btih info hash: {hash}");
                        }
                    }
                    else if (value.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase))
                    {
                        var hash = value.Substring("urn:btmh:".Length).Trim();

                        if (hash.StartsWith("1220", StringComparison.OrdinalIgnoreCase) && hash.Length == 68 && IsValidHex(hash))
                        {
                            result.V2InfoHash = hash.Substring(4).ToLowerInvariant();
                        }
                        else if (hash.Length == 64 && IsValidHex(hash))
                        {
                            result.V2InfoHash = hash.ToLowerInvariant();
                        }
                        else if (hash.Length == 32)
                        {
                            result.V2InfoHash = Base32ToHex(hash).ToLowerInvariant();
                        }
                        else if (hash.Length == 40 && IsValidHex(hash))
                        {
                            result.V2InfoHash = hash.ToLowerInvariant();
                        }
                        else
                        {
                            throw new FormatException($"Invalid btmh info hash: {hash}");
                        }
                    }

                    break;

                case "dn":
                    result.DisplayName = value;
                    break;

                case "tr":
                    if (!string.IsNullOrWhiteSpace(value) && !result.Trackers.Contains(value))
                    {
                        result.Trackers.Add(value);
                    }

                    break;

                case "x.pe":
                    result.ExactPeers.Add(value);
                    break;

                case "ws":
                    result.WebSeeds.Add(value);
                    break;
            }
        }

        result.InfoHash = result.V1InfoHash ?? result.V2InfoHash;

        if (string.IsNullOrEmpty(result.InfoHash))
        {
            throw new FormatException("Magnet link missing valid info hash (xt=urn:btih:... or xt=urn:btmh:...)");
        }

        return result;
    }

    private static string Base32ToHex(string base32)
    {
        const string b32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32.ToUpperInvariant().TrimEnd('=');
        var bits = new List<bool>();

        foreach (var c in clean)
        {
            var val = b32Alphabet.IndexOf(c);
            if (val < 0)
            {
                throw new FormatException($"Invalid character in Base32 string: '{c}'");
            }

            for (var bit = 4; bit >= 0; bit--)
            {
                bits.Add((val & (1 << bit)) != 0);
            }
        }

        var bytes = new byte[bits.Count / 8];
        for (var i = 0; i < bytes.Length; i++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                if (bits[(i * 8) + bit])
                {
                    bytes[i] |= (byte)(1 << (7 - bit));
                }
            }
        }

        return Convert.ToHexString(bytes);
    }

    private static bool IsValidHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }
}
