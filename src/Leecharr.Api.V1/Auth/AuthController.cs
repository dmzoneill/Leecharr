using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Leecharr.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Auth;

[V1ApiController("auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IConfigService _configService;
    private readonly IUserSessionRepository _userSessionRepository;

    public AuthController(
        IUserService userService,
        IIdentityProviderService identityProviderService,
        IConfigFileProvider configFileProvider,
        IConfigService configService,
        IUserSessionRepository userSessionRepository = null)
    {
        _userService = userService;
        _identityProviderService = identityProviderService;
        _configFileProvider = configFileProvider;
        _configService = configService;
        _userSessionRepository = userSessionRepository;
    }

    [HttpGet("providers")]
    [AllowAnonymous]
    public ActionResult<List<AuthProviderResource>> GetProviders()
    {
        var providers = new List<AuthProviderResource>();

        var enabledProviders = _identityProviderService.GetEnabled();
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
                    ? $"/api/v1/auth/login/saml/{p.ProviderId}"
                    : $"/api/v1/auth/login/{p.ProviderId}"
            });
        }

        return Ok(providers);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserResource>> Login([FromBody] LoginRequestResource request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Username and password are required" });
        }

        var user = _userService.Authenticate(request.Username, request.Password);
        if (user == null)
        {
            return Unauthorized(new { error = "Invalid username or password" });
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
            new("DisplayName", user.DisplayName ?? user.Username)
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in rolesList)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        var authProps = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync("Cookies", principal, authProps);

        if (_userSessionRepository != null)
        {
            try
            {
                var session = new UserSession
                {
                    UserId = user.Id,
                    SessionToken = Guid.NewGuid().ToString("N"),
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Expiry = (authProps.ExpiresUtc ?? DateTimeOffset.UtcNow.AddDays(30)).UtcDateTime,
                    LastActivity = DateTime.UtcNow
                };
                _userSessionRepository.Insert(session);
            }
            catch
            {
                // Non-fatal if session insertion fails
            }
        }

        return Ok(new CurrentUserResource
        {
            Id = user.Id,
            Identifier = user.Identifier,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = rolesList,
            AvatarUrl = user.AvatarUrl,
            IsAuthenticated = true
        });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookies");
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public ActionResult<CurrentUserResource> GetCurrentUser()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            if (!_configFileProvider.AuthenticationEnabled)
            {
                return Ok(new CurrentUserResource
                {
                    Username = "admin",
                    DisplayName = "Administrator",
                    Roles = new List<string> { "Admin" },
                    IsAuthenticated = true
                });
            }

            return Ok(new CurrentUserResource
            {
                IsAuthenticated = false
            });
        }

        var username = User.Identity?.Name ?? "User";
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var displayName = User.FindFirst("DisplayName")?.Value ?? username;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (roles.Count == 0)
        {
            roles.Add("User");
        }

        return Ok(new CurrentUserResource
        {
            Username = username,
            Email = email,
            DisplayName = displayName,
            Roles = roles,
            IsAuthenticated = true
        });
    }

    [HttpGet("login/{providerId}")]
    [AllowAnonymous]
    public ActionResult ChallengeProvider(string providerId, [FromQuery] string returnUrl = "/")
    {
        var schemeName = $"Oidc_{providerId}";
        var props = new AuthenticationProperties
        {
            RedirectUri = returnUrl ?? "/"
        };

        return Challenge(props, schemeName);
    }

    [HttpGet("login/saml/{providerId}")]
    [AllowAnonymous]
    public ActionResult ChallengeSaml(string providerId, [FromQuery] string returnUrl = "/")
    {
        var provider = _identityProviderService.GetByProviderId(providerId);
        if (provider == null || provider.ProviderType != IdentityProviderType.Saml || string.IsNullOrWhiteSpace(provider.IssuerUrl))
        {
            return NotFound("SAML Identity Provider not found");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var acsUrl = $"{baseUrl}/api/v1/auth/callback/saml/{providerId}";
        var id = "_" + Guid.NewGuid().ToString("N");
        var issueInstant = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var samlRequest = $@"<samlp:AuthnRequest xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""{id}"" Version=""2.0"" IssueInstant=""{issueInstant}"" Destination=""{provider.IssuerUrl}"" AssertionConsumerServiceURL=""{acsUrl}""><saml:Issuer>{baseUrl}/saml/metadata</saml:Issuer><samlp:NameIDPolicy Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"" AllowCreate=""true""/></samlp:AuthnRequest>";

        var b64Request = Convert.ToBase64String(Encoding.UTF8.GetBytes(samlRequest));
        var redirectUrl = $"{provider.IssuerUrl}{(provider.IssuerUrl.Contains('?') ? "&" : "?")}SAMLRequest={Uri.EscapeDataString(b64Request)}&RelayState={Uri.EscapeDataString(returnUrl ?? "/")}";

        return Redirect(redirectUrl);
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
            return BadRequest("Missing SAMLResponse payload");
        }

        try
        {
            var rawXml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse));
            var doc = XDocument.Parse(rawXml);

            var nameId = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NameID")?.Value;
            var email = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Attribute" && (e.Attribute("Name")?.Value?.Contains("email", StringComparison.OrdinalIgnoreCase) == true))
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeValue")?.Value ?? nameId;
            var displayName = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Attribute" && (e.Attribute("Name")?.Value?.Contains("displayName", StringComparison.OrdinalIgnoreCase) == true || e.Attribute("Name")?.Value?.Contains("name", StringComparison.OrdinalIgnoreCase) == true))
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeValue")?.Value ?? nameId;

            if (string.IsNullOrWhiteSpace(nameId) && string.IsNullOrWhiteSpace(email))
            {
                return Unauthorized("Unable to resolve NameID or Email from SAML response");
            }

            var username = !string.IsNullOrWhiteSpace(email) ? email.Split('@')[0] : (nameId ?? "saml_user");
            var user = _userService.GetByUsername(username);

            var roles = doc.Descendants()
                .Where(e => e.Name.LocalName == "Attribute" && (e.Attribute("Name")?.Value?.Contains("role", StringComparison.OrdinalIgnoreCase) == true || e.Attribute("Name")?.Value?.Contains("group", StringComparison.OrdinalIgnoreCase) == true))
                .SelectMany(e => e.Elements().Where(v => v.Name.LocalName == "AttributeValue").Select(v => v.Value))
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList();

            if (roles.Count == 0)
            {
                var isFirstUser = !_userService.HasAnyUsers();
                roles.Add(isFirstUser ? "Admin" : "User");
            }

            if (user == null)
            {
                user = _userService.CreateUser(username, Guid.NewGuid().ToString("N"), email, displayName ?? username, roles);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("DisplayName", user.DisplayName ?? user.Username)
            };

            if (!string.IsNullOrEmpty(user.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, user.Email));
            }

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("Cookies", principal);

            if (_userSessionRepository != null)
            {
                try
                {
                    var session = new UserSession
                    {
                        UserId = user.Id,
                        SessionToken = Guid.NewGuid().ToString("N"),
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        CreatedAt = DateTime.UtcNow,
                        Expiry = DateTime.UtcNow.AddDays(30),
                        LastActivity = DateTime.UtcNow
                    };
                    _userSessionRepository.Insert(session);
                }
                catch
                {
                    // Non-fatal if session insertion fails
                }
            }

            return Redirect(string.IsNullOrWhiteSpace(relayState) ? "/" : relayState);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to process SAML assertion", details = ex.Message });
        }
    }

    [HttpGet("saml/metadata")]
    [AllowAnonymous]
    public ActionResult GetSamlMetadata()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
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

        return Content(xml, "application/samlmetadata+xml");
    }
}
