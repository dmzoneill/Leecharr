using System;
using System.Security.Cryptography;
using BencodeNET.Objects;

namespace NzbDrone.Core.Torrents;

public static class InfoHashCalculator
{
    public static string Calculate(BDictionary infoDictionary)
    {
        if (infoDictionary == null)
        {
            throw new ArgumentNullException(nameof(infoDictionary));
        }

        var encoded = infoDictionary.EncodeAsBytes();
        var hash = SHA1.HashData(encoded);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] CalculateBytes(BDictionary infoDictionary)
    {
        if (infoDictionary == null)
        {
            throw new ArgumentNullException(nameof(infoDictionary));
        }

        var encoded = infoDictionary.EncodeAsBytes();
        return SHA1.HashData(encoded);
    }
}
