// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Leecharr.Http.Security;

public static class FreeboxAuthHelper
{
    public static string ComputeSessionPassword(string appToken, string challenge)
    {
        if (string.IsNullOrEmpty(appToken) || string.IsNullOrEmpty(challenge))
        {
            return string.Empty;
        }

        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(appToken), Encoding.UTF8.GetBytes(challenge));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
