using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Leecharr.Http.Authentication;

public static class AppleClientSecretGenerator
{
    public static string GenerateClientSecret(
        string teamId,
        string clientId,
        string keyId,
        string privateKeyPem,
        int expirationMinutes = 60)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var now = DateTimeOffset.UtcNow;
        var exp = now.AddMinutes(expirationMinutes);

        var header = new
        {
            alg = "ES256",
            kid = keyId,
            typ = "JWT"
        };

        var payload = new
        {
            iss = teamId,
            iat = now.ToUnixTimeSeconds(),
            exp = exp.ToUnixTimeSeconds(),
            aud = "https://appleid.apple.com",
            sub = clientId
        };

        var headerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header));
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        var headerBase64 = Base64UrlEncode(headerBytes);
        var payloadBase64 = Base64UrlEncode(payloadBytes);

        var stringToSign = $"{headerBase64}.{payloadBase64}";
        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(stringToSign), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var signatureBase64 = Base64UrlEncode(signature);

        return $"{stringToSign}.{signatureBase64}";
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
