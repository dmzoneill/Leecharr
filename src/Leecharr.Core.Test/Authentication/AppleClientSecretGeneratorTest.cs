// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Leecharr.Http.Authentication;
using NUnit.Framework;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class AppleClientSecretGeneratorTest
{
    private string validPrivateKeyPem = null!;
    private ECDsa keyPair = null!;

    [SetUp]
    public void SetUp()
    {
        this.keyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        this.validPrivateKeyPem = this.keyPair.ExportECPrivateKeyPem();
    }

    [TearDown]
    public void TearDown()
    {
        this.keyPair.Dispose();
    }

    [Test]
    public void GenerateClientSecret_WithValidParameters_GeneratesVerifiableSignedJwt()
    {
        const string teamId = "APPLE_TEAM_ID_123";
        const string clientId = "com.leecharr.app";
        const string keyId = "KEY_ID_XYZ";
        const int expirationMinutes = 45;

        var jwt = AppleClientSecretGenerator.GenerateClientSecret(teamId, clientId, keyId, this.validPrivateKeyPem, expirationMinutes);

        jwt.Should().NotBeNullOrWhiteSpace();
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3, "JWT should consist of header, payload, and signature");

        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        var headerDoc = JsonDocument.Parse(headerJson);
        headerDoc.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        headerDoc.RootElement.GetProperty("kid").GetString().Should().Be(keyId);
        headerDoc.RootElement.GetProperty("typ").GetString().Should().Be("JWT");

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        var payloadDoc = JsonDocument.Parse(payloadJson);
        payloadDoc.RootElement.GetProperty("iss").GetString().Should().Be(teamId);
        payloadDoc.RootElement.GetProperty("sub").GetString().Should().Be(clientId);
        payloadDoc.RootElement.GetProperty("aud").GetString().Should().Be("https://appleid.apple.com");

        var iat = payloadDoc.RootElement.GetProperty("iat").GetInt64();
        var exp = payloadDoc.RootElement.GetProperty("exp").GetInt64();
        (exp - iat).Should().Be(expirationMinutes * 60);

        var dataToVerify = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signatureBytes = Base64UrlDecode(parts[2]);

        var isValid = this.keyPair.VerifyData(dataToVerify, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        isValid.Should().BeTrue("The signature should be verifiable with the corresponding public key");
    }

    [Test]
    public void GenerateClientSecret_WithPkcs8Pem_Succeeds()
    {
        var pkcs8Pem = this.keyPair.ExportPkcs8PrivateKeyPem();
        var jwt = AppleClientSecretGenerator.GenerateClientSecret("TEAM", "client", "K1", pkcs8Pem, 10);

        jwt.Should().NotBeNullOrWhiteSpace();
        jwt.Split('.').Should().HaveCount(3);
    }

    [Test]
    public void GenerateClientSecret_WithInvalidPem_ThrowsCryptographicExceptionAndDisposesHandle()
    {
        var act = () => AppleClientSecretGenerator.GenerateClientSecret("TEAM", "client", "K1", "INVALID_PEM", 10);

        act.Should().Throw<ArgumentException>();
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2:
                output += "==";
                break;
            case 3:
                output += "=";
                break;
        }

        return Convert.FromBase64String(output);
    }
}
