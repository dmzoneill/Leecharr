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

    [Test]
    public async Task SamlCallback_WhenValidInResponseToProvided_AuthenticatesSuccessfully()
    {
        const string requestId = "_req_123456";
        AuthController.RegisterPendingSamlRequest(requestId);

        const string assertionId = "_assertion_in_resp_valid";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "bob@example.com",
            "User",
            injectDuplicateAssertion: false,
            inResponseTo: requestId);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername("bob").Returns(new User
        {
            Id = 105,
            Username = "bob",
            Email = "bob@example.com",
        });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/settings");

        result.Should().BeOfType<RedirectResult>();
    }

    [Test]
    public async Task SamlCallback_WhenUnknownInResponseToProvided_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_in_resp_invalid";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "bob@example.com",
            "User",
            injectDuplicateAssertion: false,
            inResponseTo: "_unknown_unsolicited_request_id");

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
        unauthorizedResult.Value.ToString().Should().Contain("InResponseTo");
    }

    [Test]
    public async Task SamlCallback_WhenAssertionReplayed_RejectsSecondAttemptWithUnauthorized()
    {
        const string assertionId = "_assertion_replay_test_999";
        const string requestId = "_req_replay_test_999";
        AuthController.RegisterPendingSamlRequest(requestId);

        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "carol@example.com",
            "User",
            injectDuplicateAssertion: false,
            inResponseTo: requestId);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername("carol").Returns(new User
        {
            Id = 106,
            Username = "carol",
            Email = "carol@example.com",
        });

        var firstResult = await this.controller.SamlCallback("saml1", b64Saml, "/settings");
        firstResult.Should().BeOfType<RedirectResult>();

        // Re-register the InResponseTo so we specifically verify the assertion replay check
        // (simulating an attacker re-injecting a previously seen assertion into a newly initiated login flow)
        AuthController.RegisterPendingSamlRequest(requestId);

        // Second submission of the exact same assertion must fail as a replay
        var secondResult = await this.controller.SamlCallback("saml1", b64Saml, "/settings");
        secondResult.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)secondResult;
        unauthorizedResult.Value.ToString().Should().Contain("replay detected");
    }

    [Test]
    public async Task SamlCallback_WhenInResponseToMissing_RejectsWithUnauthorized()
    {
        const string assertionId = "_assertion_missing_in_resp";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "bob@example.com",
            "User",
            injectDuplicateAssertion: false,
            inResponseTo: string.Empty);

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
        unauthorizedResult.Value.ToString().Should().Be("SAML response is missing required InResponseTo attribute (unsolicited responses rejected).");
    }

    [Test]
    public async Task SamlCallback_WhenFallbackJitUserProvisioningWithUnrecognizedRoles_DefaultsToReadOnly()
    {
        const string assertionId = "_assertion_fallback_jit_unrecognized";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "newuser@example.com",
            "Engineering_Team",
            injectDuplicateAssertion: false);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername(Arg.Any<string>()).Returns((User)null);
        this.userService.HasAnyUsers().Returns(true);

        List<string> assignedRoles = null;
        this.userService.CreateUser(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<List<string>>(r => assignedRoles = r))
            .Returns(callInfo => new User
            {
                Id = 200,
                Username = callInfo.ArgAt<string>(0),
                Email = callInfo.ArgAt<string>(2),
                DisplayName = callInfo.ArgAt<string>(3),
                Roles = System.Text.Json.JsonSerializer.Serialize(callInfo.ArgAt<List<string>>(4)),
            });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/torrents");

        result.Should().BeOfType<RedirectResult>();
        assignedRoles.Should().NotBeNull();
        assignedRoles.Should().ContainSingle().Which.Should().Be("ReadOnly");
    }

    [Test]
    public async Task SamlCallback_WhenFallbackJitUserProvisioningWithRecognizedRole_AssignsRecognizedRole()
    {
        const string assertionId = "_assertion_fallback_jit_operator";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "operator_user@example.com",
            "Operator",
            injectDuplicateAssertion: false);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername(Arg.Any<string>()).Returns((User)null);
        this.userService.HasAnyUsers().Returns(true);

        List<string> assignedRoles = null;
        this.userService.CreateUser(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<List<string>>(r => assignedRoles = r))
            .Returns(callInfo => new User
            {
                Id = 201,
                Username = callInfo.ArgAt<string>(0),
                Email = callInfo.ArgAt<string>(2),
                DisplayName = callInfo.ArgAt<string>(3),
                Roles = System.Text.Json.JsonSerializer.Serialize(callInfo.ArgAt<List<string>>(4)),
            });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/torrents");

        result.Should().BeOfType<RedirectResult>();
        assignedRoles.Should().NotBeNull();
        assignedRoles.Should().ContainSingle().Which.Should().Be("Operator");
    }

    [Test]
    public async Task SamlCallback_WhenFallbackJitUserProvisioningFirstUser_AssignsAdminRole()
    {
        const string assertionId = "_assertion_fallback_jit_firstuser";
        var (b64Saml, certB64) = CreateSignedSamlResponse(
            assertionId,
            "firstuser@example.com",
            "Engineering",
            injectDuplicateAssertion: false);

        this.identityProviderService.GetByProviderId("saml1").Returns(new IdentityProviderDefinition
        {
            ProviderId = "saml1",
            ProviderType = IdentityProviderType.Saml,
            IssuerUrl = "https://idp.example.com",
            Certificate = certB64,
            IsEnabled = true,
        });

        this.userService.GetByUsername(Arg.Any<string>()).Returns((User)null);
        this.userService.HasAnyUsers().Returns(false);

        List<string> assignedRoles = null;
        this.userService.CreateUser(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<List<string>>(r => assignedRoles = r))
            .Returns(callInfo => new User
            {
                Id = 202,
                Username = callInfo.ArgAt<string>(0),
                Email = callInfo.ArgAt<string>(2),
                DisplayName = callInfo.ArgAt<string>(3),
                Roles = System.Text.Json.JsonSerializer.Serialize(callInfo.ArgAt<List<string>>(4)),
            });

        var result = await this.controller.SamlCallback("saml1", b64Saml, "/torrents");

        result.Should().BeOfType<RedirectResult>();
        assignedRoles.Should().NotBeNull();
        assignedRoles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    #endregion

    private static (string SamlResponseBase64, string CertBase64) CreateSignedSamlResponse(
        string assertionId,
        string nameId,
        string role,
        bool injectDuplicateAssertion = false,
        string inResponseTo = null,
        IReadOnlyList<string> groups = null)
    {
        if (inResponseTo == null)
        {
            inResponseTo = $"_req_{Guid.NewGuid():N}";
            AuthController.RegisterPendingSamlRequest(inResponseTo);
        }

        using var rsa = RSA.Create(2048);
        var certReq = new CertificateRequest("CN=SamlTestIdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Cert));

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notBefore = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notOnOrAfter = DateTime.UtcNow.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var inResponseToAttr = string.IsNullOrWhiteSpace(inResponseTo) ? string.Empty : $@" InResponseTo=""{inResponseTo}""";

        var extraGroupsXml = new StringBuilder();
        if (groups != null)
        {
            foreach (var g in groups)
            {
                extraGroupsXml.AppendLine($@"      <saml:Attribute Name=""group"">
        <saml:AttributeValue>{g}</saml:AttributeValue>
      </saml:Attribute>");
            }
        }

        var xml = $@"<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol""
                                   xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion""
                                   ID=""_resp_{Guid.NewGuid():N}"" Version=""2.0"" IssueInstant=""{now}""{inResponseToAttr}>
  <saml:Issuer>https://idp.example.com</saml:Issuer>
  <samlp:Status>
    <samlp:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success""/>
  </samlp:Status>
  <saml:Assertion ID=""{assertionId}"" Version=""2.0"" IssueInstant=""{now}"">
    <saml:Issuer>https://idp.example.com</saml:Issuer>
    <saml:Subject>
      <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{nameId}</saml:NameID>
    </saml:Subject>
    <saml:Conditions NotBefore=""{notBefore}"" NotOnOrAfter=""{notOnOrAfter}""/>
    <saml:AttributeStatement>
      <saml:Attribute Name=""email"">
        <saml:AttributeValue>{nameId}</saml:AttributeValue>
      </saml:Attribute>
      <saml:Attribute Name=""role"">
        <saml:AttributeValue>{role}</saml:AttributeValue>
      </saml:Attribute>
{extraGroupsXml}    </saml:AttributeStatement>
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
