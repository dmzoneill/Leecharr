// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.DownloadClients;

public static class DownloadClientPasswordCodec
{
    private const string Prefix = "enc:";
    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("Leecharr.DownloadClient.Password.Entropy.Key"));
    private static readonly byte[] Iv = SHA256.HashData(Encoding.UTF8.GetBytes("Leecharr.DownloadClient.Password.Entropy.Iv"))[..16];

    public static string Unprotect(string password)
    {
        if (string.IsNullOrEmpty(password) || !password.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return password;
        }

        var cipherText = password[Prefix.Length..];
        try
        {
            var bytes = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.IV = Iv;
            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return password;
        }
    }
}
