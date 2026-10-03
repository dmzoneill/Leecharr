// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Leecharr.Http.Authentication;

public static class FacebookAuthHelper
{
    public static string GenerateAppSecretProof(string accessToken, string appSecret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(accessToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
