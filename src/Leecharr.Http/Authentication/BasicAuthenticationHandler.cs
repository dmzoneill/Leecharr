// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Authentication;

public class BasicAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "Basic";
}

public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
{
    private readonly IConfigFileProvider configFileProvider;
    private readonly IUserService userService;
    private readonly AuthRateLimiter rateLimiter;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider,
        IUserService userService = null,
        AuthRateLimiter rateLimiter = null)
        : base(options, logger, encoder)
    {
        this.configFileProvider = configFileProvider;
        this.userService = userService;
        this.rateLimiter = rateLimiter ?? AuthRateLimiter.Shared;
    }

    public static void ResetThrottling()
    {
        AuthRateLimiter.Shared.ResetAll();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!this.Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var clientIp = this.GetClientIpAddress();

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(this.Request.Headers["Authorization"]);
            if (!string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (this.rateLimiter.IsThrottled(clientIp))
            {
                return Task.FromResult(AuthenticateResult.Fail("Too many failed authentication attempts. Please try again later."));
            }

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
            var username = credentials.Length > 0 ? credentials[0] : string.Empty;
            var password = credentials.Length > 1 ? credentials[1] : string.Empty;

            var configuredApiKey = this.configFileProvider.ApiKey;

            // Allow auth if authentication is disabled or password/username matches API key
            if (!this.configFileProvider.AuthenticationEnabled ||
                (!string.IsNullOrWhiteSpace(configuredApiKey) && (RpcAuthenticationHelper.FixedTimeEquals(password, configuredApiKey) || RpcAuthenticationHelper.FixedTimeEquals(username, configuredApiKey))))
            {
                this.rateLimiter.Reset(clientIp);

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(username) ? "Admin" : username),
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(ClaimTypes.Role, "Operator"),
                    new Claim(ClaimTypes.Role, "User"),
                };
                var identity = new ClaimsIdentity(claims, BasicAuthenticationOptions.DefaultScheme);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), BasicAuthenticationOptions.DefaultScheme)));
            }

            // Authenticate against database user accounts when provided
            var resolvedUserService = this.userService ?? this.Context.RequestServices?.GetService<IUserService>();
            if (resolvedUserService != null && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                var user = resolvedUserService.Authenticate(username, password);
                if (user != null)
                {
                    this.rateLimiter.Reset(clientIp);

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

                    if (rolesList.Count == 0)
                    {
                        rolesList.Add("User");
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

                    var identity = new ClaimsIdentity(claims, BasicAuthenticationOptions.DefaultScheme);
                    return Task.FromResult(AuthenticateResult.Success(
                        new AuthenticationTicket(new ClaimsPrincipal(identity), BasicAuthenticationOptions.DefaultScheme)));
                }
            }

            this.rateLimiter.RecordFailure(clientIp);
            return Task.FromResult(AuthenticateResult.Fail("Invalid Basic authentication credentials."));
        }
        catch (Exception ex)
        {
            this.rateLimiter.RecordFailure(clientIp);
            return Task.FromResult(AuthenticateResult.Fail($"Failed to parse Basic authentication header: {ex.Message}"));
        }
    }

    private string GetClientIpAddress()
    {
        return this.Context.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }
}
