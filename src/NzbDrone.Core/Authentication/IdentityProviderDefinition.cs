// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderDefinition : ModelBase
{
    public string ProviderId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IdentityProviderType ProviderType { get; set; } = IdentityProviderType.Oidc;

    public bool IsEnabled { get; set; } = true;

    public string ClientId { get; set; }

    public string ClientSecretEncrypted { get; set; }

    public string IssuerUrl { get; set; }

    public string MetadataUrl { get; set; }

    public string Scopes { get; set; } = "openid profile email";

    public string Certificate { get; set; }

    public string RoleMappingRules { get; set; }

    public string IconUrl { get; set; }

    public string ButtonText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
