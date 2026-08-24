using System;
using System.Collections.Generic;
using Leecharr.Http.REST;
using NzbDrone.Core.Authentication;

namespace Leecharr.Api.V1.Auth;

public class AuthProviderResource : RestResource
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IdentityProviderType ProviderType { get; set; }
    public string IconUrl { get; set; }
    public string ButtonText { get; set; }
    public string LoginUrl { get; set; } = string.Empty;
}

public class LoginRequestResource
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = true;
}

public class CurrentUserResource : RestResource
{
    public Guid Identifier { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public List<string> Roles { get; set; } = new List<string>();
    public string AvatarUrl { get; set; }
    public bool IsAuthenticated { get; set; }
}
