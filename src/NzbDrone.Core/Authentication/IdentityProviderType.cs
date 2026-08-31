// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Authentication;

public enum IdentityProviderType
{
    Oidc = 0,
    Saml = 1,
    Social = 2,
    ForwardAuth = 3,
}
