// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Authentication;

public class BasicAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "Basic";
}

public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
{
    private readonly IConfigFileProvider configFileProvider;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider)
        : base(options, logger, encoder)
    {
        this.configFileProvider = configFileProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!this.Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(this.Request.Headers["Authorization"]);
            if (!string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
            var username = credentials.Length > 0 ? credentials[0] : string.Empty;
            var password = credentials.Length > 1 ? credentials[1] : string.Empty;

            var configuredApiKey = this.configFileProvider.ApiKey;

            // Allow auth if password or username matches API key or authentication is disabled
            if (!this.configFileProvider.AuthenticationEnabled ||
                (!string.IsNullOrWhiteSpace(configuredApiKey) && (RpcAuthenticationHelper.FixedTimeEquals(password, configuredApiKey) || RpcAuthenticationHelper.FixedTimeEquals(username, configuredApiKey))))
            {
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

            return Task.FromResult(AuthenticateResult.Fail("Invalid Basic authentication credentials."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Failed to parse Basic authentication header: {ex.Message}"));
        }
    }
}
