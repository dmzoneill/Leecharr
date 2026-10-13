// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
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
    public void GetOrCreateCertificate_WhenSelfSignedKeyFilesAreWorldReadable_RestrictsThemToTheOwner()
    {
        if (!UnixPrivatePermissions.IsUnixLike)
        {
            Assert.Ignore("Unix file mode checks only apply on Linux/macOS/BSD.");
        }

        var cachedPfx = Path.Combine(this.tempDir, "leecharr-selfsigned.pfx");
        var cachedPwd = Path.Combine(this.tempDir, "leecharr-selfsigned.pwd");
        var worldReadable = UnixPrivatePermissions.PrivateFileMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        File.WriteAllText(cachedPwd, "preexisting-secret");
        File.WriteAllBytes(cachedPfx, new byte[] { 1, 2, 3 });
        File.SetUnixFileMode(cachedPwd, worldReadable);
        File.SetUnixFileMode(cachedPfx, worldReadable);

        var cert = this.certificateManager.GetOrCreateCertificate(this.config);

        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();
        File.GetUnixFileMode(cachedPfx).Should().Be(UnixPrivatePermissions.PrivateFileMode);
        File.GetUnixFileMode(cachedPwd).Should().Be(UnixPrivatePermissions.PrivateFileMode);

        File.SetUnixFileMode(cachedPfx, worldReadable);
        File.SetUnixFileMode(cachedPwd, worldReadable);

        var reused = this.certificateManager.GetOrCreateCertificate(this.config);

        reused.Thumbprint.Should().Be(cert.Thumbprint);
        File.GetUnixFileMode(cachedPfx).Should().Be(UnixPrivatePermissions.PrivateFileMode);
        File.GetUnixFileMode(cachedPwd).Should().Be(UnixPrivatePermissions.PrivateFileMode);
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
    public void GetOrCreateCertificate_WhenCustomCertPathMissing_ThrowsFileNotFoundException()
    {
        this.config.SslCertPath.Returns(Path.Combine(this.tempDir, "missing-custom.pfx"));

        Action act = () => this.certificateManager.GetOrCreateCertificate(this.config);

        act.Should().Throw<FileNotFoundException>();
        File.Exists(Path.Combine(this.tempDir, "leecharr-selfsigned.pfx")).Should().BeFalse();
    }

    [Test]
    public void GetOrCreateCertificate_WhenCustomPfxHasWrongPassword_ThrowsInvalidOperationException()
    {
        var customPfxPath = Path.Combine(this.tempDir, "custom-badpass.pfx");
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=BadPassHost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var testCert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(customPfxPath, testCert.Export(X509ContentType.Pfx, "correctpass"));

        this.config.SslCertPath.Returns(customPfxPath);
        this.config.SslCertPassword.Returns("wrongpass");

        Action act = () => this.certificateManager.GetOrCreateCertificate(this.config);

        act.Should().Throw<InvalidOperationException>();
        File.Exists(Path.Combine(this.tempDir, "leecharr-selfsigned.pfx")).Should().BeFalse();
    }

    [Test]
    public void GetOrCreateCertificate_WhenExplicitKeyPathMissing_Throws()
    {
        var (fullChainPem, _, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(this.tempDir, "certonly-key-missing.pem");
        File.WriteAllText(certPemPath, fullChainPem);

        this.config.SslCertPath.Returns(certPemPath);
        this.config.SslKeyPath.Returns(Path.Combine(this.tempDir, "missing-privkey.pem"));

        Action act = () => this.certificateManager.GetOrCreateCertificate(this.config);

        act.Should().Throw<Exception>();
        File.Exists(Path.Combine(this.tempDir, "leecharr-selfsigned.pfx")).Should().BeFalse();
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

    [Test]
    public async Task ValidateCertificateAsync_WhenTlsServerPresentsDifferentCertificate_DoesNotReportHandshakeSuccess()
    {
        using var serverRsa = RSA.Create(2048);
        var serverReq = new CertificateRequest("CN=TlsProbeServer", serverRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var listeningCert = serverReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));

        using var probeRsa = RSA.Create(2048);
        var probeReq = new CertificateRequest("CN=TlsProbeClient", probeRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var probeCert = probeReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
        var probePfxPath = Path.Combine(this.tempDir, "probe-cert.pfx");
        File.WriteAllBytes(probePfxPath, probeCert.Export(X509ContentType.Pfx, string.Empty));

        using var cts = new CancellationTokenSource();
        var port = await StartMinimalTlsServerAsync(listeningCert, cts.Token);

        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: probePfxPath,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: port,
            testTlsHandshake: true);

        result.IsValid.Should().BeTrue(result.Message);
        result.HandshakeSucceeded.Should().BeFalse();
        result.Message.Should().Contain("HTTPS listener will activate");

        cts.Cancel();
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenTlsServerPresentsMatchingCertificate_ReportsHandshakeSuccess()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=TlsProbeMatch", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pfxPath = Path.Combine(this.tempDir, "matching-server.pfx");
        File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, string.Empty));

        using var cts = new CancellationTokenSource();
        var port = await StartMinimalTlsServerAsync(cert, cts.Token);

        var result = await this.certificateManager.ValidateCertificateAsync(
            certPath: pfxPath,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: port,
            testTlsHandshake: true);

        result.IsValid.Should().BeTrue(result.Message);
        result.HandshakeSucceeded.Should().BeTrue();
        result.Message.Should().Contain("TLS handshake succeeded");

        cts.Cancel();
    }

    private static async Task<int> StartMinimalTlsServerAsync(X509Certificate2 serverCertificate, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync(cancellationToken);
                        _ = Task.Run(
                            async () =>
                            {
                                await using var tcpClient = client;
                                await using var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
                                await sslStream.AuthenticateAsServerAsync(serverCertificate, cancellationToken);

                                var buffer = new byte[4096];
#pragma warning disable CA2022 // Test server only needs to consume any client bytes
                                await sslStream.ReadAsync(buffer, cancellationToken);
#pragma warning restore CA2022

                                var response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
                                var responseBytes = Encoding.ASCII.GetBytes(response);
                                await sslStream.WriteAsync(responseBytes, cancellationToken);
                            },
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    listener.Stop();
                }
            },
            cancellationToken);

        await Task.Delay(50, cancellationToken);
        return port;
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
