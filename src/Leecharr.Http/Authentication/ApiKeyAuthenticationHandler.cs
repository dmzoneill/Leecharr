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
    private readonly IConfigFileProvider _configFileProvider;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider)
        : base(options, logger, encoder)
    {
        _configFileProvider = configFileProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_configFileProvider.AuthenticationEnabled)
        {
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity("NoAuth")),
                    ApiKeyAuthenticationOptions.DefaultScheme)));
        }

        var apiKey = Request.Headers[Options.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(apiKey),
            Encoding.UTF8.GetBytes(_configFileProvider.ApiKey)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "API") };
        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
