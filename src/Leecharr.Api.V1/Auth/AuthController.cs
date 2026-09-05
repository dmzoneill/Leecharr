// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Auth;

[V1ApiController("auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService userService;
    private readonly IIdentityProviderService identityProviderService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly IConfigService configService;
    private readonly IUserSessionRepository userSessionRepository;
    private readonly IJitUserProvisioningService jitUserProvisioningService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public AuthController(
        IUserService userService,
        IIdentityProviderService identityProviderService,
        IConfigFileProvider configFileProvider,
        IConfigService configService,
        IUserSessionRepository userSessionRepository = null,
        IJitUserProvisioningService jitUserProvisioningService = null)
    {
        this.userService = userService;
        this.identityProviderService = identityProviderService;
        this.configFileProvider = configFileProvider;
        this.configService = configService;
        this.userSessionRepository = userSessionRepository;
        this.jitUserProvisioningService = jitUserProvisioningService;
    }

    [HttpGet("providers")]
    [AllowAnonymous]
    public ActionResult<List<AuthProviderResource>> GetProviders()
    {
        var providers = new List<AuthProviderResource>();
        var basePath = this.Request.PathBase.HasValue ? this.Request.PathBase.Value : string.Empty;

        var enabledProviders = this.identityProviderService.GetEnabled();
        foreach (var p in enabledProviders)
        {
            providers.Add(new AuthProviderResource
            {
                Id = p.Id,
                ProviderId = p.ProviderId,
                Name = p.Name,
                ProviderType = p.ProviderType,
                IconUrl = p.IconUrl,
                ButtonText = p.ButtonText ?? $"Sign in with {p.Name}",
                LoginUrl = p.ProviderType == IdentityProviderType.Saml
                    ? $"{basePath}/api/v1/auth/login/saml/{p.ProviderId}"
                    : $"{basePath}/api/v1/auth/login/{p.ProviderId}",
            });
        }

        return this.Ok(providers);
    }

    [HttpPost("login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserResource>> Login([FromBody] LoginRequestResource request, [FromQuery] string returnUrl = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return this.BadRequest(new { error = "Username and password are required" });
        }

        var user = this.userService.Authenticate(request.Username, request.Password);
        if (user == null)
        {
            return this.Unauthorized(new { error = "Invalid username or password" });
        }

        var rolesList = new List<string>();
        try
        {
            if (!string.IsNullOrEmpty(user.Roles))
            {
                rolesList = JsonSerializer.Deserialize<List<string>>(user.Roles) ?? new List<string> { "User" };
            }
        }
        catch
        {
            rolesList = new List<string> { "User" };
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("DisplayName", user.DisplayName ?? user.Username),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in rolesList)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var sessionToken = Guid.NewGuid().ToString("N");
        claims.Add(new Claim("SessionId", sessionToken));
        claims.Add(new Claim("TicketId", sessionToken));
        claims.Add(new Claim("SessionToken", sessionToken));

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        var authProps = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8),
        };

        await this.HttpContext.SignInAsync("Cookies", principal, authProps);

        if (this.userSessionRepository != null)
        {
            try
            {
                var session = new UserSession
                {
                    UserId = user.Id,
                    SessionToken = sessionToken,
                    IpAddress = this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    UserAgent = this.Request.Headers["User-Agent"].ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Expiry = (authProps.ExpiresUtc ?? DateTimeOffset.UtcNow.AddDays(30)).UtcDateTime,
                    LastActivity = DateTime.UtcNow,
                };
                this.userSessionRepository.Insert(session);
            }
            catch
            {
                // Non-fatal if session insertion fails
            }
        }

        var requestedUrl = returnUrl ?? request?.ReturnUrl;
        var safeReturnUrl = SanitizeRedirectUrl(requestedUrl);

        return this.Ok(new CurrentUserResource
        {
            Id = user.Id,
            Identifier = user.Identifier,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = rolesList,
            AvatarUrl = user.AvatarUrl,
            IsAuthenticated = true,
            ReturnUrl = safeReturnUrl,
        });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout()
    {
        var sessionToken = this.User.FindFirst("SessionId")?.Value ??
                           this.User.FindFirst("TicketId")?.Value ??
                           this.User.FindFirst("SessionToken")?.Value;

        if (!string.IsNullOrEmpty(sessionToken) && this.userSessionRepository != null)
        {
            try
            {
                var session = this.userSessionRepository.FindBySessionToken(sessionToken);
                if (session != null)
                {
                    this.userSessionRepository.Delete(session.Id);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Failed to remove session record upon logout.");
            }
        }

        await this.HttpContext.SignOutAsync("Cookies");
        return this.Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public ActionResult<CurrentUserResource> GetCurrentUser()
    {
        if (!this.User.Identity?.IsAuthenticated ?? true)
        {
            if (!this.configFileProvider.AuthenticationEnabled)
            {
                return this.Ok(new CurrentUserResource
                {
                    Username = "admin",
                    DisplayName = "Administrator",
                    Roles = new List<string> { "Admin" },
                    IsAuthenticated = true,
                });
            }

            return this.Ok(new CurrentUserResource
            {
                IsAuthenticated = false,
            });
        }

        var username = this.User.Identity?.Name ?? "User";
        var email = this.User.FindFirst(ClaimTypes.Email)?.Value;
        var displayName = this.User.FindFirst("DisplayName")?.Value ?? username;
        var roles = this.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (roles.Count == 0)
        {
            roles.Add("User");
        }

        return this.Ok(new CurrentUserResource
        {
            Username = username,
            Email = email,
            DisplayName = displayName,
            Roles = roles,
            IsAuthenticated = true,
        });
    }

    [HttpGet("login/{providerId}")]
    [AllowAnonymous]
    public ActionResult ChallengeProvider(string providerId, [FromQuery] string returnUrl = "/")
    {
        var safeReturnUrl = SanitizeRedirectUrl(returnUrl);
        var schemeName = $"Oidc_{providerId}";
        var props = new AuthenticationProperties
        {
            RedirectUri = safeReturnUrl,
        };

        return this.Challenge(props, schemeName);
    }

    [HttpGet("login/saml/{providerId}")]
    [AllowAnonymous]
    public ActionResult ChallengeSaml(string providerId, [FromQuery] string returnUrl = "/")
    {
        var safeReturnUrl = SanitizeRedirectUrl(returnUrl);
        var provider = this.identityProviderService.GetByProviderId(providerId);
        if (provider == null || provider.ProviderType != IdentityProviderType.Saml || string.IsNullOrWhiteSpace(provider.IssuerUrl))
        {
            return this.NotFound("SAML Identity Provider not found");
        }

        var baseUrl = $"{this.Request.Scheme}://{this.Request.Host}{this.Request.PathBase}";
        var acsUrl = $"{baseUrl}/api/v1/auth/callback/saml/{providerId}";
        var id = "_" + Guid.NewGuid().ToString("N");
        var issueInstant = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var samlRequest = $@"<samlp:AuthnRequest xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""{id}"" Version=""2.0"" IssueInstant=""{issueInstant}"" Destination=""{provider.IssuerUrl}"" AssertionConsumerServiceURL=""{acsUrl}""><saml:Issuer>{baseUrl}/saml/metadata</saml:Issuer><samlp:NameIDPolicy Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"" AllowCreate=""true""/></samlp:AuthnRequest>";

        var b64Request = Convert.ToBase64String(Encoding.UTF8.GetBytes(samlRequest));
        var redirectUrl = $"{provider.IssuerUrl}{(provider.IssuerUrl.Contains('?') ? "&" : "?")}SAMLRequest={Uri.EscapeDataString(b64Request)}&RelayState={Uri.EscapeDataString(safeReturnUrl)}";

        return this.Redirect(redirectUrl);
    }

    [HttpPost("callback/saml/{providerId?}")]
    [HttpPost("callback/saml")]
    [AllowAnonymous]
    public async Task<ActionResult> SamlCallback(
        [FromRoute] string providerId = null,
        [FromForm(Name = "SAMLResponse")] string samlResponse = null,
        [FromForm(Name = "RelayState")] string relayState = "/")
    {
        if (string.IsNullOrWhiteSpace(samlResponse))
        {
            return this.BadRequest("Missing SAMLResponse payload");
        }

        try
        {
            var rawXml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse));
            var xmlDoc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            xmlDoc.LoadXml(rawXml);

            // 1. Resolve Identity Provider
            IdentityProviderDefinition provider = null;
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                provider = this.identityProviderService.GetByProviderId(providerId);
                if (provider == null && int.TryParse(providerId, out var pid))
                {
                    provider = this.identityProviderService.GetById(pid);
                }
            }

            if (provider == null)
            {
                var issuer = xmlDoc.DocumentElement?.GetElementsByTagName("Issuer")?.Item(0)?.InnerText;
                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    provider = this.identityProviderService.GetEnabled().FirstOrDefault(p =>
                        string.Equals(p.IssuerUrl, issuer, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.ProviderId, issuer, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (provider == null)
            {
                provider = this.identityProviderService.GetEnabled().FirstOrDefault(p => p.ProviderType == IdentityProviderType.Saml);
            }

            if (provider == null || !provider.IsEnabled)
            {
                return this.Unauthorized("SAML Identity Provider not found or is disabled");
            }

            // 2. Reject duplicate Response or Assertion elements to prevent XML Signature Wrapping (XSW)
            var responseCount = 0;
            var assertionCount = 0;
            CountSamlElements(xmlDoc.DocumentElement, ref responseCount, ref assertionCount);

            if (responseCount > 1)
            {
                return this.Unauthorized("SAML response contains duplicate Response elements (XML Signature Wrapping detected)");
            }

            if (assertionCount > 1)
            {
                return this.Unauthorized("SAML response contains duplicate Assertion elements (XML Signature Wrapping detected)");
            }

            if (assertionCount == 0)
            {
                return this.Unauthorized("SAML response contains no Assertion elements");
            }

            // 3. Validate SAML Digital Signature using System.Security.Cryptography.Xml.SignedXml
            if (string.IsNullOrWhiteSpace(provider.Certificate))
            {
                return this.Unauthorized("SAML certificate not configured for Identity Provider");
            }

            X509Certificate2 cert;
            try
            {
                var certStr = provider.Certificate.Trim();
                if (certStr.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
                {
                    var b64 = certStr
                        .Replace("-----BEGIN CERTIFICATE-----", string.Empty)
                        .Replace("-----END CERTIFICATE-----", string.Empty)
                        .Replace("\r", string.Empty)
                        .Replace("\n", string.Empty)
                        .Trim();
                    cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(b64));
                }
                else
                {
                    cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certStr));
                }
            }
            catch (Exception ex)
            {
                return this.Unauthorized($"Invalid SAML certificate in Identity Provider configuration: {ex.Message}");
            }

            var signatureNodes = xmlDoc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
            if (signatureNodes.Count == 0)
            {
                signatureNodes = xmlDoc.GetElementsByTagName("Signature");
            }

            if (signatureNodes.Count == 0)
            {
                return this.Unauthorized("SAML response is missing digital signature");
            }

            var isSignatureValid = false;
            XmlElement verifiedElement = null;
            string verifiedReferenceId = null;

            foreach (XmlElement sigElement in signatureNodes)
            {
                var signedXml = new SignedXml(xmlDoc);
                signedXml.LoadXml(sigElement);
                if (signedXml.CheckSignature(cert, true))
                {
                    if (signedXml.SignedInfo?.References?.Count > 0)
                    {
                        var reference = (Reference)signedXml.SignedInfo.References[0];
                        var uri = reference.Uri;
                        if (string.IsNullOrEmpty(uri) || uri == "#")
                        {
                            verifiedElement = xmlDoc.DocumentElement;
                        }
                        else if (uri.StartsWith('#'))
                        {
                            verifiedReferenceId = uri.Substring(1);
                            verifiedElement = FindElementById(xmlDoc.DocumentElement, verifiedReferenceId);
                        }
                    }

                    isSignatureValid = true;
                    break;
                }
            }

            if (!isSignatureValid || verifiedElement == null)
            {
                return this.Unauthorized("SAML digital signature verification failed");
            }

            var isResponseSigned = string.Equals(verifiedElement.LocalName, "Response", StringComparison.OrdinalIgnoreCase);
            var isAssertionSigned = string.Equals(verifiedElement.LocalName, "Assertion", StringComparison.OrdinalIgnoreCase);

            if (!isResponseSigned && !isAssertionSigned)
            {
                return this.Unauthorized("SAML signature does not cover Response or Assertion");
            }

            XmlElement targetAssertion = null;
            if (isAssertionSigned)
            {
                targetAssertion = verifiedElement;
            }
            else
            {
                targetAssertion = FindChildElementByLocalName(verifiedElement, "Assertion");
            }

            if (targetAssertion == null)
            {
                return this.Unauthorized("Verified SAML element contains no Assertion");
            }

            if (isAssertionSigned && !string.IsNullOrEmpty(verifiedReferenceId))
            {
                var targetId = targetAssertion.GetAttribute("ID");
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    targetId = targetAssertion.GetAttribute("id");
                }

                if (!string.Equals(targetId, verifiedReferenceId, StringComparison.Ordinal))
                {
                    return this.Unauthorized("SAML assertion ID mismatch between signature reference and assertion element");
                }
            }

            // Parse ONLY the verified assertion subtree
            var assertionDoc = XElement.Parse(targetAssertion.OuterXml);

            // 4. Validate SAML timestamp attributes (NotBefore, NotOnOrAfter)
            var now = DateTime.UtcNow;
            var allowedSkew = TimeSpan.FromMinutes(5);

            var conditionsElements = assertionDoc.Descendants().Where(e => e.Name.LocalName == "Conditions");
            foreach (var elem in conditionsElements)
            {
                var notBeforeVal = elem.Attribute("NotBefore")?.Value;
                if (!string.IsNullOrWhiteSpace(notBeforeVal) && DateTimeOffset.TryParse(notBeforeVal, out var notBefore))
                {
                    if (now < notBefore.UtcDateTime - allowedSkew)
                    {
                        return this.Unauthorized("SAML assertion is not yet valid (NotBefore constraint violation)");
                    }
                }

                var notOnOrAfterVal = elem.Attribute("NotOnOrAfter")?.Value;
                if (!string.IsNullOrWhiteSpace(notOnOrAfterVal) && DateTimeOffset.TryParse(notOnOrAfterVal, out var notOnOrAfter))
                {
                    if (now >= notOnOrAfter.UtcDateTime + allowedSkew)
                    {
                        return this.Unauthorized("SAML assertion has expired (NotOnOrAfter constraint violation)");
                    }
                }
            }

            var nameId = assertionDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NameID")?.Value;
            var email = assertionDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Attribute" && (e.Attribute("Name")?.Value?.Contains("email", StringComparison.OrdinalIgnoreCase) == true))
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeValue")?.Value ?? nameId;
            var displayName = assertionDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Attribute" && (e.Attribute("Name")?.Value?.Contains("displayName", StringComparison.OrdinalIgnoreCase) == true || e.Attribute("Name")?.Value?.Contains("name", StringComparison.OrdinalIgnoreCase) == true))
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeValue")?.Value ?? nameId;

            if (string.IsNullOrWhiteSpace(nameId) && string.IsNullOrWhiteSpace(email))
            {
                return this.Unauthorized("Unable to resolve NameID or Email from SAML response");
            }

            var rawGroups = assertionDoc.Descendants()
                .Where(e => e.Name.LocalName == "Attribute" && (e.Attribute("Name")?.Value?.Contains("role", StringComparison.OrdinalIgnoreCase) == true || e.Attribute("Name")?.Value?.Contains("group", StringComparison.OrdinalIgnoreCase) == true))
                .SelectMany(e => e.Elements().Where(v => v.Name.LocalName == "AttributeValue").Select(v => v.Value))
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList();

            var subjectId = nameId ?? email;
            var username = !string.IsNullOrWhiteSpace(email) ? email : (nameId ?? "saml_user");

            User user;
            if (this.jitUserProvisioningService != null)
            {
                var profile = new ExternalUserProfile(
                    provider.ProviderId,
                    subjectId,
                    username,
                    email,
                    displayName,
                    rawGroups);

                user = this.jitUserProvisioningService.ProvisionOrUpdateUser(profile);
            }
            else
            {
                var isFirstUser = !this.userService.HasAnyUsers();
                var roles = rawGroups.Count > 0 ? rawGroups : (isFirstUser ? new List<string> { "Admin" } : new List<string> { "User" });
                user = this.userService.CreateUser(username, Guid.NewGuid().ToString("N"), email, displayName ?? username, roles);
            }

            var rolesList = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(user.Roles))
                {
                    rolesList = JsonSerializer.Deserialize<List<string>>(user.Roles) ?? new List<string> { "User" };
                }
            }
            catch
            {
                rolesList = new List<string> { "User" };
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("DisplayName", user.DisplayName ?? user.Username),
            };

            if (!string.IsNullOrEmpty(user.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, user.Email));
            }

            foreach (var role in rolesList)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var sessionToken = Guid.NewGuid().ToString("N");
            claims.Add(new Claim("SessionId", sessionToken));
            claims.Add(new Claim("TicketId", sessionToken));
            claims.Add(new Claim("SessionToken", sessionToken));

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            await this.HttpContext.SignInAsync("Cookies", principal);

            if (this.userSessionRepository != null)
            {
                try
                {
                    var session = new UserSession
                    {
                        UserId = user.Id,
                        SessionToken = sessionToken,
                        IpAddress = this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                        UserAgent = this.Request.Headers["User-Agent"].ToString(),
                        CreatedAt = DateTime.UtcNow,
                        Expiry = DateTime.UtcNow.AddDays(30),
                        LastActivity = DateTime.UtcNow,
                    };
                    this.userSessionRepository.Insert(session);
                }
                catch
                {
                    // Non-fatal if session insertion fails
                }
            }

            var safeRedirect = SanitizeRedirectUrl(relayState);
            return this.Redirect(safeRedirect);
        }
        catch (Exception ex)
        {
            return this.BadRequest(new { error = "Failed to process SAML assertion", details = ex.Message });
        }
    }

    [HttpGet("saml/metadata")]
    [AllowAnonymous]
    public ActionResult GetSamlMetadata()
    {
        var baseUrl = $"{this.Request.Scheme}://{this.Request.Host}{this.Request.PathBase}";
        var entityId = $"{baseUrl}/saml/metadata";
        var acsUrl = $"{baseUrl}/api/v1/auth/callback/saml";

        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<md:EntityDescriptor xmlns:md=""urn:oasis:names:tc:SAML:2.0:metadata"" entityID=""{entityId}"">
  <md:SPSSODescriptor AuthnRequestsSigned=""false"" WantAssertionsSigned=""true"" protocolSupportEnumeration=""urn:oasis:names:tc:SAML:2.0:protocol"">
    <md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</md:NameIDFormat>
    <md:NameIDFormat>urn:oasis:names:tc:SAML:2.0:nameid-format:persistent</md:NameIDFormat>
    <md:AssertionConsumerService Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"" Location=""{acsUrl}"" index=""1"" isDefault=""true""/>
  </md:SPSSODescriptor>
</md:EntityDescriptor>";

        return this.Content(xml, "application/samlmetadata+xml");
    }

    public static bool IsLocalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("/\\", StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < url.Length; i++)
        {
            if (char.IsControl(url[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static string SanitizeRedirectUrl(string url)
    {
        return IsLocalUrl(url) ? url : "/";
    }

    private static void CountSamlElements(XmlNode node, ref int responseCount, ref int assertionCount)
    {
        if (node == null)
        {
            return;
        }

        if (node is XmlElement elem)
        {
            if (string.Equals(elem.LocalName, "Response", StringComparison.OrdinalIgnoreCase))
            {
                responseCount++;
            }
            else if (string.Equals(elem.LocalName, "Assertion", StringComparison.OrdinalIgnoreCase))
            {
                assertionCount++;
            }
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            CountSamlElements(child, ref responseCount, ref assertionCount);
        }
    }

    private static XmlElement FindElementById(XmlElement root, string id)
    {
        if (root == null || string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (root.GetAttribute("ID") == id || root.GetAttribute("id") == id || root.GetAttribute("AssertionURI") == id)
        {
            return root;
        }

        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is XmlElement childElem)
            {
                var found = FindElementById(childElem, id);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static XmlElement FindChildElementByLocalName(XmlElement root, string localName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is XmlElement elem)
            {
                if (string.Equals(elem.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                {
                    return elem;
                }

                var found = FindChildElementByLocalName(elem, localName);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
