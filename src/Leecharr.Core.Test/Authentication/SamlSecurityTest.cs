// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Leecharr.Api.V1.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class SamlSecurityTest
{
    private IUserService userService;
    private IIdentityProviderService identityProviderService;
    private IConfigFileProvider configFileProvider;
    private IConfigService configService;
    private IUserSessionRepository sessionRepository;
    private AuthController controller;

    [SetUp]
    public void SetUp()
    {
        this.userService = Substitute.For<IUserService>();
        this.identityProviderService = Substitute.For<IIdentityProviderService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.sessionRepository = Substitute.For<IUserSessionRepository>();

        this.controller = new AuthController(
            this.userService,
            this.identityProviderService,
            this.configFileProvider,
            this.configService,
            this.sessionRepository);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("leecharr.local");
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        this.controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    #region Open Redirect Tests

    [TestCase("/settings", true)]
    [TestCase("/torrents", true)]
    [TestCase("/", true)]
    [TestCase("/settings?tab=general", true)]
    [TestCase("/api/v1/status", true)]
    [TestCase("https://evil.com", false)]
    [TestCase("http://evil.com/phish", false)]
    [TestCase("//evil.com", false)]
    [TestCase("//evil.com/phish", false)]
    [TestCase("/\\evil.com", false)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("data:text/html,<script>alert(1)</script>", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase(null, false)]
    public void IsLocalUrl_ValidatesCorrectly(string url, bool expectedResult)
    {
        AuthController.IsLocalUrl(url).Should().Be(expectedResult);
    }

    [TestCase("https://evil.com", "/")]
    [TestCase("//evil.com", "/")]
    [TestCase("/\\evil.com", "/")]
    [TestCase("javascript:alert(1)", "/")]
    [TestCase("", "/")]
    [TestCase(null, "/")]
    [TestCase("/settings", "/settings")]
    [TestCase("/torrents", "/torrents")]
    [TestCase("/", "/")]
    public void SanitizeRedirectUrl_SanitizesOpenRedirects(string input, string expectedResult)
    {
        AuthController.SanitizeRedirectUrl(input).Should().Be(expectedResult);
    }

    [Test]
    public void ChallengeProvider_WithOpenRedirect_SanitizesRedirectUri()
    {
        var result = this.controller.ChallengeProvider("google", "https://evil.com");
        result.Should().BeOfType<ChallengeResult>();

        var challenge = (ChallengeResult)result;
        challenge.Properties.RedirectUri.Should().Be("/");
    }

    [Test]
    public void ChallengeProvider_WithValidLocalUrl_PreservesRedirectUri()
    {
        var result = this.controller.ChallengeProvider("google", "/settings");
        result.Should().BeOfType<ChallengeResult>();

        var challenge = (ChallengeResult)result;
        challenge.Properties.RedirectUri.Should().Be("/settings");
    }

    [Test]
    public void ChallengeSaml_WithOpenRedirect_SanitizesRelayState()
    {
        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com/sso",
            IsEnabled = true,
        });

        var result = this.controller.ChallengeSaml("saml1", "https://evil.com");
        result.Should().BeOfType<RedirectResult>();

        var redirect = (RedirectResult)result;
        redirect.Url.Should().Contain("RelayState=%2F");
        redirect.Url.Should().NotContain("evil.com");
    }

    #endregion

    #region SAML XML Signature Wrapping (XSW) Tests

    [Test]
    public async Task SamlCallback_WhenResponseContainsDuplicateAssertion_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_orig_123";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "legit@example.com",
            "User",
            injectDuplicateAssertion: true);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/settings");

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)result;
        unauthorizedResult.StatusCode.Should().Be(401);
        unauthorizedResult.Value.ToString().Should().Contain("duplicate Assertion");
    }

    [Test]
    public async Task SamlCallback_WhenResponseContainsDuplicateResponse_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_resp_dup";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "user@example.com",
            "User",
            injectDuplicateAssertion: false);

        // Duplicate the Response element
        var rawXml = Encoding.UTF8.GetString(Convert.FromBase64String(b64Saml));
        var xmlWithDuplicateResponse = $"<wrapper>{rawXml}{rawXml}</wrapper>";
        var b64WithDup = Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlWithDuplicateResponse));

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        var result = await this.controller.SamlCallback("saml1", b64WithDup, "/settings");

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)result;
        unauthorizedResult.StatusCode.Should().Be(401);
        unauthorizedResult.Value.ToString().Should().Contain("duplicate Response");
    }

    [Test]
    public async Task SamlCallback_WhenValidSignedSamlProvided_SanitizesOpenRedirectRelayState()
    {
        const string assertionId = "_assertion_valid_123";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "legit@example.com",
            "User",
            injectDuplicateAssertion: false);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername("legit").Returns(new User
        {
            Id = 99,
            Username = "legit",
            Email = "legit@example.com",
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "https://evil.com");

        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().Be("/");
    }

    [Test]
    public async Task SamlCallback_WhenValidSignedSamlProvided_PreservesLocalRelayState()
    {
        const string assertionId = "_assertion_valid_456";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "alice@example.com",
            "User",
            injectDuplicateAssertion: false);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername("alice").Returns(new User
        {
            Id = 101,
            Username = "alice",
            Email = "alice@example.com",
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/torrents");

        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().Be("/torrents");
    }

    #endregion

    #region SubjectConfirmationData & AudienceRestriction Tests

    [Test]
    public async Task SamlCallback_WhenSubjectConfirmationRecipientMismatched_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_wrong_recip";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "user@example.com",
            "User",
            recipient: "https://evil.com/api/v1/auth/callback/saml/saml1");

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/settings");

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)result;
        unauthorizedResult.StatusCode.Should().Be(401);
        unauthorizedResult.Value.ToString().Should().Contain("Recipient mismatch");
    }

    [Test]
    public async Task SamlCallback_WhenSubjectConfirmationExpired_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_expired_sc";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "user@example.com",
            "User",
            recipient: "https://leecharr.local/api/v1/auth/callback/saml/saml1",
            subjectNotOnOrAfter: DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ"));

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/settings");

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)result;
        unauthorizedResult.StatusCode.Should().Be(401);
        unauthorizedResult.Value.ToString().Should().Contain("SubjectConfirmation has expired");
    }

    [Test]
    public async Task SamlCallback_WhenAudienceRestrictionMismatched_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_wrong_aud";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "user@example.com",
            "User",
            recipient: "https://leecharr.local/api/v1/auth/callback/saml/saml1",
            audience: "https://other-app.com/saml/metadata");

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/settings");

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)result;
        unauthorizedResult.StatusCode.Should().Be(401);
        unauthorizedResult.Value.ToString().Should().Contain("AudienceRestriction does not include this SP");
    }

    [Test]
    public async Task SamlCallback_WhenValidSubjectConfirmationAndAudience_Succeeds()
    {
        const string assertionId = "_assertion_valid_sc_aud";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "alice@example.com",
            "User",
            recipient: "https://leecharr.local/api/v1/auth/callback/saml/saml1",
            audience: "https://leecharr.local/saml/metadata");

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername("alice").Returns(new User
        {
            Id = 101,
            Username = "alice",
            Email = "alice@example.com",
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/torrents");

        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().Be("/torrents");
    }

    #endregion

    private static (string SamlResponseBase64, string CertBase64) CreateSignedSamlResponse(
        string assertionId,
        string nameId,
        string role,
        bool injectDuplicateAssertion = false,
        string recipient = null,
        string subjectNotOnOrAfter = null,
        string audience = null)
    {
        using var rsa = RSA.Create(2048);
        var certReq = new CertificateRequest("CN=SamlTestIdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Cert));

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notBefore = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notOnOrAfter = DateTime.UtcNow.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var subjectConfirmationXml = string.Empty;
        if (!string.IsNullOrEmpty(recipient) || !string.IsNullOrEmpty(subjectNotOnOrAfter))
        {
            var recipAttr = string.IsNullOrEmpty(recipient) ? string.Empty : " Recipient=\"" + recipient + "\"";
            var notOnOrAfterAttr = string.IsNullOrEmpty(subjectNotOnOrAfter) ? string.Empty : " NotOnOrAfter=\"" + subjectNotOnOrAfter + "\"";
            subjectConfirmationXml = "<saml:SubjectConfirmation Method=\"urn:oasis:names:tc:SAML:2.0:cm:bearer\"><saml:SubjectConfirmationData" + recipAttr + notOnOrAfterAttr + "/></saml:SubjectConfirmation>";
        }

        var audienceRestrictionXml = string.Empty;
        if (!string.IsNullOrEmpty(audience))
        {
            audienceRestrictionXml = "<saml:AudienceRestriction><saml:Audience>" + audience + "</saml:Audience></saml:AudienceRestriction>";
        }

        var xml = $@"<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol""
                                   xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion""
                                   ID=""_resp_{Guid.NewGuid():N}"" Version=""2.0"" IssueInstant=""{now}"">
  <saml:Issuer>https://idp.example.com</saml:Issuer>
  <samlp:Status>
    <samlp:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success""/>
  </samlp:Status>
  <saml:Assertion ID=""{assertionId}"" Version=""2.0"" IssueInstant=""{now}"">
    <saml:Issuer>https://idp.example.com</saml:Issuer>
    <saml:Subject>
      <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{nameId}</saml:NameID>
      {subjectConfirmationXml}
    </saml:Subject>
    <saml:Conditions NotBefore=""{notBefore}"" NotOnOrAfter=""{notOnOrAfter}"">
      {audienceRestrictionXml}
    </saml:Conditions>
    <saml:AttributeStatement>
      <saml:Attribute Name=""email"">
        <saml:AttributeValue>{nameId}</saml:AttributeValue>
      </saml:Attribute>
      <saml:Attribute Name=""role"">
        <saml:AttributeValue>{role}</saml:AttributeValue>
      </saml:Attribute>
    </saml:AttributeStatement>
  </saml:Assertion>
</samlp:Response>";

        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        xmlDoc.LoadXml(xml);

        var assertionElem = (XmlElement)xmlDoc.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0];
        var signedXml = new SignedXml(assertionElem) { SigningKey = rsa };

        var reference = new Reference($"#{assertionId}");
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var xmlDigitalSignature = signedXml.GetXml();

        assertionElem.InsertBefore(xmlDoc.ImportNode(xmlDigitalSignature, true), assertionElem.FirstChild);

        if (injectDuplicateAssertion)
        {
            var forgedAssertion = xmlDoc.CreateElement("saml", "Assertion", "urn:oasis:names:tc:SAML:2.0:assertion");
            forgedAssertion.SetAttribute("ID", "_forged_assertion");
            forgedAssertion.InnerXml = @"<saml:Issuer xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">https://idp.example.com</saml:Issuer>
<saml:Subject xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
  <saml:NameID>admin@evil.com</saml:NameID>
</saml:Subject>";
            xmlDoc.DocumentElement.AppendChild(forgedAssertion);
        }

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlDoc.OuterXml));
        return (b64, certBase64);
    }
}
