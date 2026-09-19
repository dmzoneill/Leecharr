// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        var cleanUri = magnetUri.Trim();
        var fragmentIndex = cleanUri.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            cleanUri = cleanUri.Substring(0, fragmentIndex);
        }

        if (!cleanUri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Invalid magnet link prefix");
        }

        var result = new ParsedMagnetLink();
        var query = cleanUri.Substring("magnet:?".Length);
        var parameters = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

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
                case string k when k == "xt" || k.StartsWith("xt.", StringComparison.Ordinal):
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
                        else if (hash.Length is 52 or 55 or 56)
                        {
                            var hex = Base32ToHex(hash).ToLowerInvariant();
                            if (hex.Length == 68 && hex.StartsWith("1220", StringComparison.OrdinalIgnoreCase))
                            {
                                result.V2InfoHash = hex.Substring(4);
                            }
                            else if (hex.Length == 64)
                            {
                                result.V2InfoHash = hex;
                            }
                            else
                            {
                                throw new FormatException($"Invalid btmh info hash: {hash}");
                            }
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

                case string k when k == "tr" || k.StartsWith("tr.", StringComparison.Ordinal):
                    if (!string.IsNullOrWhiteSpace(value) && !result.Trackers.Contains(value))
                    {
                        result.Trackers.Add(value);
                    }

                    break;

                case "x.pe":
                    result.ExactPeers.Add(value);
                    break;

                case "ws" or "as":
                    if (!string.IsNullOrWhiteSpace(value) && !result.WebSeeds.Contains(value))
                    {
                        result.WebSeeds.Add(value);
                    }

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

    public static string BuildMagnetUri(string infoHash, string name = null, IEnumerable<string> trackers = null, string v2InfoHash = null)
    {
        var sb = new StringBuilder("magnet:?");
        var clean = infoHash?.Trim();
        if (!string.IsNullOrEmpty(clean))
        {
            if (clean.Length == 64 && IsValidHex(clean))
            {
                sb.Append($"xt=urn:btmh:1220{clean.ToLowerInvariant()}");
            }
            else if (clean.Length == 68 && clean.StartsWith("1220", StringComparison.OrdinalIgnoreCase) && IsValidHex(clean))
            {
                sb.Append($"xt=urn:btmh:{clean.ToLowerInvariant()}");
            }
            else
            {
                sb.Append($"xt=urn:btih:{clean.ToLowerInvariant()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(v2InfoHash))
        {
            var cleanV2 = v2InfoHash.Trim();
            var v2Btmh = cleanV2.StartsWith("1220", StringComparison.OrdinalIgnoreCase) ? cleanV2 : $"1220{cleanV2}";
            var prefix = sb.Length > 8 ? "&" : string.Empty;
            sb.Append($"{prefix}xt=urn:btmh:{v2Btmh.ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var prefix = sb.Length > 8 ? "&" : string.Empty;
            sb.Append($"{prefix}dn={Uri.EscapeDataString(name)}");
        }

        if (trackers != null)
        {
            foreach (var tr in trackers.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var prefix = sb.Length > 8 ? "&" : string.Empty;
                sb.Append($"{prefix}tr={Uri.EscapeDataString(tr)}");
            }
        }

        return sb.ToString();
    }

    public static string NormalizeInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return string.Empty;
        }

        var clean = infoHash.Trim();
        if (clean.Length is 32 or 52 or 55 or 56)
        {
            try
            {
                var hex = Base32ToHex(clean).ToLowerInvariant();
                if (hex.Length == 68 && hex.StartsWith("1220", StringComparison.OrdinalIgnoreCase))
                {
                    return hex.Substring(4);
                }

                if (hex.Length is 40 or 64)
                {
                    return hex;
                }
            }
            catch
            {
                return clean.ToLowerInvariant();
            }
        }

        if (clean.Length == 68 && clean.StartsWith("1220", StringComparison.OrdinalIgnoreCase) && IsValidHex(clean))
        {
            return clean.Substring(4).ToLowerInvariant();
        }

        return clean.ToLowerInvariant();
    }

    public static string Base32ToHex(string base32)
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

    public static bool IsValidHex(string hex)
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
