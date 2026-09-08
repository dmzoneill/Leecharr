// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;

namespace Leecharr.Core.Test.Security;

[TestFixture]
public class CertificateManagerTest
{
    private string tempDir = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private IConfigFileProvider config = null!;
    private CertificateManager certificateManager = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), $"leecharr-cert-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);

        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.tempDir);

        this.config = Substitute.For<IConfigFileProvider>();
        this.config.BindAddress.Returns("127.0.0.1");
        this.config.Port.Returns(7889);
        this.config.EnableSsl.Returns(true);
        this.config.SslPort.Returns(7890);
        this.config.SslCertPath.Returns(string.Empty);
        this.config.SslKeyPath.Returns(string.Empty);
        this.config.SslCertPassword.Returns(string.Empty);

        this.certificateManager = new CertificateManager(this.appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            try
            {
                Directory.Delete(this.tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void Constructor_WhenAppFolderInfoNull_ThrowsArgumentNullException()
    {
        Action act = () => new CertificateManager(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void GetOrCreateCertificate_WhenCertPathEmpty_GeneratesAndCachesSelfSignedCert()
    {
        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();
        cert.Subject.Should().Contain("Leecharr");
        cert.NotAfter.ToUniversalTime().Should().BeOnOrBefore(DateTime.UtcNow.AddDays(398));
        cert.NotAfter.ToUniversalTime().Should().BeAfter(DateTime.UtcNow.AddDays(365));

        var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        basicConstraints.Should().NotBeNull();
        basicConstraints!.CertificateAuthority.Should().BeFalse();
        basicConstraints.Critical.Should().BeTrue();

        var ski = cert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().SingleOrDefault();
        ski.Should().NotBeNull();
        ski!.SubjectKeyIdentifier.Should().NotBeNullOrWhiteSpace();

        var cachedPfx = Path.Combine(this.tempDir, "leecharr-selfsigned.pfx");
        File.Exists(cachedPfx).Should().BeTrue();

        var cachedPwd = Path.Combine(this.tempDir, "leecharr-selfsigned.pwd");
        File.Exists(cachedPwd).Should().BeTrue();
        File.ReadAllText(cachedPwd).Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void GetOrCreateCertificate_WhenCustomPasswordConfigured_GeneratesWithConfiguredPassword()
    {
        this.config.SslCertPassword.Returns("custom-secret-password-123");

        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();

        var cachedPfx = Path.Combine(this.tempDir, "leecharr-selfsigned.pfx");
        File.Exists(cachedPfx).Should().BeTrue();

        // Verify PFX can be loaded with the configured password
        var loaded = X509CertificateLoader.LoadPkcs12FromFile(cachedPfx, "custom-secret-password-123", X509KeyStorageFlags.Exportable);
        loaded.Thumbprint.Should().Be(cert.Thumbprint);
    }

    [Test]
    public void GetOrCreateCertificate_WhenSelfSignedCertCached_ReusesExistingCert()
    {
        var cert1 = this.certificateManager.GetOrCreateCertificate(this.config);
        var cert2 = this.certificateManager.GetOrCreateCertificate(this.config);

        cert1.Thumbprint.Should().Be(cert2.Thumbprint);
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenSelfSigned_ReturnsValidResultWithSans()
    {
        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: string.Empty,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue(result.Message);
        result.HasPrivateKey.Should().BeTrue();
        result.Subject.Should().Contain("Leecharr");
        result.SubjectAlternativeNames.Should().Contain("localhost");
        result.SubjectAlternativeNames.Should().Contain("127.0.0.1");
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenCertPathNotFound_ReturnsInvalidResult()
    {
        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: Path.Combine(this.tempDir, "missing-cert.pfx"),
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenCustomPfxProvided_ValidatesSuccessfully()
    {
        var customPfxPath = Path.Combine(this.tempDir, "custom-test.pfx");
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=CustomHost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var testCert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pfxBytes = testCert.Export(X509ContentType.Pfx, "testpass");
        File.WriteAllBytes(customPfxPath, pfxBytes);

        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: customPfxPath,
            keyPath: string.Empty,
            password: "testpass",
            bindAddress: "127.0.0.1",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Subject.Should().Contain("CustomHost");
        result.HasPrivateKey.Should().BeTrue();
    }

    [Test]
    public void GetOrCreateCertificate_WhenPemWithIntermediateChainAndSeparateKey_LoadsFullChainAndNormalizesToPkcs12()
    {
        var (fullChainPem, keyPem, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(this.tempDir, "fullchain.pem");
        var keyPemPath = Path.Combine(this.tempDir, "privkey.pem");

        File.WriteAllText(certPemPath, fullChainPem);
        File.WriteAllText(keyPemPath, keyPem);

        this.config.SslCertPath.Returns(certPemPath);
        this.config.SslKeyPath.Returns(keyPemPath);

        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();
        cert.Subject.Should().Contain("leechar-server.local");
        cert.Issuer.Should().Contain("Test Intermediate CA");
    }

    [Test]
    public void GetOrCreateCertificate_WhenPemWithIntermediateChainAndKeyInSingleFile_LoadsFullChainAndNormalizesToPkcs12()
    {
        var (fullChainPem, keyPem, _, _) = GenerateTestChain();
        var combinedPem = $"{fullChainPem}\n{keyPem}";
        var bundlePemPath = Path.Combine(this.tempDir, "bundle.pem");

        File.WriteAllText(bundlePemPath, combinedPem);

        this.config.SslCertPath.Returns(bundlePemPath);
        this.config.SslKeyPath.Returns(string.Empty);

        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();
        cert.Subject.Should().Contain("leechar-server.local");
        cert.Issuer.Should().Contain("Test Intermediate CA");
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenPemWithFullChain_ReturnsValidWithSansAndPrivateKey()
    {
        var (fullChainPem, keyPem, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(this.tempDir, "fullchain.pem");
        var keyPemPath = Path.Combine(this.tempDir, "privkey.pem");

        File.WriteAllText(certPemPath, fullChainPem);
        File.WriteAllText(keyPemPath, keyPem);

        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: certPemPath,
            keyPath: keyPemPath,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue(result.Message);
        result.HasPrivateKey.Should().BeTrue();
        result.Subject.Should().Contain("leechar-server.local");
        result.SubjectAlternativeNames.Should().Contain("leechar-server.local");
        result.SubjectAlternativeNames.Should().Contain("127.0.0.1");
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenPemMissingPrivateKey_ReturnsInvalidResult()
    {
        var (fullChainPem, _, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(this.tempDir, "certonly.pem");

        File.WriteAllText(certPemPath, fullChainPem);

        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: certPemPath,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("does not contain a private key");
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenPemWithEncryptedPrivateKey_ValidatesSuccessfullyWithPassword()
    {
        using var caRsa = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=Test Intermediate CA", caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var caCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(2));

        using var leafRsa = RSA.Create(2048);
        var leafReq = new CertificateRequest("CN=encrypted-leaf.local", leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var leafCert = leafReq.Create(caCert, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(1), serial);

        var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
        var encryptedKeyPem = leafRsa.ExportEncryptedPkcs8PrivateKeyPem("secretpassword".AsSpan(), pbeParams);
        var fullChainPem = $"{leafCert.ExportCertificatePem()}\n{caCert.ExportCertificatePem()}";

        var certPemPath = Path.Combine(this.tempDir, "fullchain-enc.pem");
        var keyPemPath = Path.Combine(this.tempDir, "privkey-enc.pem");

        File.WriteAllText(certPemPath, fullChainPem);
        File.WriteAllText(keyPemPath, encryptedKeyPem);

        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: certPemPath,
            keyPath: keyPemPath,
            password: "secretpassword",
            bindAddress: "127.0.0.1",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue(result.Message);
        result.HasPrivateKey.Should().BeTrue();
        result.Subject.Should().Contain("encrypted-leaf.local");
    }

    [Test]
    public void GetOrCreateCertificate_SelfSignedCert_HasBasicConstraintsAndSubjectKeyIdentifierAndValidLifetime()
    {
        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();

        // 398-day browser limit compliance (capping to 397 days)
        var totalDays = (cert.NotAfter.ToUniversalTime() - cert.NotBefore.ToUniversalTime()).TotalDays;
        totalDays.Should().BeLessThanOrEqualTo(398);
        cert.NotAfter.ToUniversalTime().Should().BeOnOrBefore(DateTime.UtcNow.AddDays(398));
        cert.NotAfter.ToUniversalTime().Should().BeAfter(DateTime.UtcNow.AddDays(365));

        // Basic Constraints: CA = false, Critical = true (RFC 5280)
        var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        basicConstraints.Should().NotBeNull();
        basicConstraints!.CertificateAuthority.Should().BeFalse();
        basicConstraints.Critical.Should().BeTrue();

        // Subject Key Identifier (RFC 5280)
        var ski = cert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().SingleOrDefault();
        ski.Should().NotBeNull();
        ski!.SubjectKeyIdentifier.Should().NotBeNullOrWhiteSpace();

        // Key Usage
        var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        keyUsage.Should().NotBeNull();
        keyUsage!.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.DigitalSignature);
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.KeyEncipherment);

        // Enhanced Key Usage (Server Authentication)
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        eku.Should().NotBeNull();
        eku!.EnhancedKeyUsages["1.3.6.1.5.5.7.3.1"].Should().NotBeNull();
    }

    [Test]
    public void GetOrCreateCertificate_WhenBindAddressIsHostname_IncludesDnsSan()
    {
        this.config.BindAddress.Returns("leecharr.local");

        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        var sanExt = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
        sanExt.Should().NotBeNull();

        var dnsNames = sanExt!.EnumerateDnsNames().ToList();
        dnsNames.Should().Contain("leecharr.local");
        dnsNames.Should().Contain("localhost");
    }

    [Test]
    public void GetOrCreateCertificate_WhenBindAddressIsWildcard_DoesNotIncludeWildcardSan()
    {
        this.config.BindAddress.Returns("*");

        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        var sanExt = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
        sanExt.Should().NotBeNull();

        var dnsNames = sanExt!.EnumerateDnsNames().ToList();
        dnsNames.Should().NotContain("*");
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenBindAddressIsHostname_IncludesHostnameInSubjectAlternativeNames()
    {
        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: string.Empty,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "media.home.arpa",
            sslPort: 7890,
            testTlsHandshake: false);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue(result.Message);
        result.SubjectAlternativeNames.Should().Contain("media.home.arpa");
    }

    private static (string FullChainPem, string KeyPem, string LeafPem, string CaPem) GenerateTestChain(
        string subjectName = "CN=leechar-server.local",
        string caSubject = "CN=Test Intermediate CA")
    {
        using var caRsa = RSA.Create(2048);
        var caReq = new CertificateRequest(caSubject, caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var caCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(2));

        using var leafRsa = RSA.Create(2048);
        var leafReq = new CertificateRequest(subjectName, leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("leechar-server.local");
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        leafReq.CertificateExtensions.Add(sanBuilder.Build());

        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var leafCert = leafReq.Create(caCert, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(1), serial);

        var caPem = caCert.ExportCertificatePem();
        var leafPem = leafCert.ExportCertificatePem();
        var keyPem = leafRsa.ExportPkcs8PrivateKeyPem();
        var fullChainPem = $"{leafPem}\n{caPem}";

        return (fullChainPem, keyPem, leafPem, caPem);
    }
}
