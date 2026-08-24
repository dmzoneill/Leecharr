using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
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

    public AuthController(
        IUserService userService,
        IIdentityProviderService identityProviderService,
        IConfigFileProvider configFileProvider,
        IConfigService configService)
    {
        _userService = userService;
        _identityProviderService = identityProviderService;
        _configFileProvider = configFileProvider;
        _configService = configService;
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
