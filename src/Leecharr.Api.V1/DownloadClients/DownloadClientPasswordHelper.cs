// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Leecharr.Api.V1.DownloadClients;

public static class DownloadClientPasswordHelper
{
    private const string Prefix = "enc:";
    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("Leecharr.DownloadClient.Password.Entropy.Key"));
    private static readonly byte[] Iv = SHA256.HashData(Encoding.UTF8.GetBytes("Leecharr.DownloadClient.Password.Entropy.Iv"))[..16];

    public static string Protect(string password, IDataProtector protector = null)
    {
        if (string.IsNullOrEmpty(password) || password.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return password;
        }

        if (protector != null)
        {
            try
            {
                return Prefix + protector.Protect(password);
            }
            catch
            {
                // Fall back to Aes
            }
        }

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(password);
        var cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return Prefix + Convert.ToBase64String(cipher);
    }

    public static string Unprotect(string password, IDataProtector protector = null)
    {
        if (string.IsNullOrEmpty(password) || !password.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return password;
        }

        var cipherText = password[Prefix.Length..];

        if (protector != null)
        {
            try
            {
                return protector.Unprotect(cipherText);
            }
            catch
            {
                // Fall back to Aes
            }
        }

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
