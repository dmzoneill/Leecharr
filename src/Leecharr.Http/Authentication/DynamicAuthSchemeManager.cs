// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
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
    private readonly IServiceProvider serviceProvider;
    private readonly IIdentityProviderRepository identityProviderRepository;
    private readonly IJitUserProvisioningService jitUserProvisioning;
    private readonly Logger logger;

    public DynamicAuthSchemeManager(
        IServiceProvider serviceProvider,
        IIdentityProviderRepository identityProviderRepository,
        IJitUserProvisioningService jitUserProvisioning,
        Logger logger)
    {
        this.serviceProvider = serviceProvider;
        this.identityProviderRepository = identityProviderRepository;
        this.jitUserProvisioning = jitUserProvisioning;
        this.logger = logger;
    }

    public async Task InitializeConfiguredProvidersAsync()
    {
        try
        {
            var enabledProviders = this.identityProviderRepository.GetEnabled();
            foreach (var provider in enabledProviders)
            {
                if (provider.ProviderType == IdentityProviderType.Oidc || provider.ProviderType == IdentityProviderType.Social)
                {
                    await this.RegisterOrUpdateOidcProviderAsync(provider);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to initialize configured dynamic authentication schemes");
        }
    }

    public async Task RegisterOrUpdateOidcProviderAsync(IdentityProviderDefinition provider)
    {
        var schemeName = $"Oidc_{provider.ProviderId}";
        var oidcOptionsCache = this.serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var oidcPostConfigure = this.serviceProvider.GetService<IPostConfigureOptions<OpenIdConnectOptions>>();
        var schemeProvider = this.serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var dataProtection = this.serviceProvider.GetService<IDataProtectionProvider>();

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

                    var user = this.jitUserProvisioning.ProvisionOrUpdateUser(profile);

                    var rolesList = new List<string>();
                    try
                    {
                        if (user != null && !string.IsNullOrEmpty(user.Roles))
                        {
                            rolesList = JsonSerializer.Deserialize<List<string>>(user.Roles) ?? new List<string> { "User" };
                        }
                    }
                    catch
                    {
                        rolesList = new List<string> { "User" };
                    }

                    if (rolesList.Count == 0)
                    {
                        rolesList.Add("User");
                    }

                    var userId = user?.Id.ToString() ?? sub;
                    var finalUsername = user?.Username ?? username;
                    var finalDisplayName = user?.DisplayName ?? displayName;
                    var finalEmail = user?.Email ?? email;

                    var sessionToken = Guid.NewGuid().ToString("N");
                    var userClaims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, userId),
                        new(ClaimTypes.Name, finalUsername),
                        new("DisplayName", finalDisplayName),
                        new("SessionId", sessionToken),
                        new("TicketId", sessionToken),
                        new("SessionToken", sessionToken),
                    };

                    if (!string.IsNullOrEmpty(finalEmail))
                    {
                        userClaims.Add(new Claim(ClaimTypes.Email, finalEmail));
                    }

                    foreach (var role in rolesList)
                    {
                        userClaims.Add(new Claim(ClaimTypes.Role, role));
                    }

                    var identity = new ClaimsIdentity(userClaims, "Cookies");
                    context.Principal = new ClaimsPrincipal(identity);

                    var userSessionRepository = context.HttpContext?.RequestServices?.GetService<IUserSessionRepository>() ??
                                               this.serviceProvider?.GetService<IUserSessionRepository>();
                    if (userSessionRepository != null && user != null)
                    {
                        try
                        {
                            var session = new UserSession
                            {
                                UserId = user.Id,
                                SessionToken = sessionToken,
                                IpAddress = context.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                                UserAgent = context.HttpContext?.Request?.Headers?["User-Agent"].ToString() ?? string.Empty,
                                CreatedAt = DateTime.UtcNow,
                                Expiry = DateTime.UtcNow.AddDays(30),
                                LastActivity = DateTime.UtcNow,
                            };
                            userSessionRepository.Insert(session);
                        }
                        catch (Exception ex)
                        {
                            this.logger.Error(ex, "Failed to insert UserSession for OIDC user {0}", finalUsername);
                        }
                    }

                    return Task.CompletedTask;
                },
            },
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

        this.logger.Info("Registered dynamic OIDC authentication scheme: {0} ({1})", schemeName, provider.Name);
    }

    public async Task RemoveProviderSchemeAsync(string providerId)
    {
        var schemeName = $"Oidc_{providerId}";
        var schemeProvider = this.serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var oidcOptionsCache = this.serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();

        if (schemeProvider != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        if (oidcOptionsCache != null)
        {
            oidcOptionsCache.TryRemove(schemeName);
        }

        this.logger.Info("Removed dynamic authentication scheme: {0}", schemeName);
        await Task.CompletedTask;
    }
}
