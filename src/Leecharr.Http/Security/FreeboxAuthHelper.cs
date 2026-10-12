// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Leecharr.Http.Security;

public static class FreeboxAuthHelper
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "HMAC-SHA1 is required by the Freebox API session handshake")]
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
