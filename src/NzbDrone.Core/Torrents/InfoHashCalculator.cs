// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Security.Cryptography;
using BencodeNET.Objects;

namespace NzbDrone.Core.Torrents;

public static class InfoHashCalculator
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S4790", Justification = "SHA1 is mandated by the BitTorrent BEP 3 protocol specification for info-hashes")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA1 is mandated by the BitTorrent BEP 3 protocol specification for info-hashes")]
    public static string Calculate(ReadOnlySpan<byte> infoDictionaryBytes)
    {
        if (infoDictionaryBytes.IsEmpty)
        {
            throw new ArgumentException("Info dictionary bytes cannot be empty.", nameof(infoDictionaryBytes));
        }

        var hash = SHA1.HashData(infoDictionaryBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Calculate(BDictionary infoDictionary)
    {
        ArgumentNullException.ThrowIfNull(infoDictionary);

        return Calculate(infoDictionary.EncodeAsBytes());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S4790", Justification = "SHA1 is mandated by the BitTorrent BEP 3 protocol specification for info-hashes")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA1 is mandated by the BitTorrent BEP 3 protocol specification for info-hashes")]
    public static byte[] CalculateBytes(ReadOnlySpan<byte> infoDictionaryBytes)
    {
        if (infoDictionaryBytes.IsEmpty)
        {
            throw new ArgumentException("Info dictionary bytes cannot be empty.", nameof(infoDictionaryBytes));
        }

        return SHA1.HashData(infoDictionaryBytes);
    }

    public static byte[] CalculateBytes(BDictionary infoDictionary)
    {
        ArgumentNullException.ThrowIfNull(infoDictionary);

        return CalculateBytes(infoDictionary.EncodeAsBytes());
    }

    public static string CalculateV2(ReadOnlySpan<byte> infoDictionaryBytes)
    {
        if (infoDictionaryBytes.IsEmpty)
        {
            throw new ArgumentException("Info dictionary bytes cannot be empty.", nameof(infoDictionaryBytes));
        }

        var hash = SHA256.HashData(infoDictionaryBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string CalculateV2(BDictionary infoDictionary)
    {
        ArgumentNullException.ThrowIfNull(infoDictionary);

        return CalculateV2(infoDictionary.EncodeAsBytes());
    }

    public static byte[] CalculateV2Bytes(ReadOnlySpan<byte> infoDictionaryBytes)
    {
        if (infoDictionaryBytes.IsEmpty)
        {
            throw new ArgumentException("Info dictionary bytes cannot be empty.", nameof(infoDictionaryBytes));
        }

        return SHA256.HashData(infoDictionaryBytes);
    }

    public static byte[] CalculateV2Bytes(BDictionary infoDictionary)
    {
        ArgumentNullException.ThrowIfNull(infoDictionary);

        return CalculateV2Bytes(infoDictionary.EncodeAsBytes());
    }
}
