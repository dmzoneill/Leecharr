// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";

    public string HeaderName { get; set; } = "X-Api-Key";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IConfigFileProvider configFileProvider;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider)
        : base(options, logger, encoder)
    {
        this.configFileProvider = configFileProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!this.configFileProvider.AuthenticationEnabled)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Name, "Admin"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "Operator"),
                new Claim(ClaimTypes.Role, "User"),
            };
            var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.DefaultScheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationOptions.DefaultScheme)));
        }

        var apiKey = this.Request.Headers[this.Options.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey) && this.Request.Headers.TryGetValue("ApiKey", out var customApiKey))
        {
            apiKey = customApiKey.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(apiKey) &&
            this.Request.Headers.TryGetValue("Authorization", out var authHeaderVal) &&
            authHeaderVal.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            apiKey = authHeaderVal.ToString()["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var masterApiKey = this.configFileProvider.ApiKey;
        if (!string.IsNullOrEmpty(masterApiKey) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(apiKey),
                Encoding.UTF8.GetBytes(masterApiKey)))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "0"),
                new Claim(ClaimTypes.Name, "MasterApiKey"),
                new Claim(ClaimTypes.Role, "Admin"),
            };
            var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.DefaultScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
    }
}
