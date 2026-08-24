using System;
using System.Security.Cryptography;
using System.Text;

namespace Leecharr.Http.Authentication;

public static class FacebookAuthHelper
{
    public static string GenerateAppSecretProof(string accessToken, string appSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(accessToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
