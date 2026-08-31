// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Authentication;

public class ForwardAuthOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ForwardAuth";

    public string UsernameHeaders { get; set; } = "X-authentik-username;Remote-User;X-Forwarded-User";

    public string EmailHeaders { get; set; } = "X-authentik-email;Remote-Email;X-Forwarded-Email";

    public string DisplayNameHeaders { get; set; } = "X-authentik-name;Remote-Name;X-Forwarded-Preferred-Username";

    public string GroupsHeaders { get; set; } = "X-authentik-groups;Remote-Groups;X-Forwarded-Groups";
}

public class ForwardAuthHandler : AuthenticationHandler<ForwardAuthOptions>
{
    private readonly ITrustedNetworkService trustedNetworkService;
    private readonly IJitUserProvisioningService jitProvisioningService;
    private readonly IConfigService configService;

    public ForwardAuthHandler(
        IOptionsMonitor<ForwardAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITrustedNetworkService trustedNetworkService,
        IJitUserProvisioningService jitProvisioningService,
        IConfigService configService)
        : base(options, logger, encoder)
    {
        this.trustedNetworkService = trustedNetworkService;
        this.jitProvisioningService = jitProvisioningService;
        this.configService = configService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var trustedCidrs = this.configService.GetValue("ForwardAuthTrustedProxies", string.Empty);
        var remoteIp = this.Request.HttpContext.Connection.RemoteIpAddress;

        if (!this.trustedNetworkService.IsTrustedProxy(remoteIp, trustedCidrs))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = this.GetHeaderValue(this.Options.UsernameHeaders);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var email = this.GetHeaderValue(this.Options.EmailHeaders);
        var displayName = this.GetHeaderValue(this.Options.DisplayNameHeaders) ?? username;
        var rawGroupsStr = this.GetHeaderValue(this.Options.GroupsHeaders);
        var groups = !string.IsNullOrWhiteSpace(rawGroupsStr)
            ? rawGroupsStr.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        var profile = new ExternalUserProfile(
            "forward-auth",
            username,
            username,
            email,
            displayName,
            groups);

        var user = this.jitProvisioningService.ProvisionOrUpdateUser(profile);

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

        var identity = new ClaimsIdentity(claims, ForwardAuthOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ForwardAuthOptions.DefaultScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string GetHeaderValue(string headerNames)
    {
        var candidates = headerNames.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in candidates)
        {
            if (this.Request.Headers.TryGetValue(name.Trim(), out var val) && !string.IsNullOrWhiteSpace(val.FirstOrDefault()))
            {
                return val.FirstOrDefault().Trim();
            }
        }

        return null;
    }
}
