using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NzbDrone.Core.Authentication;

namespace Leecharr.Http.Authentication;

public class DynamicAuthSchemeManager : IDynamicAuthSchemeManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IIdentityProviderRepository _identityProviderRepository;
    private readonly IJitUserProvisioningService _jitUserProvisioning;
    private readonly Logger _logger;

    public DynamicAuthSchemeManager(
        IServiceProvider serviceProvider,
        IIdentityProviderRepository identityProviderRepository,
        IJitUserProvisioningService jitUserProvisioning,
        Logger logger)
    {
        _serviceProvider = serviceProvider;
        _identityProviderRepository = identityProviderRepository;
        _jitUserProvisioning = jitUserProvisioning;
        _logger = logger;
    }

    public async Task InitializeConfiguredProvidersAsync()
    {
        try
        {
            var enabledProviders = _identityProviderRepository.GetEnabled();
            foreach (var provider in enabledProviders)
            {
                if (provider.ProviderType == IdentityProviderType.Oidc || provider.ProviderType == IdentityProviderType.Social)
                {
                    await RegisterOrUpdateOidcProviderAsync(provider);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize configured dynamic authentication schemes");
        }
    }

    public async Task RegisterOrUpdateOidcProviderAsync(IdentityProviderDefinition provider)
    {
        var schemeName = $"Oidc_{provider.ProviderId}";
        var oidcOptionsCache = _serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var oidcPostConfigure = _serviceProvider.GetService<IPostConfigureOptions<OpenIdConnectOptions>>();
        var schemeProvider = _serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var dataProtection = _serviceProvider.GetService<IDataProtectionProvider>();

        if (oidcOptionsCache == null || schemeProvider == null)
        {
            return;
        }

        oidcOptionsCache.TryRemove(schemeName);

        if (string.IsNullOrWhiteSpace(provider.IssuerUrl) || string.IsNullOrWhiteSpace(provider.ClientId))
        {
            return;
        }

        var options = new OpenIdConnectOptions
        {
            SignInScheme = "Cookies",
            Authority = provider.IssuerUrl.TrimEnd('/'),
            ClientId = provider.ClientId,
            ClientSecret = provider.ClientSecretEncrypted ?? string.Empty,
            ResponseType = OpenIdConnectResponseType.Code,
            ResponseMode = OpenIdConnectResponseMode.Query,
            GetClaimsFromUserInfoEndpoint = true,
            SaveTokens = true,
            CallbackPath = $"/signin-oidc-{provider.ProviderId}",
            RequireHttpsMetadata = provider.IssuerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
            DataProtectionProvider = dataProtection,
            Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    var claims = context.Principal?.Claims.ToList() ?? new();
                    var sub = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")?.Value
                              ?? Guid.NewGuid().ToString();
                    var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "preferred_username" || c.Type == "nickname")?.Value
                                   ?? sub;
                    var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
                    var displayName = claims.FirstOrDefault(c => c.Type == "name")?.Value ?? username;
                    var avatarUrl = claims.FirstOrDefault(c => c.Type == "picture" || c.Type == "avatar_url")?.Value;
                    var groups = claims.Where(c => c.Type == "groups" || c.Type == "roles" || c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

                    var profile = new ExternalUserProfile(
                        provider.ProviderId,
                        sub,
                        username,
                        email,
                        displayName,
                        groups,
                        avatarUrl);

                    _jitUserProvisioning.ProvisionOrUpdateUser(profile);

                    return Task.CompletedTask;
                }
            }
        };

        options.Scope.Clear();
        var scopes = (provider.Scopes ?? "openid profile email").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var scope in scopes)
        {
            options.Scope.Add(scope);
        }

        if (oidcPostConfigure != null)
        {
            oidcPostConfigure.PostConfigure(schemeName, options);
        }

        oidcOptionsCache.TryAdd(schemeName, options);

        var existingScheme = await schemeProvider.GetSchemeAsync(schemeName);
        if (existingScheme != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        var newScheme = new AuthenticationScheme(schemeName, provider.Name, typeof(OpenIdConnectHandler));
        schemeProvider.AddScheme(newScheme);

        _logger.Info("Registered dynamic OIDC authentication scheme: {0} ({1})", schemeName, provider.Name);
    }

    public async Task RemoveProviderSchemeAsync(string providerId)
    {
        var schemeName = $"Oidc_{providerId}";
        var schemeProvider = _serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var oidcOptionsCache = _serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();

        if (schemeProvider != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        if (oidcOptionsCache != null)
        {
            oidcOptionsCache.TryRemove(schemeName);
        }

        _logger.Info("Removed dynamic authentication scheme: {0}", schemeName);
        await Task.CompletedTask;
    }
}
