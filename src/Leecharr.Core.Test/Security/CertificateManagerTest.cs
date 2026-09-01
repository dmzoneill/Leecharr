// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
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
        cert.NotAfter.Should().BeAfter(DateTime.UtcNow.AddYears(4));

        var cachedPfx = Path.Combine(this.tempDir, "leecharr-selfsigned.pfx");
        File.Exists(cachedPfx).Should().BeTrue();
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
}
